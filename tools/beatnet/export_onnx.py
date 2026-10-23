"""Phase 4 N4b: export the beat model to ONNX for the editor (Microsoft.ML.OnnxRuntime, CPU).

    python tools/beatnet/export_onnx.py <model.pt> <out.onnx> [<check.npz>]

Input "mel" (1, frames, 128) float32, output "logits" (1, frames, 2), frames dynamic. With a cached .npz it checks
that ONNX Runtime (if installed) or the exported graph reproduces PyTorch's activation, and writes <out>.check.json:
the first 2 000 mel frames' beat probabilities, so the C# side can be tested against the same numbers.
"""
import json
import os
import sys

import numpy as np
import torch

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from train import BeatNet  # noqa: E402


def main():
    ckpt_path, out = sys.argv[1], sys.argv[2]
    ckpt = torch.load(ckpt_path, map_location="cpu")
    model = BeatNet()
    model.load_state_dict(ckpt["model"])
    model.eval()
    dummy = torch.zeros(1, 1200, 128)
    torch.onnx.export(model, dummy, out, input_names=["mel"], output_names=["logits"],
                      dynamic_axes={"mel": {1: "frames"}, "logits": {1: "frames"}}, opset_version=17, dynamo=False)
    meta = {"threshold": ckpt["threshold"], "epoch": ckpt["epoch"], "fps": 100, "mels": 128}
    print(f"exported {out} ({os.path.getsize(out) / 1e6:.2f} MB), threshold {ckpt['threshold']}")
    if len(sys.argv) > 3:
        mel = np.load(sys.argv[3])["mel"][:2000].astype(np.float32)
        with torch.no_grad():
            ref = torch.sigmoid(model(torch.from_numpy(mel).unsqueeze(0)))[0, :, 0].numpy()
        try:
            import onnxruntime as ort
            got = 1 / (1 + np.exp(-ort.InferenceSession(out).run(None, {"mel": mel[None]})[0][0, :, 0]))
            print(f"onnxruntime vs torch: max abs diff {np.abs(got - ref).max():.2e}")
        except ImportError:
            print("onnxruntime not installed in this venv: the C# test checks the numbers instead")
        meta["check_npz"] = os.path.basename(sys.argv[3])
        meta["check_beat_prob"] = [round(float(v), 6) for v in ref]
    json.dump(meta, open(out + ".json", "w"), indent=None)


if __name__ == "__main__":
    main()
