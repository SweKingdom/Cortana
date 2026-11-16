<#
.SYNOPSIS
  Measures the D-052 performance budget for Companion.exe.
.DESCRIPTION
  Per run: start the app, wait for the first rendered frame (start-to-orb), warm up,
  then measure idle CPU over a window and read private bytes. Reports the median of
  -Runs runs and PASS/FAIL against D-052. Exit code 1 if any metric fails.
  Not measured yet: first word (step 2), first audio (step 6).
  Only the app process (and its children) is measured; GPU / dwm.exe cost is not counted.
#>
param(
    [ValidateSet(30, 15, 10)] [int]$Fps = 10,
    [ValidateSet("default", "hardware")] [string]$Render = "default",
    [int]$Runs = 3,
    [int]$WarmupSeconds = 30,
    [int]$MeasureSeconds = 60,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src\Companion\Companion.csproj'
$exe  = Join-Path $repo 'src\Companion\bin\Release\net10.0-windows\Companion.exe'
$outDir = Join-Path $repo 'perf-results'

# D-052 thresholds (idle CPU <2 %, median of 3)
$maxPrivateMB = 150; $maxTreeMB = 300; $maxCpuPct = 2.0; $maxStartMs = 1000

function Get-Median([double[]]$v) {
    $s = $v | Sort-Object
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

if (-not $NoBuild) {
    Write-Host "Building Release..."
    dotnet build $proj -c Release -nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}
if (-not (Test-Path $exe)) { throw "Not found: $exe" }

# A stale instance would hit the single-instance mutex and skew the result.
Get-Process Companion -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force $outDir | Out-Null

$results = @()
for ($run = 1; $run -le $Runs; $run++) {
    $perfFile = Join-Path $env:TEMP "companion-perf-$PID-$run.txt"
    Remove-Item $perfFile -ErrorAction SilentlyContinue
    $env:COMPANION_PERF_FILE = $perfFile
    $env:COMPANION_FPS = "$Fps"
    if ($Render -eq "hardware") { $env:COMPANION_RENDER = "hardware" } else { Remove-Item Env:COMPANION_RENDER -ErrorAction SilentlyContinue }

    Write-Host "Run $run/$Runs at $Fps fps ($Render render)..."
    $p = Start-Process $exe -PassThru
    try {
        $deadline = (Get-Date).AddSeconds(10)
        while (-not (Test-Path $perfFile) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 50 }
        if (-not (Test-Path $perfFile)) { throw "App never reported its first frame (is it running?)" }
        $startMs = [double](Get-Content $perfFile -Raw)

        Start-Sleep -Seconds $WarmupSeconds
        $p.Refresh()
        $cpu0 = $p.TotalProcessorTime.TotalSeconds; $t0 = Get-Date
        Start-Sleep -Seconds $MeasureSeconds
        $p.Refresh()
        $cpu1 = $p.TotalProcessorTime.TotalSeconds; $t1 = Get-Date
        $cpuPct = (($cpu1 - $cpu0) / ($t1 - $t0).TotalSeconds) * 100

        $privMB = $p.PrivateMemorySize64 / 1MB
        $wsMB = $p.WorkingSet64 / 1MB
        $treeMB = (Get-TreePrivateBytes $p.Id) / 1MB

        $results += [pscustomobject]@{ Run = $run; StartMs = $startMs; CpuPct = $cpuPct; PrivateMB = $privMB; WorkingSetMB = $wsMB; TreeMB = $treeMB }
        Write-Host ("  start {0:F0} ms, cpu {1:F3}%, private {2:F1} MB, working set {3:F1} MB, tree {4:F1} MB" -f $startMs, $cpuPct, $privMB, $wsMB, $treeMB)
    }
    finally {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        Remove-Item $perfFile -ErrorAction SilentlyContinue
    }
}
Remove-Item Env:COMPANION_PERF_FILE, Env:COMPANION_FPS, Env:COMPANION_RENDER -ErrorAction SilentlyContinue

$med = [pscustomobject]@{
    StartMs   = Get-Median ($results.StartMs)
    CpuPct    = Get-Median ($results.CpuPct)
    PrivateMB = Get-Median ($results.PrivateMB)
    TreeMB    = Get-Median ($results.TreeMB)
}

function Row($name, $value, $fmt, $limit, $unit) {
    $ok = $value -lt $limit
    Write-Host ("{0,-28} {1,10} {2}   limit < {3} {2}   {4}" -f $name, ($fmt -f $value), $unit, $limit, $(if ($ok) { 'PASS' } else { 'FAIL' }))
    return $ok
}

Write-Host ""
Write-Host "Median of $Runs runs at $Fps fps, $Render render (D-052):"
$ok = @(
    (Row 'App private bytes'          $med.PrivateMB '{0:F1}' $maxPrivateMB 'MB'),
    (Row 'Whole tree (incl. claude)'  $med.TreeMB    '{0:F1}' $maxTreeMB    'MB'),
    (Row 'Idle CPU (one core)'        $med.CpuPct    '{0:F3}' $maxCpuPct    '%'),
    (Row 'Start to orb'               $med.StartMs   '{0:F0}' $maxStartMs   'ms')
)
Write-Host ("{0,-28} {1}" -f 'First word', 'n/a (step 2)')

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
[pscustomobject]@{ version = 1; fps = $Fps; render = $Render; runs = $results; median = $med } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $outDir "perf-$stamp-${Fps}fps-$Render.json") -Encoding utf8

if ($ok -contains $false) { exit 1 }
