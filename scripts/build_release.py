"""Build and validate the public Aseprite release from the matching Unity version."""

import argparse
import hashlib
import json
import re
from pathlib import Path
from tempfile import TemporaryDirectory
from zipfile import ZipFile

from build_extension import DEMO, ROOT, build_extension


def build_release(output, tag):
    version = json.loads((ROOT / "unity/package.json").read_text(encoding="utf-8"))["version"]
    profile = json.loads((DEMO / "profile.json").read_text(encoding="utf-8"))
    if not re.fullmatch(r"\d+\.\d+\.\d+", version) or version != profile["version"]:
        raise ValueError("The Unity package and the public Aseprite profile must share a numeric version.")
    if tag is not None and tag != f"v{version}":
        raise ValueError(f"Expected tag v{version}, got {tag}.")

    license_text = (ROOT / "LICENSE").read_bytes()
    if (ROOT / "unity/LICENSE.md").read_bytes() != license_text:
        raise ValueError("The Unity and repository licenses must match.")

    filename = f'{profile["archiveStem"]}-{version}.aseprite-extension'
    with TemporaryDirectory() as directory:
        first = Path(directory) / filename
        second = Path(directory) / "repeat" / filename
        build_extension(DEMO, first)
        build_extension(DEMO, second)
        contents = first.read_bytes()
        if contents != second.read_bytes():
            raise ValueError("Two builds from the same inputs produced different archives.")
        with ZipFile(first) as archive:
            if json.loads(archive.read("package.json"))["version"] != version:
                raise ValueError("The extension manifest version does not match the release.")
            if archive.testzip() is not None or archive.read("LICENSE") != license_text:
                raise ValueError("The extension archive or its license is invalid.")

    output.mkdir(parents=True, exist_ok=True)
    (output / filename).write_bytes(contents)
    digest = hashlib.sha256(contents).hexdigest()
    (output / "SHA256SUMS.txt").write_text(f"{digest}  {filename}\n", encoding="ascii", newline="\n")
    print(f"Verified release files for v{version} in {output.resolve()}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tag", help="validate a Git tag against both component versions")
    parser.add_argument("--output", type=Path, default=ROOT / ".build/release")
    args = parser.parse_args()
    build_release(args.output, args.tag)
