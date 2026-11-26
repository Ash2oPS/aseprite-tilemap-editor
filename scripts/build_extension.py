"""Build an Aseprite extension from the core and a project's brush pack."""

import argparse
import json
import os
import re
from pathlib import Path
from tempfile import NamedTemporaryFile
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo


ROOT = Path(__file__).resolve().parents[1]
DEMO = ROOT / "packs/minimal"


def build_extension(pack, output, script_root=None):
    profile = json.loads((pack / "profile.json").read_text(encoding="utf-8"))
    for key in ("name", "publisher", "commandPrefix", "archiveStem", "updateFolder"):
        if not re.fullmatch(r"[A-Za-z0-9_-]+", profile[key]):
            raise ValueError(f"{key} must be a safe identifier")
    if not re.fullmatch(r"\d+\.\d+\.\d+", profile["version"]):
        raise ValueError("Use a numeric major.minor.patch version")
    if not profile["layers"] or not all(profile[key] for key in ("planGroup", "menuTitle", "tilesetName")):
        raise ValueError("Set the menu title, plan group, tileset name and at least one initial layer")

    bundle = {path.name: path.read_bytes() for path in (ROOT / "aseprite").glob("*.lua")}
    bundle["profile.json"] = json.dumps(profile, indent=2).encode("utf-8")
    for asset in ("tiles.json", "tiles.png"):
        bundle[f"resources/{asset}"] = (pack / "resources" / asset).read_bytes()
    manifest = {key: profile[key] for key in ("name", "publisher", "version")}
    manifest.update(displayName=profile["menuTitle"], description="Paint and export native tilemaps.",
                    categories=["Scripts"], contributes={"scripts": [{"path": "./plugin.lua"}]})
    bundle["LICENSE"] = (ROOT / "LICENSE").read_bytes()
    bundle["package.json"] = json.dumps(manifest, indent=2).encode("utf-8")

    if script_root is not None:
        for name, data in bundle.items():
            path = script_root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    output.parent.mkdir(parents=True, exist_ok=True)
    with NamedTemporaryFile(dir=output.parent, suffix=".zip", delete=False) as temporary:
        staged = Path(temporary.name)
    try:
        with ZipFile(staged, "w", compression=ZIP_DEFLATED) as archive:
            for name, data in sorted(bundle.items()):
                archive.writestr(ZipInfo(name), data, compress_type=ZIP_DEFLATED)
        os.replace(staged, output)
    finally:
        staged.unlink(missing_ok=True)
    print(f'{profile["menuTitle"]} {profile["version"]}: {output.resolve()}')
    return profile


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pack", type=Path, default=DEMO)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--script-root", type=Path, help="also unpack the installation for CLI tests")
    args = parser.parse_args()
    build_extension(args.pack, args.output, args.script_root)
