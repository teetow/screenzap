param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$env:OMP_NUM_THREADS = '4'
$env:MKL_NUM_THREADS = '4'
$venv = Join-Path $PSScriptRoot '.venv'
if (-not (Test-Path "$venv\Scripts\python.exe")) {
    & $Python -m venv $venv | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the model-export environment (Python 3.12 required).' }
}
$exportPython = "$venv\Scripts\python.exe"
& $exportPython -m pip install torch==2.10.0 torchvision==0.25.0 --index-url https://download.pytorch.org/whl/cpu | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Could not install CPU-only PyTorch for model export.' }
& $exportPython -m pip install -r "$PSScriptRoot\requirements.txt" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Could not install model-export dependencies.' }
& $exportPython "$PSScriptRoot\export.py" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Model export or validation failed.' }
