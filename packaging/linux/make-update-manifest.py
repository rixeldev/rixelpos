#!/usr/bin/env python3
"""Generates the update manifest published on GitHub Pages."""
import argparse
import hashlib
import json
from pathlib import Path


def asset(folder: Path, pattern: str):
    files = sorted(folder.glob(pattern))
    if not files:
        return None
    package = files[0]
    return {
        "file": package.name,
        "sha256": hashlib.sha256(package.read_bytes()).hexdigest(),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dir", required=True, help="Directory with update assets")
    parser.add_argument("--version", required=True, help="Build version (e.g. 1.4.0.12)")
    parser.add_argument("--output", default=None, help="Manifest path (defaults to DIR/update.json)")
    args = parser.parse_args()

    folder = Path(args.dir)
    folder.mkdir(parents=True, exist_ok=True)
    manifest = {
        "version": args.version,
        "linux": asset(folder, "*.pkg.tar.zst"),
        "linuxDeb": asset(folder, "*.deb"),
        "windows": asset(folder, "RixelPOS-Setup-*.exe"),
    }
    output = Path(args.output) if args.output else folder / "update.json"
    output.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps(manifest, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
