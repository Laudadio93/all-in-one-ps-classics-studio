"""Build the complete Windows suite from this checkout and local resource inputs."""

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def checked_inputs(assets):
    missing = []
    files = []
    for item in json.loads((ROOT / "build-inputs.json").read_text(encoding="utf-8")):
        relative = Path(item["path"])
        file = assets / relative
        if not file.is_file() or file.stat().st_size != item["bytes"]:
            missing.append(str(relative))
            continue
        with file.open("rb") as source:
            matches = hashlib.file_digest(source, "sha256").hexdigest() == item["sha256"]
        if not matches:
            missing.append(str(relative))
        files.append(relative)
    if missing:
        raise SystemExit("Missing or mismatched build inputs:\n  " + "\n  ".join(missing))
    return files


def publish(project, destination):
    subprocess.run([
        "dotnet", "publish", str(project), "-c", "Release", "-r", "win-x64",
        "--self-contained", "true", "-p:PublishSingleFile=true",
        "-p:EnableCompressionInSingleFile=false", "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-o", str(destination),
    ], check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", required=True, type=Path, help="Root matching the paths in build-inputs.json")
    parser.add_argument("--check", action="store_true", help="Verify inputs without building")
    args = parser.parse_args()
    assets = args.assets.resolve()
    files = checked_inputs(assets)
    print(f"Verified {len(files)} build inputs.", flush=True)
    if args.check:
        return
    if sys.platform != "win32":
        raise SystemExit("The Windows release must be built on Windows.")

    build_root = ROOT / ".build"
    build_root.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(dir=build_root) as temporary:
        job = Path(temporary)
        source = job / "src"
        shutil.copytree(ROOT / "src", source)
        for relative in files:
            output = source / relative
            output.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(assets / relative, output)
        resources = source / "PlayStationStudioSuite" / "Resources"
        subprocess.run([
            sys.executable, "-m", "PyInstaller", "--noconfirm",
            "--distpath", str(job / "memory"), "--workpath", str(job / "pyinstaller"),
            "PS2MemoryCardStudio.spec",
        ], cwd=source, check=True)
        shutil.copyfile(job / "memory" / "PS2 Memory Card Studio.exe", resources / "memory.exe")
        for folder, project, exe, member in [
            ("PS3StudioPS1", "PS2ClassicsPackageStudio.csproj", "PS1 and PS2 Classics Package Studio.exe", "ps3.exe"),
            ("PS4StudioSuiteComponent", "PS4Studio.csproj", "PS4 Classics Package Studio.exe", "ps4.exe"),
        ]:
            destination = job / folder
            publish(source / folder / project, destination)
            shutil.copyfile(destination / exe, resources / member)
        publish(source / "PlayStationStudioSuite" / "Suite.csproj", job / "suite")
        destination = ROOT / "dist" / "PlayStation Classics Studio.exe"
        destination.parent.mkdir(exist_ok=True)
        os.replace(job / "suite" / destination.name, destination)
        print(f"Created {destination}")


if __name__ == "__main__":
    main()
