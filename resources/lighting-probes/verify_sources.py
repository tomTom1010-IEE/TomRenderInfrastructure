"""Verify package SHA-256s, streamable bundles and distinct ME/Studio sources."""
import argparse
import csv
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import xml.etree.ElementTree as ET
from zipfile import ZIP_STORED, ZipFile


def identity(bundle, asset):
    return (bundle.replace("\\", "/").lower().removeprefix("abdata/"),
            PurePosixPath(asset.replace("\\", "/")).name.lower().removesuffix(".prefab"))


def inspect(path, fingerprint):
    with path.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != fingerprint["sha256"]:
            raise ValueError(f"zipmod digest mismatch: {path.name}")
    with ZipFile(path) as archive:
        tree = ET.fromstring(archive.read("manifest.xml"))
        if (tree.findtext("guid"), tree.findtext("version")) != (fingerprint["guid"], fingerprint["version"]):
            raise ValueError("wrong GUID or version")
        items, sources, bundles = set(), set(), {}
        for entry in archive.infolist():
            n = entry.filename
            if n.endswith(".unity3d"):
                if entry.compress_type != ZIP_STORED:
                    raise ValueError(f"bundle is not streamable: {n}")
                bundles[n] = hashlib.sha256(archive.read(n)).hexdigest()
            if PurePosixPath(n).name.startswith("ItemList_") and n.endswith(".csv"):
                for row in csv.DictReader(io.StringIO(archive.read(n).decode("utf-8-sig"))):
                    items.add(identity(row["Bundle Path"], row["File Name"]))
        for entry in tree.iter("Shader"):
            sources.add(identity(entry.attrib["AssetBundle"], entry.attrib["Asset"]))
        if bundles != fingerprint["bundles"] or archive.testzip():
            raise ValueError("bundle inventory, hash or archive CRC mismatch")
        return items, sources


if __name__ == "__main__":
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--pair", nargs=2, action="append", required=True,
                     metavar=("ZIPMOD", "FINGERPRINT"), type=Path)
    args = cli.parse_args()
    items, sources = set(), set()
    for path, pin in args.pair:
        p, s = inspect(path, json.loads(pin.read_text(encoding="utf-8")))
        items.update(p)
        sources.update(s)
    if items & sources:
        raise SystemExit(f"ME source is also a Studio item: {sorted(items & sources)}")
    print(f"{len(items)} Studio prefabs; {len(sources)} shader sources; zero collisions.")
