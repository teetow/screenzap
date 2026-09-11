"""Build-time only: export and verify the model bundled with Screenzap. No ComfyUI."""
import argparse
import hashlib
import os
from pathlib import Path
from urllib.request import urlopen
import shutil

os.environ['OMP_NUM_THREADS'] = '4'
os.environ['MKL_NUM_THREADS'] = '4'
import numpy as np
import onnx
import onnxruntime as ort
import torch
from spandrel import ModelLoader

CHECKPOINT = '006_colorCAR_DFWB_s126w7_SwinIR-M_jpeg10.pth'
SHA256 = '0005b707e0e6f75b4d13c7447e2f184858ddbdcec95aa7eb8c07e8afa1d26bd9'
ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--checkpoint', type=Path, default=Path(__file__).parent / CHECKPOINT)
parser.add_argument('--output', type=Path, default=ROOT / 'screenzap/Models/swinir-jpeg10.onnx')
args = parser.parse_args()
if not args.checkpoint.exists():
    url = 'https://github.com/JingyunLiang/SwinIR/releases/download/v0.0/' + CHECKPOINT
    print('Downloading original SwinIR JPEG10 checkpoint…', flush=True)
    temporary = args.checkpoint.with_suffix('.download')
    with urlopen(url, timeout=120) as source, temporary.open('wb') as target:
        shutil.copyfileobj(source, target)
    temporary.replace(args.checkpoint)
if hashlib.sha256(args.checkpoint.read_bytes()).hexdigest() != SHA256:
    raise ValueError('Unexpected checkpoint checksum; refusing to export.')

torch.set_num_threads(4)
torch.set_num_interop_threads(1)
torch.manual_seed(0)
model = ModelLoader().load_from_file(str(args.checkpoint)).model.eval().cpu()
assert model.window_size == 7 and model.img_range == 255
# Fixed shapes permit execution-provider optimization and bound GPU memory.
# RGB float32 [0,1] in/out; mean subtraction and img_range=255 are inside the graph.
tile = torch.rand(1, 3, 252, 252)
args.output.parent.mkdir(parents=True, exist_ok=True)
output = args.output.with_suffix('.partial.onnx')
with torch.inference_mode():
    expected = model(tile).numpy()
    torch.onnx.export(model, tile, str(output), input_names=['image'], output_names=['clean'],
                      opset_version=17, dynamo=False, do_constant_folding=True)
onnx.checker.check_model(str(output))
options = ort.SessionOptions()
options.intra_op_num_threads = 4
options.inter_op_num_threads = 1
session = ort.InferenceSession(str(output), options, providers=['CPUExecutionProvider'])
actual = session.run(None, {'image': tile.numpy()})[0]
error = float(np.max(np.abs(expected - actual)))
assert error < 0.0001, f'Export differs from PyTorch: maximum absolute error {error}'
del session
output.replace(args.output)
print(f'Exported and verified {args.output}: {args.output.stat().st_size} bytes; max error {error}', flush=True)
print('SHA256:', hashlib.sha256(args.output.read_bytes()).hexdigest(), flush=True)
