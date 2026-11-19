# All in One PS Classics Studio

**[Download the portable Windows ZIP](https://github.com/Laudadio93/all-in-one-ps-classics-studio/releases/download/source-2026.11.19/PlayStation-Classics-Studio-PC-Manuals.zip)** · [Release notes and source code](https://github.com/Laudadio93/all-in-one-ps-classics-studio/releases/tag/source-2026.11.19)

Redistribution rights for the bundled PS4 emulator components, PS4 package tool, and PS3 placeholder license have not been confirmed.

Prepare PS1 and PS2 games for PS3 or PS4, convert PS2 Classics and PS3 disc images, and build virtual memory cards in one Windows application. The standalone edition runs from a single **PlayStation Classics Studio.exe** on 64-bit Windows. The packaging engines, Windows desktop runtime, memory-card runtime, and blank card templates travel with the executable. End users do not need to install Python, .NET, or companion applications.

| Studio tab | Inputs | Result |
| --- | --- | --- |
| PS4 Package Studio | PS1 BIN or raw 2352-byte ISO with matching CUE, or PS2 DVD ISO | PS4 Classics fPKG for a compatible PS4 environment |
| PS3 Package Studio | Supported PS1 disc images or PS2 DVD ISOs | PS3 Classics PKG for a compatible PS3 environment |
| PS2 & PS3 Encrypt / Decrypt | PS2 DVD ISO, PS2 Classics ISO.BIN.ENC, or PS3 disc ISO | Converted image saved as a new file |
| PS2 Memory Card Studio | PSU, MAX, CBS, SPS, XPS, or PS2 PSV saves | ECC .ps2 or non-ECC .vmc virtual card |
| PS1 Memory Card Studio | MCS saves or standard raw 128 KiB PS1 memory cards | Raw MCR, MCD, VM1, or VMC card |

## Getting started

1. Download and extract the Windows ZIP, then open **PlayStation Classics Studio.exe**. The window title and executable use this shorter name. The repository covers the combined five-tab edition.
2. Pick the tab for your **destination**. For example, choose **PS3 Package Studio** to turn a PS2 DVD ISO into a PS3 package. Choose **PS2 Memory Card Studio** to put saves on a virtual card.
3. Add your own discs or saves, check the detected information, and choose an output location with room for both the result and temporary files. Keep a CUE and all its referenced tracks together.
4. For game packages, review the build activity and completion location. For Apollo, copy a finished card to the USB directory for its system.

## What works offline

Disc identification and verification use local data for supported images. Card creation, conversions with the necessary keys, local artwork, and local manual previews run on the PC. Previously cached cover styles and PS2 manuals can be reused offline. Looking up a missing cover or manual, or opening a community compatibility report, requires a connection. The PS1 packaging engine can also look up artwork and menu audio.

The two package tabs treat manuals as **PC previews only**. A missing or damaged manual does not block package creation. Games, personal save files, downloaded manuals, cover libraries, and personal disc keys are supplied by the user or retrieved when needed.

## Guides

- [Complete feature guide, input formats, options, and limits](FEATURES.md)
- [Combined-edition release notes and verification](RELEASE-NOTES.md)
- [Credits, contributors, and upstream projects](CREDITS.md)
- [Source layout and full-bundle build instructions](BUILDING.md)

Compatible console software and a game-specific configuration may still be required. A successful build verifies package structure on Windows. Playback varies by console, title, region, and disc revision.

Maintained by **Laudadio93**. Earlier standalone projects are available separately. [PS1 and PS2 Classics Package Studio](https://github.com/Laudadio93/ps2-classics-package-studio) · [PS2 Memory Card Studio](https://github.com/Laudadio93/ps2-memory-card-studio)

