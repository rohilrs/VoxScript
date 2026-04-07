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

    # NeMo may export with external data files (e.g. onnx__MatMul_8046).
    # Merge everything into a single self-contained ONNX file.
    print("Merging external weights into single ONNX file...")
    onnx_model = onnx.load(str(onnx_path), load_external_data=True)
    onnx.save_model(
        onnx_model,
        str(onnx_path),
        save_as_external_data=False,
    )

    # Clean up any leftover external data files
    for f in args.output_dir.iterdir():
        if f.suffix not in (".onnx", ".model") and f.is_file() and f.name != tokenizer_path.name:
            f.unlink()
            print(f"  Removed external data file: {f.name}")

    # Extract SentencePiece tokenizer .model file
    tokenizer = model.tokenizer
    sp_model_path = None
    if hasattr(tokenizer, "tokenizer") and hasattr(tokenizer.tokenizer, "vocab_file"):
        sp_model_path = tokenizer.tokenizer.vocab_file
    elif hasattr(tokenizer, "model_path"):
        sp_model_path = tokenizer.model_path

    if sp_model_path and Path(sp_model_path).exists():
        shutil.copy2(sp_model_path, tokenizer_path)
        print(f"Tokenizer saved to {tokenizer_path}")
    else:
        print("WARNING: Could not locate SentencePiece .model file.")
        print("You may need to manually extract it from the NeMo checkpoint.")

    print(f"\nDone! Files in {args.output_dir}:")
    for f in sorted(args.output_dir.iterdir()):
        size_mb = f.stat().st_size / (1024 * 1024)
        print(f"  {f.name} ({size_mb:.1f} MB)")
    print("\nImport the .onnx file into VoxScript via Settings > Models > Import local file.")


if __name__ == "__main__":
    main()
