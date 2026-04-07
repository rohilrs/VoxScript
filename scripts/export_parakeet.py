#!/usr/bin/env python3
"""Export NVIDIA Parakeet CTC model to ONNX + SentencePiece tokenizer.

Usage:
    pip install nemo_toolkit[asr] onnx
    python scripts/export_parakeet.py [--output-dir models/parakeet]
"""
import argparse
import shutil
from pathlib import Path

import nemo.collections.asr as nemo_asr
import onnx


def main():
    parser = argparse.ArgumentParser(description="Export Parakeet CTC to ONNX")
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path("models/parakeet"),
        help="Directory to save exported files (default: models/parakeet)",
    )
    parser.add_argument(
        "--model-name",
        default="nvidia/parakeet-ctc-0.6b",
        help="HuggingFace model name (default: nvidia/parakeet-ctc-0.6b)",
    )
    args = parser.parse_args()

    args.output_dir.mkdir(parents=True, exist_ok=True)
    onnx_path = args.output_dir / "parakeet-ctc-0.6b.onnx"
    tokenizer_path = args.output_dir / "parakeet-ctc-0.6b.model"

    print(f"Loading {args.model_name} from HuggingFace...")
    model = nemo_asr.models.ASRModel.from_pretrained(args.model_name)

    print(f"Exporting ONNX to {onnx_path}...")
    model.export(str(onnx_path))

    # NeMo may export with many small external data files (e.g. onnx__MatMul_8046).
    # Consolidate into a single external data file alongside the .onnx.
    # (Protobuf has a 2GB limit, so models >2GB must use external data.)
    print("Consolidating external weights into single data file...")
    onnx_model = onnx.load(str(onnx_path), load_external_data=True)
    data_filename = f"{onnx_path.stem}.onnx.data"
    onnx.save_model(
        onnx_model,
        str(onnx_path),
        save_as_external_data=True,
        all_tensors_to_one_file=True,
        location=data_filename,
    )

    # Clean up any leftover scattered data files
    for f in args.output_dir.iterdir():
        if f.name in (onnx_path.name, data_filename, tokenizer_path.name):
            continue
        if f.suffix in (".onnx", ".model"):
            continue
        if f.is_file():
            f.unlink()
            print(f"  Removed old data file: {f.name}")

    # Extract SentencePiece tokenizer .model file
    # NeMo stores the path in different attributes depending on version/tokenizer type.
    tokenizer = model.tokenizer
    sp_model_path = None
    candidates = []

    # Try common attribute paths
    for attr_chain in [
        ("tokenizer", "vocab_file"),
        ("tokenizer", "model_path"),
        ("model_path",),
        ("vocab_file",),
        ("tokenizer", "sp_model_file"),
        ("sp_model_file",),
    ]:
        obj = tokenizer
        for attr in attr_chain:
            obj = getattr(obj, attr, None)
            if obj is None:
                break
        if isinstance(obj, str) and Path(obj).exists():
            candidates.append(obj)

    # Also try dir() to find any .model file reference
    if not candidates:
        for attr in dir(tokenizer):
            if attr.startswith("_"):
                continue
            val = getattr(tokenizer, attr, None)
            if isinstance(val, str) and val.endswith(".model") and Path(val).exists():
                candidates.append(val)

    if candidates:
        sp_model_path = candidates[0]
        shutil.copy2(sp_model_path, tokenizer_path)
        print(f"Tokenizer saved to {tokenizer_path}")
    else:
        # Last resort: print all string attributes so user can find it
        print("WARNING: Could not auto-detect SentencePiece .model file.")
        print("Tokenizer attributes:")
        for attr in sorted(dir(tokenizer)):
            if attr.startswith("_"):
                continue
            val = getattr(tokenizer, attr, None)
            if isinstance(val, str) and len(val) < 500:
                print(f"  {attr} = {val}")
        print("If you see a path to a .model file above, copy it manually to:")
        print(f"  {tokenizer_path}")

    print(f"\nDone! Files in {args.output_dir}:")
    for f in sorted(args.output_dir.iterdir()):
        size_mb = f.stat().st_size / (1024 * 1024)
        print(f"  {f.name} ({size_mb:.1f} MB)")
    print("\nImport the .onnx file into VoxScript via Settings > Models > Import local file.")


if __name__ == "__main__":
    main()
