<#
.SYNOPSIS
  Measures the D-052 performance budget for Companion.exe.
.DESCRIPTION
  Per run: start the app, wait for the first rendered frame (start-to-orb), warm up,
  then measure idle CPU over a window and read private bytes. Reports the median of
  -Runs runs and PASS/FAIL against D-052. Exit code 1 if any metric fails.
  -Chat adds a chat run (step 2): the app drives its real chat path with scripted messages
  (COMPANION_PERF_CHAT, see README) and this script reports first word (Enter to the first real
  character in the bubble, fillers excluded) for the real flow (input opens, pre-warm, simulated
  typing) and for cold turns, the app's private bytes after a chat, the whole tree at rest and the
  peak while a claude process runs (reported separately, D-056). It sends about 15 short Haiku
  messages on the plan. -ChatOnly skips the idle runs.
  Not measured yet: first audio (step 6).
  Only the app process (and its children) is measured; GPU / dwm.exe cost is not counted.
#>
param(
    [ValidateSet(30, 15, 10)] [int]$Fps = 10,
    [ValidateSet("default", "hardware")] [string]$Render = "default",
    [int]$Runs = 3,
    [int]$WarmupSeconds = 30,
    [int]$MeasureSeconds = 60,
    [switch]$NoBuild,
    [string]$Pinned = "",
    [switch]$Baseline,
    [string]$ExePath = "",
    [string]$Label = "app",
    [switch]$Chat,
    [switch]$ChatOnly,
    [int]$ChatWarm = 10,
    [int]$ChatCold = 6,
    [int]$TypingMs = 3000
)
if ($ChatOnly) { $Chat = $true; $Runs = 0 }
. (Join-Path $PSScriptRoot 'perflib.ps1')   # Get-Median, Get-TreePrivateBytes, baseline helpers

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src\Companion\Companion.csproj'
$exe  = if ($ExePath) { $ExePath } else { Join-Path $repo 'src\Companion\bin\Release\net10.0-windows\Companion.exe' }
$outDir = Join-Path $repo 'perf-results'

# D-052 thresholds. Idle CPU <2 % is a reported target, not a gate (D-079); regressions are judged by tools/perfhistory.ps1.
$maxPrivateMB = 150; $maxTreeMB = 300; $maxCpuPct = 2.0; $maxStartMs = 1000
$maxFirstWordMs = 2000   # architecture: first word ~2 s (D-052), judged on the real flow with pre-warm

if (-not $NoBuild -and -not $ExePath) {
    Write-Host "Building Release..."
    dotnet build $proj -c Release -nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}
if (-not (Test-Path $exe)) { throw "Not found: $exe" }

# A stale instance would hit the single-instance mutex and skew the result.
Get-Process Companion -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force $outDir | Out-Null

$results = @()
$legacyRuns = if ($Baseline) { 0 } else { $Runs }
for ($run = 1; $run -le $legacyRuns; $run++) {
    $perfFile = Join-Path $env:TEMP "companion-perf-$PID-$run.txt"
    Remove-Item $perfFile -ErrorAction SilentlyContinue
    $env:COMPANION_PERF_FILE = $perfFile
    $env:COMPANION_FPS = "$Fps"
    if ($Pinned) { $env:COMPANION_PERF_PINNED = $Pinned }
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
Remove-Item Env:COMPANION_PERF_FILE, Env:COMPANION_FPS, Env:COMPANION_RENDER, Env:COMPANION_PERF_PINNED -ErrorAction SilentlyContinue

# D-079: the 2 % idle CPU figure is a reported target, not a gate. Prints the same line, returns nothing for the pass/fail list.
function Target($name, $value, $fmt, $limit, $unit) {
    Write-Host ("{0,-28} {1,10} {2}   target < {3} {2}   {4}   (reported, not a gate, D-079; compare builds with tools/perfhistory.ps1)" -f $name, ($fmt -f $value), $unit, $limit, $(if ($value -lt $limit) { 'under' } else { 'over' }))
}

function Row($name, $value, $fmt, $limit, $unit) {
    $ok = $value -lt $limit
    Write-Host ("{0,-28} {1,10} {2}   limit < {3} {2}   {4}" -f $name, ($fmt -f $value), $unit, $limit, $(if ($ok) { 'PASS' } else { 'FAIL' }))
    return $ok
}

$ok = @()

# ---------------- Idle CPU with baselines (cpu-investigation) ----------------
if ($Baseline -and $Runs -gt 0) {
    $state = Get-MachineState
    Write-Host ""
    Write-Host "Machine: $($state.Cpu); $($state.Os)"
    Write-Host "Power plan: $($state.PowerPlan); $($state.Power)"
    $variants = @(Get-BaselineVariants $repo) + [pscustomobject]@{ Name = $Label; Exe = $exe; Args = @(); Kind = 'app' }
    Write-Host ("Idle CPU: {0} rounds x {1} variants, {2} s settle + {3} s measured each, rotated, {4} fps" -f $Runs, $variants.Count, $WarmupSeconds, $MeasureSeconds, $Fps)
    $rows = Invoke-IdleRotation $variants $Runs $WarmupSeconds $MeasureSeconds $Fps
    $sum = Write-IdleReport $rows $variants
    Write-Host ""
    Format-IdleTable $sum
    $app = $sum | Where-Object { $_.Variant -eq $Label }
    Write-Host ""
    Write-Host ("Median of $Runs runs (D-052 as written: raw): {0:F2}% raw; above the bare window {1:F2}; above the static orb {2:F2}" -f $app.MedianRaw, $app.MedianMinusBare, $app.MedianMinusStatic)
    Target 'Idle CPU, raw' $app.MedianRaw '{0:F3}' $maxCpuPct '%'
    $ok += (Row 'App private bytes' $app.PrivateMB '{0:F1}' $maxPrivateMB 'MB')
    $ok += (Row 'Start to orb' $app.StartMs '{0:F0}' $maxStartMs 'ms')
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    [pscustomobject]@{ version = 1; machine = $state; fps = $Fps; warmup = $WarmupSeconds; measure = $MeasureSeconds; rows = $rows; summary = $sum } |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $outDir "perf-baseline-$stamp.json") -Encoding utf8
}

