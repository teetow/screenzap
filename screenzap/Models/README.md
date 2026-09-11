# De-JPEG model

The published app includes `swinir-jpeg10.onnx` (about 63 MB) and the self-contained Windows ML runtime (`Microsoft.Windows.AI.MachineLearning` 2.3.42). Windows ML discovers/registers certified providers; inference uses its included `Microsoft.ML.OnnxRuntime` APIs with `MAX_PERFORMANCE` automatic selection. GPU/NPU acceleration depends on hardware and model support; CPU fallback uses at most four workers. Windows 10 build 18362 or newer, x64.

No Python, ComfyUI or local server is used at runtime. Windows ML may download hardware-specific execution providers on first use. The model and core GPU/CPU runtime are bundled. See [Microsoft's provider selection documentation](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/select-execution-providers).

For a fresh source checkout, install Python 3.12 and run from the repository root:

```powershell
.\tools\dejpeg\prepare.ps1
$env:DOTNET_PROCESSOR_COUNT = '4'
dotnet publish screenzap/Screenzap.csproj -c Release -m:2 -p:UseSharedCompilation=false
```

Python dependencies are build tools only, isolated in `tools/dejpeg/.venv`. The script downloads the hash-checked upstream checkpoint, exports static 252×252 RGB float32 tiles (ONNX opset 17), and compares ONNX output against PyTorch. Build tools and generated weights are ignored by Git. Release CI regenerates and bundles the model. Ship the entire publish folder, not just the EXE.

Source: [SwinIR by Jingyun Liang et al.](https://github.com/JingyunLiang/SwinIR), color JPEG artifact removal, JPEG10 checkpoint. Screenzap converts the weights to ONNX without retraining; inference uses 28-pixel context on each side of a tile. Input/output normalization is inside the model. See `SwinIR-LICENSE.txt` for the upstream license.
