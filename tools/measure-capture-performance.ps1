param([string]$LogPath = (Join-Path $PSScriptRoot '..\local\capture-performance.log'))
$previous = $env:SCREENZAP_CAPTURE_PERF
$result = 0
try {
    $env:SCREENZAP_CAPTURE_PERF = '1'
    & (Join-Path $PSScriptRoot 'test-regressions.ps1') -Filter 'FullyQualifiedName~CaptureOverlayPerformanceTests' -Detailed -LogPath $LogPath
    $result = $LASTEXITCODE
} finally { $env:SCREENZAP_CAPTURE_PERF = $previous }
exit $result