if ($legacyRuns -gt 0) {
$med = [pscustomobject]@{
    StartMs   = Get-Median ($results.StartMs)
    CpuPct    = Get-Median ($results.CpuPct)
    PrivateMB = Get-Median ($results.PrivateMB)
    TreeMB    = Get-Median ($results.TreeMB)
}

Write-Host ""
Write-Host ("Median of $Runs runs at $Fps fps, $Render render (D-052)" + $(if ($Pinned) { ", with a pinned bubble holding the $Pinned expression" } else { "" }) + ":")
$ok = @(
    (Row 'App private bytes'          $med.PrivateMB '{0:F1}' $maxPrivateMB 'MB'),
    (Row 'Whole tree (incl. claude)'  $med.TreeMB    '{0:F1}' $maxTreeMB    'MB'),
    (Row 'Start to orb'               $med.StartMs   '{0:F0}' $maxStartMs   'ms')
)
Target 'Idle CPU (one core)' $med.CpuPct '{0:F3}' $maxCpuPct '%'

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
[pscustomobject]@{ version = 1; fps = $Fps; render = $Render; runs = $results; median = $med } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $outDir "perf-$stamp-${Fps}fps-$Render.json") -Encoding utf8
}

# ---------------- Chat run (step 2) ----------------
function Get-Pct([double[]]$v, [double]$p) {
    if (-not $v -or $v.Count -eq 0) { return [double]::NaN }
    $s = $v | Sort-Object
    return $s[[math]::Min($s.Count - 1, [int][math]::Ceiling($p * $s.Count) - 1)]
}

