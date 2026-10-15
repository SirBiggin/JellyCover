#!/usr/bin/env python3
"""Build JellyCover and package it the way Jellyfin's plugin installer expects.

Usage:
  python package.py [--version 1.0.0.0] [--target-abi 12.2.0.0] [--changelog "..."] [--skip-build]

Produces dist/jellycover_<version>.zip containing Jellyfin.Plugin.JellyCover.dll + meta.json at the zip root
(same layout as jprm), prints the MD5 Jellyfin verifies on install, and writes dist/manifest-entry.json that can
be merged into a plugin-repository manifest (sourceUrl must be filled in / adjusted for the release asset).

Manual install: unzip into <jellyfin data>/plugins/JellyCover_<version>/ and restart Jellyfin.
"""
import argparse
import datetime
import hashlib
import json
import pathlib
import subprocess
import sys
import zipfile

GUID = "9be8d848-e1ba-4129-89af-3bf0def71e9c"
NAME = "JellyCover"
ARTIFACT = "Jellyfin.Plugin.JellyCover.dll"

root = pathlib.Path(__file__).resolve().parent
ap = argparse.ArgumentParser()
ap.add_argument("--version", default="1.0.0.0")
ap.add_argument("--target-abi", default="12.2.0.0")
ap.add_argument("--changelog", default="Initial release")
ap.add_argument("--repo", default="SirBiggin/JellyCover")
ap.add_argument("--skip-build", action="store_true")
ap.add_argument("--dotnet", default=str(pathlib.Path.home() / ".dotnet" / "dotnet"))
a = ap.parse_args()

proj = root / "Jellyfin.Plugin.JellyCover" / "Jellyfin.Plugin.JellyCover.csproj"
if not a.skip_build:
    dotnet = next((c for c in (a.dotnet, a.dotnet + ".exe") if pathlib.Path(c).exists()), "dotnet")
    subprocess.run([dotnet, "build", str(proj), "-c", "Release", f"-p:Version={a.version}",
                    f"-p:AssemblyVersion={a.version}", f"-p:FileVersion={a.version}"], check=True)

dll = root / "Jellyfin.Plugin.JellyCover" / "bin" / "Release" / "net10.0" / ARTIFACT
if not dll.exists():
    sys.exit(f"missing build output: {dll}")

dist = root / "dist"
dist.mkdir(exist_ok=True)
ts = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.0000000Z")

meta = {
    "category": "Metadata",
    "changelog": a.changelog,
    "description": "Book metadata, covers and author details from Hardcover.app. Needs a Hardcover API token with the read:catalog scope.",
    "guid": GUID,
    "name": NAME,
    "overview": "Hardcover.app metadata and cover provider for Jellyfin book libraries",
    "owner": "SirBiggin",
    "targetAbi": a.target_abi,
    "timestamp": ts,
    "version": a.version,
    "status": "Active",
    "autoUpdate": True,
    "imagePath": "",
    "assemblies": [],
}

zip_name = f"jellycover_{a.version}.zip"
zip_path = dist / zip_name
with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
    z.write(dll, ARTIFACT)
    z.writestr("meta.json", json.dumps(meta, indent=2))
md5 = hashlib.md5(zip_path.read_bytes()).hexdigest()

entry = {
    "guid": GUID,
    "name": NAME,
    "description": meta["description"],
    "overview": meta["overview"],
    "owner": "SirBiggin",
    "category": "Metadata",
    "imageUrl": "",
    "versions": [
        {
            "version": a.version,
            "changelog": a.changelog,
            "targetAbi": a.target_abi,
            "sourceUrl": f"https://github.com/{a.repo}/releases/download/v{a.version}/{zip_name}",
            "checksum": md5,
            "timestamp": ts,
        }
    ],
}
(dist / "manifest-entry.json").write_text(json.dumps(entry, indent=2) + "\n")
print(f"{zip_path}  md5={md5}")
print(f"{dist / 'manifest-entry.json'}")
