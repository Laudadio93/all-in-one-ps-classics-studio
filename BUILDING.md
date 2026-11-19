# Building the full Windows application

This repository contains the suite host, both packaging studios, the image converters, the memory-card application and filesystem engine, and the native package-writer accelerator source. The [release page](https://github.com/Laudadio93/all-in-one-ps-classics-studio/releases/tag/source-2026.11.19) includes a portable Windows ZIP.

## Source layout

| Directory under `src/` | What it contains |
| --- | --- |
| `PlayStationStudioSuite/` | Windows shell, tab hosting, embedded component verification and extraction, shutdown handling, and suite About dialog |
| `PS4StudioSuiteComponent/` | PS4 package creation, the PS2 and PS3 image converters, disc checks, artwork, compatibility options, and PC manual viewer |
| `PS3StudioPS1/` | PS3 builders for PS1 and PS2, package verification and manual exclusion, PS3 menu preview, and native writer source under `native/` |
| `PackageStudioShared/` | Process execution, cancellation, output handling, and failure reporting used by the package studios |
| `PS2MemoryCardStudio-source/` | PS1 and PS2 card editors, raw-card handling, PS2 save parsers, and upstream memory-engine notices |

## Maintainer build

Use 64-bit Windows, the .NET 10 SDK with Windows desktop support, and a 64-bit Python environment with Tkinter. The development build used CPython 3.14, CustomTkinter 6.0.0, darkdetect 0.8.0, and PyInstaller 6.22.3. These are build-machine dependencies. The resulting EXE carries its runtimes.

1. Create or activate the Python environment. Install the build dependencies with `python -m pip install customtkinter==6.0.0 darkdetect==0.8.0 pyinstaller==6.22.3`.
2. Prepare a local input directory using the project-relative paths in the manifest. It lists the exact filenames, byte counts and SHA-256 hashes for the tested set. Inputs include the packaging and conversion archives, helper binaries, UI artwork, offline catalogs, blank cards, and the notices/source archive embedded by About. Supply resources you are authorized to use. [Build input manifest](build-inputs.json)
3. Run the two commands below from the repository root. The first identifies missing or mismatched inputs before compilation. The second builds the card editor, publishes both WPF studios, embeds their executables into the host, and writes the complete portable application.

```powershell
python build.py --assets "D:\StudioResources" --check
python build.py --assets "D:\StudioResources"
```

The output is `dist/PlayStation Classics Studio.exe`. Building uses an isolated temporary directory under `.build/`. A build machine may need internet access for its initial SDK or package restore.

The manifest contains fingerprints only. Public source downloads exclude the PS4 console emulator binaries and SDK packer, bundled license material, and the preassembled resource packs that carry them. The standalone Windows edition is designed to carry these resources at build time. A user running the complete build does not need this toolchain or a directory of build inputs.
