<#
.SYNOPSIS
  Shared idle-CPU measurement helpers for tools/perfcheck.ps1 and tools/perfhistory.ps1 (dot-source this file).
.DESCRIPTION
  A "variant" is { Name; Exe; Args; Kind } where Kind is 'baseline' or 'app'. The baselines come from
  tools/perfbaseline (PerfBaseline.exe --bare / --static): same window setup as the app, no animation.
  Idle runs are rotated across variants round by round (the starting variant shifts each round), so slow drift on
  the machine hits every variant alike, and each app run is compared with the baseline run of the SAME round.
#>

function Get-Median([double[]]$v) {
    if ($null -eq $v -or @($v).Count -eq 0) { return [double]::NaN }
    $s = @($v | Sort-Object)
    $n = $s.Count
    if ($n % 2) { return $s[[int](($n - 1) / 2)] }
    return ($s[$n / 2 - 1] + $s[$n / 2]) / 2
}

function Get-TreePrivateBytes([int]$rootPid) {
    $all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId
    $ids = [System.Collections.Generic.List[int]]::new(); $ids.Add($rootPid)
    for ($i = 0; $i -lt $ids.Count; $i++) {
        foreach ($c in $all | Where-Object { $_.ParentProcessId -eq $ids[$i] }) { $ids.Add([int]$c.ProcessId) }
    }
    $sum = 0
    foreach ($id in $ids) {
        $p = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($p) { $sum += $p.PrivateMemorySize64 }
    }
    return $sum
}

function Get-MachineState {
    $plan = (powercfg /getactivescheme) -join ' '
    $bat = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1
    $power = if (-not $bat) { 'no battery (desktop)' }
             elseif ($bat.BatteryStatus -in 2, 6, 7, 8, 9) { "on AC power (battery $($bat.EstimatedChargeRemaining)%)" }
             else { "ON BATTERY ($($bat.EstimatedChargeRemaining)%)" }
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    [pscustomobject]@{
        When        = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
        PowerPlan   = ($plan -replace '^Power Scheme GUID:\s*', '')
        Power       = $power
        Cpu         = "$($cpu.Name.Trim()), $($cpu.NumberOfLogicalProcessors) logical processors"
        Os          = (Get-CimInstance Win32_OperatingSystem).Caption
    }
}

# Total CPU seconds of every process we are allowed to read, except $exceptPid. Used to see how busy the rest of the PC was.
function Get-OtherCpuSeconds([int]$exceptPid) {
    $sum = 0.0
    foreach ($p in [System.Diagnostics.Process]::GetProcesses()) {
        if ($p.Id -eq $exceptPid -or $p.Id -eq $PID) { continue }
        try { $sum += $p.TotalProcessorTime.TotalSeconds } catch { }
    }
    return $sum
}

# One idle run: start, wait for the first frame, settle, measure CPU over the window, read memory.
function Measure-IdleRun($Variant, [int]$Warmup, [int]$Measure, [int]$Fps) {
    $perfFile = Join-Path $env:TEMP "companion-perf-$PID-$([guid]::NewGuid().ToString('N')).txt"
    $env:COMPANION_PERF_FILE = $perfFile
    $env:COMPANION_FPS = "$Fps"
    Remove-Item Env:COMPANION_RENDER -ErrorAction SilentlyContinue
    Get-Process Companion, PerfBaseline -ErrorAction SilentlyContinue | Stop-Process -Force
    $p = if ($Variant.Args) { Start-Process $Variant.Exe -ArgumentList $Variant.Args -PassThru } else { Start-Process $Variant.Exe -PassThru }
    try {
        $deadline = (Get-Date).AddSeconds(15)
        while (-not (Test-Path $perfFile) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 50 }
        if (-not (Test-Path $perfFile)) { throw "$($Variant.Name): the process never reported its first frame (is it running?)" }
        $startMs = [double](Get-Content $perfFile -Raw)

        Start-Sleep -Seconds $Warmup
        $p.Refresh()
        $cpu0 = $p.TotalProcessorTime.TotalSeconds; $usr0 = $p.UserProcessorTime.TotalSeconds
        $oth0 = Get-OtherCpuSeconds $p.Id; $t0 = Get-Date
        Start-Sleep -Seconds $Measure
        $p.Refresh()
        $cpu1 = $p.TotalProcessorTime.TotalSeconds; $usr1 = $p.UserProcessorTime.TotalSeconds
        $oth1 = Get-OtherCpuSeconds $p.Id; $t1 = Get-Date
        $secs = ($t1 - $t0).TotalSeconds

        [pscustomobject]@{
            Variant      = $Variant.Name
            Kind         = $Variant.Kind
            StartMs      = $startMs
            CpuPct       = ($cpu1 - $cpu0) / $secs * 100
            UserPct      = ($usr1 - $usr0) / $secs * 100
            KernelPct    = (($cpu1 - $cpu0) - ($usr1 - $usr0)) / $secs * 100
            OtherCpuPct  = ($oth1 - $oth0) / $secs * 100     # rest of the PC, in % of one core (partial: protected processes are unreadable)
            Threads      = $p.Threads.Count
            PrivateMB    = $p.PrivateMemorySize64 / 1MB
            WorkingSetMB = $p.WorkingSet64 / 1MB
            TreeMB       = (Get-TreePrivateBytes $p.Id) / 1MB
        }
    }
    finally {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        Remove-Item $perfFile -ErrorAction SilentlyContinue
        Remove-Item Env:COMPANION_PERF_FILE, Env:COMPANION_FPS -ErrorAction SilentlyContinue
    }
}

