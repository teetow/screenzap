param([string]$ComfyHome = $env:SCREENZAP_COMFYUI_HOME)
$ErrorActionPreference = 'Stop'
trap {
    $logDirectory = Join-Path $env:LOCALAPPDATA 'Screenzap\ComfyUI'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $_ | Out-String | Set-Content (Join-Path $logDirectory 'launcher-error.log')
    exit 1
}
if (-not $ComfyHome) {
    $desktopConfig = Join-Path $env:APPDATA 'ComfyUI\config.json'
    if (Test-Path $desktopConfig) {
        $ComfyHome = (Get-Content $desktopConfig -Raw | ConvertFrom-Json).basePath
    }
    if (-not $ComfyHome) { $ComfyHome = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'ComfyUI' }
}
$python = Join-Path $ComfyHome 'screenzap-venv\Scripts\python.exe'
$main = Join-Path $ComfyHome 'screenzap-server\main.py'
if (-not (Test-Path $python) -or -not (Test-Path $main)) {
    throw "Screenzap's local backend is not installed in $ComfyHome. Start a compatible ComfyUI server at localhost:8188."
}
$state = Join-Path $env:LOCALAPPDATA 'Screenzap\ComfyUI'
foreach ($folder in @($state, "$state\user", "$state\input", "$state\output")) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}
# A dedicated user directory/database leaves the desktop application's state alone.
$arguments = @(
    ('"{0}"' -f $main), '--base-directory', ('"{0}"' -f $ComfyHome),
    '--user-directory', ('"{0}\user"' -f $state),
    '--input-directory', ('"{0}\input"' -f $state),
    '--output-directory', ('"{0}\output"' -f $state),
    '--listen', '127.0.0.1', '--port', '8188', '--disable-auto-launch',
    '--disable-all-custom-nodes', '--reserve-vram', '2', '--lowvram'
)
Start-Process -FilePath $python -ArgumentList $arguments -WorkingDirectory (Split-Path $main) `
    -WindowStyle Hidden -RedirectStandardOutput "$state\server.log" -RedirectStandardError "$state\server-error.log"
