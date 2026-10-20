# Medición real: arranca MonitorConfig en la bandeja y lo muestrea cada segundo (memoria, CPU, handles, hilos).
# Uso: powershell -NoProfile -File tools\perf.ps1 [-Seconds 60]
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
$r5 = & $at 5; $r10 = & $at 10; $rEnd = $rows[-1]
"exe: {0:N0} KB" -f ((Get-Item $exe).Length / 1KB)
"CPU de arranque (primeros 10 s): {0:N0} ms" -f $r10.Cpu
"CPU en reposo (10 s -> {1} s): {0:N0} ms" -f ($rEnd.Cpu - $r10.Cpu), $Seconds
"Memoria a los 10 s: WS {0:N1} MB, privada {1:N1} MB" -f $r10.WS, $r10.Priv
"Memoria a los {2} s: WS {0:N1} MB, privada {1:N1} MB (pico WS {3:N1} MB)" -f $rEnd.WS, $rEnd.Priv, $Seconds, $peak
"Handles {0}, hilos {1}" -f $rEnd.Handles, $rEnd.Threads
if ($wasRunning) { Start-Process $exe -ArgumentList "--tray" }