# Rounds x variants, rotated. Returns one row per run with Round and Order added.
function Invoke-IdleRotation($Variants, [int]$Rounds, [int]$Warmup, [int]$Measure, [int]$Fps) {
    $rows = @()
    $n = $Variants.Count
    for ($r = 1; $r -le $Rounds; $r++) {
        for ($k = 0; $k -lt $n; $k++) {
            $v = $Variants[(($r - 1) + $k) % $n]
            Write-Host ("Round {0}/{1}, run {2}/{3}: {4} ..." -f $r, $Rounds, ($k + 1), $n, $v.Name)
            $row = Measure-IdleRun $v $Warmup $Measure $Fps
            $row | Add-Member Round $r -PassThru | Add-Member Order ($k + 1) | Out-Null
            Write-Host ("    cpu {0:F2}% (user {1:F2}, kernel {2:F2}), rest of PC {3:F0}% of a core, private {4:F1} MB, start {5:F0} ms" -f $row.CpuPct, $row.UserPct, $row.KernelPct, $row.OtherCpuPct, $row.PrivateMB, $row.StartMs)
            $rows += $row
        }
    }
    return $rows
}

# Table: per variant the median / min-max of raw CPU, and the median of the paired difference to the bare and the static
# baseline of the same round (negative differences are kept, they show the noise).
function Write-IdleReport($Rows, $Variants) {
    $bare = @($Rows | Where-Object { $_.Variant -eq 'baseline-bare' })
    $stat = @($Rows | Where-Object { $_.Variant -eq 'baseline-static' })
    $out = @()
    foreach ($v in $Variants) {
        $mine = @($Rows | Where-Object { $_.Variant -eq $v.Name } | Sort-Object Round)
        if (-not $mine.Count) { continue }
        $dBare = @(); $dStat = @()
        foreach ($m in $mine) {
            $b = $bare | Where-Object { $_.Round -eq $m.Round } | Select-Object -First 1
            $s = $stat | Where-Object { $_.Round -eq $m.Round } | Select-Object -First 1
            if ($b) { $dBare += $m.CpuPct - $b.CpuPct }
            if ($s) { $dStat += $m.CpuPct - $s.CpuPct }
        }
        $cpu = [double[]]($mine | ForEach-Object { $_.CpuPct })
        $out += [pscustomobject]@{
            Variant       = $v.Name
            Runs          = ($cpu | ForEach-Object { '{0:F2}' -f $_ }) -join ' / '
            MedianRaw     = Get-Median $cpu
            MinRaw        = ($cpu | Measure-Object -Minimum).Minimum
            MaxRaw        = ($cpu | Measure-Object -Maximum).Maximum
            MedianMinusBare   = if ($v.Name -eq 'baseline-bare') { $null } elseif ($dBare.Count) { Get-Median ([double[]]$dBare) } else { $null }
            MedianMinusStatic = if ($v.Kind -ne 'app') { $null } elseif ($dStat.Count) { Get-Median ([double[]]$dStat) } else { $null }
            PrivateMB     = Get-Median ([double[]]($mine | ForEach-Object { $_.PrivateMB }))
            StartMs       = Get-Median ([double[]]($mine | ForEach-Object { $_.StartMs }))
            RestOfPcPct   = Get-Median ([double[]]($mine | ForEach-Object { $_.OtherCpuPct }))
        }
    }
    return $out
}

function Format-IdleTable($Summary) {
    $f = { param($x) if ($null -eq $x) { '-' } else { '{0:F2}' -f $x } }
    Write-Host ("{0,-24} {1,-22} {2,8} {3,15} {4,10} {5,12} {6,9} {7,8}" -f 'Variant', 'Runs (CPU %)', 'Median', 'Min-Max', '- bare', '- static orb', 'Priv MB', 'Rest PC')
    foreach ($s in $Summary) {
        Write-Host ("{0,-24} {1,-22} {2,8} {3,15} {4,10} {5,12} {6,9:F1} {7,7:F0}%" -f $s.Variant, $s.Runs, (& $f $s.MedianRaw), ((& $f $s.MinRaw) + '-' + (& $f $s.MaxRaw)), (& $f $s.MedianMinusBare), (& $f $s.MedianMinusStatic), $s.PrivateMB, $s.RestOfPcPct)
    }
}

function Get-BaselineVariants([string]$repo) {
    $dir = Join-Path $repo 'tools\perfbaseline'
    $proj = Join-Path $dir 'PerfBaseline.csproj'
    Write-Host "Building baseline..."
    dotnet build $proj -c Release -nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Baseline build failed" }
    $exe = Join-Path $dir 'bin\Release\net10.0-windows\PerfBaseline.exe'
    if (-not (Test-Path $exe)) { throw "Not found: $exe" }
    @(
        [pscustomobject]@{ Name = 'baseline-bare';   Exe = $exe; Args = @('--bare');   Kind = 'baseline' },
        [pscustomobject]@{ Name = 'baseline-static'; Exe = $exe; Args = @('--static'); Kind = 'baseline' }
    )
}
