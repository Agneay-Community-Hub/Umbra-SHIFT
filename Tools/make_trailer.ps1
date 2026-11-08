# Records and encodes the Umbra Shift trailer.
#   1. Builds the Windows player (Pipeline.BuildWindows)
#   2. Runs it with -trailer: frames + audio.wav are written to Builds/Windows/trailer
#   3. Encodes Builds/Windows/trailer/UmbraShift-trailer.mp4 (1080p, H.264 + AAC) with ffmpeg
param(
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe",
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if (-not $SkipBuild) {
    $p = Start-Process $Unity -ArgumentList '-batchmode', '-quit', '-projectPath', $root, '-executeMethod', 'Pipeline.BuildWindows', '-logFile', "$root\Logs\build-trailer.log" -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "Build failed, see Logs\build-trailer.log" }
}

$exe = Join-Path $root "Builds\Windows\UmbraShift.exe"
$g = Start-Process $exe -ArgumentList '-trailer', '-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0' -PassThru
if (-not $g.WaitForExit(600000)) { $g.Kill(); throw "Trailer recording timed out" }
# the recording plays through the level flow, which writes progress; leave the player's real save alone
Remove-Item "HKCU:\Software\Umbra Shift" -Recurse -ErrorAction SilentlyContinue

$t = Join-Path $root "Builds\Windows\trailer"   # the player writes next to its own Data folder
Get-Content "$t\info.txt"
ffmpeg -y -loglevel error -framerate 30 -i "$t\frames\f%05d.jpg" -i "$t\audio.wav" `
    -vf "scale=1920:1080:flags=lanczos,format=yuv420p" -c:v libx264 -preset slow -crf 18 `
    -c:a aac -b:a 192k -shortest -movflags +faststart "$t\UmbraShift-trailer.mp4"
Write-Host "Wrote $t\UmbraShift-trailer.mp4"