if ($Chat) {
    $chatFile = Join-Path $env:TEMP "companion-chat-$PID.jsonl"
    Remove-Item $chatFile -ErrorAction SilentlyContinue
    $env:COMPANION_PERF_CHAT = "${ChatWarm}:${ChatCold}"
    $env:COMPANION_PERF_TYPING_MS = "$TypingMs"
    $env:COMPANION_PERF_CHAT_FILE = $chatFile
    $env:COMPANION_FPS = "$Fps"
    Remove-Item Env:COMPANION_RENDER -ErrorAction SilentlyContinue
    Get-Process Companion -ErrorAction SilentlyContinue | Stop-Process -Force
    Write-Host ""
    Write-Host "Chat run: $ChatWarm real-flow turns (pre-warm, ${TypingMs} ms typing) and $ChatCold cold turns..."
    $p = Start-Process $exe -PassThru
    $peakTree = 0.0; $peakProcs = 0; $finished = $false
    try {
        $deadline = (Get-Date).AddMinutes(20)
        while ((Get-Date) -lt $deadline -and -not $p.HasExited) {
            $p.Refresh()
            $tree = (Get-TreePrivateBytes $p.Id) / 1MB
            if ($tree -gt $peakTree) { $peakTree = $tree }
            $kids = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)").Count
            if ($kids -gt $peakProcs) { $peakProcs = $kids }
            if ((Test-Path $chatFile) -and (Select-String -Path $chatFile -Pattern '"done":true' -Quiet)) { $finished = $true; break }
            Start-Sleep -Milliseconds 700
        }
        if (-not $finished) { throw "Chat run did not finish (app exited or timeout)" }
        Start-Sleep -Seconds 10   # let the last claude process exit
        $p.Refresh()
        $privMB = $p.PrivateMemorySize64 / 1MB
        $restTreeMB = (Get-TreePrivateBytes $p.Id) / 1MB
        $restKids = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)").Count
    }
    finally {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    }
    Remove-Item Env:COMPANION_PERF_CHAT, Env:COMPANION_PERF_TYPING_MS, Env:COMPANION_PERF_CHAT_FILE, Env:COMPANION_FPS -ErrorAction SilentlyContinue

    $turns = Get-Content $chatFile | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.turn }
    $warm = @($turns | Where-Object { $_.mode -eq 'warm' }); $cold = @($turns | Where-Object { $_.mode -eq 'cold' })
    $failed = @($turns | Where-Object { $_.failed }).Count
    function Get-FirstWords($set) { @($set | Where-Object { $null -ne $_.firstWordMs } | ForEach-Object { [double]$_.firstWordMs }) }
    function Get-Totals($set) { @($set | Where-Object { $null -ne $_.totalMs } | ForEach-Object { [double]$_.totalMs }) }
    $warmFw = Get-FirstWords $warm; $coldFw = Get-FirstWords $cold
    $thinking = @($turns | Where-Object { $_.block -eq 'thinking' }).Count
    $fillers = @($turns | Where-Object { $_.filler }).Count

    Write-Host ""
    Write-Host "Chat results ($($turns.Count) turns, $failed failed):"
    Write-Host ("  first word, real flow (pre-warm, {0} ms typing): median {1:F0} ms, p90 {2:F0} ms, max {3:F0} ms (n={4})" -f $TypingMs, (Get-Pct $warmFw 0.5), (Get-Pct $warmFw 0.9), ($warmFw | Measure-Object -Maximum).Maximum, $warmFw.Count)
    if ($coldFw.Count) { Write-Host ("  first word, cold (no pre-warm):                   median {0:F0} ms, p90 {1:F0} ms, max {2:F0} ms (n={3})" -f (Get-Pct $coldFw 0.5), (Get-Pct $coldFw 0.9), ($coldFw | Measure-Object -Maximum).Maximum, $coldFw.Count) }
    Write-Host ("  reply complete, real flow:                        median {0:F0} ms, p90 {1:F0} ms" -f (Get-Pct (Get-Totals $warm) 0.5), (Get-Pct (Get-Totals $warm) 0.9))
    Write-Host ("  turns that started with a thinking block: {0}/{1}; turns that showed a filler: {2}/{1}" -f $thinking, $turns.Count, $fillers)
    Write-Host ("  claude processes at once during a reply: up to {0}; tree peak while claude runs: {1:F0} MB (reported separately, D-056)" -f $peakProcs, $peakTree)
    Write-Host ("  at rest after the chat: {0} claude process(es)" -f $restKids)
    $tagged = @($turns | Where-Object { $_.mood }).Count
    $notes = ($turns | Measure-Object -Property notes -Sum).Sum
    $fullNotes = ($turns | Measure-Object -Property fullNotes -Sum).Sum
    $talk = @($turns | Where-Object { $null -ne $_.talkCpuPct } | ForEach-Object { [double]$_.talkCpuPct })
    $talkMs = @($turns | Where-Object { $null -ne $_.talkMs } | ForEach-Object { [double]$_.talkMs })
    Write-Host ("  replies that ended with a mood tag: {0}/{1}; notes saved with remember: {2} (memory-full: {3})" -f $tagged, $turns.Count, $notes, $fullNotes)
    if ($talk.Count) { Write-Host ("  app CPU while a reply is shown (talking animation at 30 fps, typewriter and stream parsing; claude not counted; not a budget): median {0:F1}% of one core, p90 {1:F1}%, max {2:F1}% (n={3}, median {4:F1} s per reply)" -f (Get-Pct $talk 0.5), (Get-Pct $talk 0.9), ($talk | Measure-Object -Maximum).Maximum, $talk.Count, ((Get-Pct $talkMs 0.5) / 1000)) }
    $sum = Get-Content $chatFile | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.summary }
    if ($sum) { Write-Host ("  background summary: folded {0} messages into {1} characters in {2:F0} ms; history after: {3} messages" -f $sum.folded, $sum.chars, $sum.ms, $sum.historyAfter) } else { Write-Host "  background summary: none ran (needs more than 30 messages and 15 s of quiet)" }

    Write-Host ""
    Write-Host "Chat budget:"
    $ok += (Row 'First word (real flow)'     (Get-Pct $warmFw 0.5) '{0:F0}' $maxFirstWordMs 'ms')
    $ok += (Row 'App private bytes after chat' $privMB '{0:F1}' $maxPrivateMB 'MB')
    $ok += (Row 'Whole tree at rest after chat' $restTreeMB '{0:F1}' $maxTreeMB 'MB')
    if ($failed -gt 0) { Write-Host "  WARNING: $failed turn(s) failed; first word is over the successful turns only"; $ok += $false }

    $stamp2 = Get-Date -Format 'yyyyMMdd-HHmmss'
    [pscustomobject]@{ version = 1; typingMs = $TypingMs; turns = $turns; summary = $sum; peakTreeMB = $peakTree; privateMB = $privMB; restTreeMB = $restTreeMB } |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $outDir "perf-chat-$stamp2.json") -Encoding utf8
    Remove-Item $chatFile -ErrorAction SilentlyContinue
}

if ($ok -contains $false) { exit 1 }
