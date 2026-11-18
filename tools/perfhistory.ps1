<#
.SYNOPSIS
  Idle CPU of several builds of Companion.exe plus the baselines, rotated in one session (cpu-investigation).
.DESCRIPTION
  -Builds takes "label=path\to\Companion.exe" pairs (for example one per commit, built from git worktrees).
  Variants: baseline-bare (empty window), baseline-static (orb drawn once, no Storyboard) and every build.
  Each round runs every variant once, starting from a different one each round, so drift hits all alike.
  Reports raw idle CPU and the paired difference to the bare and the static baseline of the same round.
  With -Subject and -Reference it prints the D-079 verdict (subject median more than 0.5 points above the reference = FAIL; default 5 rounds).
  Writes perf-results/perf-history-<stamp>.json. Only the process itself is measured (no dwm / GPU).
#>
param(
    [Parameter(Mandatory)] [string[]]$Builds,
    [int]$Rounds = 5,
    [int]$WarmupSeconds = 30,
    [int]$MeasureSeconds = 60,
    [ValidateSet(30, 15, 10)] [int]$Fps = 10,
    [string]$Subject = "",      # label of the build under test (D-079 gate)
    [string]$Reference = "",    # label of the reference build, normally main
    [double]$MaxAbovePoints = 0.5
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'perflib.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repo 'perf-results'
New-Item -ItemType Directory -Force $outDir | Out-Null

$variants = @(Get-BaselineVariants $repo)
foreach ($b in $Builds) {
    $label, $path = $b -split '=', 2
    if (-not $path -or -not (Test-Path $path)) { throw "Bad build '$b' (want label=path\to\Companion.exe)" }
    $variants += [pscustomobject]@{ Name = $label; Exe = (Resolve-Path $path).Path; Args = @(); Kind = 'app' }
}

$state = Get-MachineState
Write-Host ""
Write-Host "Machine: $($state.Cpu); $($state.Os)"
Write-Host "Power plan: $($state.PowerPlan); $($state.Power)"
$mins = [math]::Round($Rounds * $variants.Count * ($WarmupSeconds + $MeasureSeconds + 3) / 60, 1)
Write-Host ("{0} rounds x {1} variants, {2} s settle + {3} s measured each, about {4} min. Leave the PC alone." -f $Rounds, $variants.Count, $WarmupSeconds, $MeasureSeconds, $mins)

$rows = Invoke-IdleRotation $variants $Rounds $WarmupSeconds $MeasureSeconds $Fps
$sum = Write-IdleReport $rows $variants
Write-Host ""
Format-IdleTable $sum

# D-079 gate: the subject fails only if its idle median (5 rounds) is more than 0.5 points above the reference's, in this one session.
$verdict = $null
if ($Subject -and $Reference) {
    $a = $sum | Where-Object { $_.Variant -eq $Subject }; $b = $sum | Where-Object { $_.Variant -eq $Reference }
    if (-not $a -or -not $b) { throw "Subject or Reference label not among the builds" }
    $delta = $a.MedianRaw - $b.MedianRaw
    $verdict = [pscustomobject]@{ subject = $Subject; reference = $Reference; deltaPoints = $delta; maxPoints = $MaxAbovePoints; rounds = $Rounds; pass = ($delta -le $MaxAbovePoints) }
    Write-Host ""
    Write-Host ("D-079 gate: {0} median {1:F2}% vs {2} median {3:F2}%: {4:+0.00;-0.00} points (limit +{5}), {6} rounds: {7}" -f $Subject, $a.MedianRaw, $Reference, $b.MedianRaw, $delta, $MaxAbovePoints, $Rounds, $(if ($verdict.pass) { 'PASS' } else { 'FAIL' }))
    if ($Rounds -lt 5) { Write-Host "  note: D-079 asks for 5 rounds; this verdict is indicative only" }
}
Write-Host ("2 % target (reported, not a gate): " + (($sum | Where-Object { $_.Variant -notlike 'baseline-*' } | ForEach-Object { '{0} {1:F2}%' -f $_.Variant, $_.MedianRaw }) -join '; '))

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
[pscustomobject]@{ version = 1; machine = $state; fps = $Fps; warmup = $WarmupSeconds; measure = $MeasureSeconds; rounds = $Rounds; rows = $rows; summary = $sum; gate = $verdict } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $outDir "perf-history-$stamp.json") -Encoding utf8
Write-Host ""
Write-Host "Saved perf-results\perf-history-$stamp.json"
if ($verdict -and -not $verdict.pass) { exit 1 }
