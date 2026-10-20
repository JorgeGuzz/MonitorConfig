# Real measurement: starts MonitorConfig in the tray and samples it every second (memory, CPU, handles, threads).
# Usage: powershell -NoProfile -File tools\perf.ps1 [-Seconds 60]
param([int]$Seconds = 60)
$exe = Join-Path $PSScriptRoot "..\MonitorConfig.exe"
$wasRunning = @(Get-Process MonitorConfig -ErrorAction SilentlyContinue).Count -gt 0
if ($wasRunning) { & $exe --exit | Out-Null }

$p = Start-Process $exe -ArgumentList "--tray" -PassThru
$rows = @()
for ($i = 1; $i -le $Seconds; $i++) {
    Start-Sleep -Seconds 1
    $p.Refresh()
    $rows += [pscustomobject]@{ S = $i; WS = $p.WorkingSet64 / 1MB; Priv = $p.PrivateMemorySize64 / 1MB; Cpu = $p.TotalProcessorTime.TotalMilliseconds; Handles = $p.HandleCount; Threads = $p.Threads.Count }
}
$p.Refresh()
$peak = $p.PeakWorkingSet64 / 1MB
& $exe --exit | Out-Null

$at = { param($s) $rows | Where-Object { $_.S -eq $s } | Select-Object -First 1 }
$r10 = & $at 10; $rEnd = $rows[-1]
"exe: {0:N0} KB" -f ((Get-Item $exe).Length / 1KB)
"Startup CPU (first 10 s): {0:N0} ms" -f $r10.Cpu
"Idle CPU (10 s -> {1} s): {0:N0} ms" -f ($rEnd.Cpu - $r10.Cpu), $Seconds
"Memory at 10 s: WS {0:N1} MB, private {1:N1} MB" -f $r10.WS, $r10.Priv
"Memory at {2} s: WS {0:N1} MB, private {1:N1} MB (peak WS {3:N1} MB)" -f $rEnd.WS, $rEnd.Priv, $Seconds, $peak
"Handles {0}, threads {1}" -f $rEnd.Handles, $rEnd.Threads
if ($wasRunning) { Start-Process $exe -ArgumentList "--tray" }
