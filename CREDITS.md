# Credits and acknowledgments

**Laudadio93** maintains All in One PS Classics Studio and its integrated Windows interface. The suite brings together the package builders, disc checks, previews, conversions, and memory-card workflows described in the [feature guide](FEATURES.md). The underlying tools, reference data, and preservation work have many authors. The public source snapshot and its local build inputs are listed in the [build guide](BUILDING.md).

## Package creation and compatibility

- **SvenGDK and the PS Classics fPKG Builder contributors** provided the reference for the PS4 packaging workflow, game identifiers, PS1 LibCrypt data, and PS4 emulator Lua profiles. **Jabu** created PS2-FPKG, the reference for the PS2 packaging and automatic widescreen workflow. [PS Classics fPKG Builder](https://github.com/SvenGDK/PS-Classics-fPKG-Builder) · [PS2-FPKG](https://www.psx-place.com/resources/ps2-fpkg.1628/)
- **Aldo Vargas (aldostools)** created PS2 Classics GUI. The **ps3dev-net team** created the underlying ps2classic tool used for PS2 Classics image conversion. [PS2 Classics GUI](https://www.psx-place.com/resources/ps2-classics-gui.638/)
- **Ronnie Sahlberg and the pop-fe and pop-fe2 contributors** developed the PS1 packaging engine, the alternative PS2 command-line workflow, and game-specific references used to match manuals. [pop-fe](https://github.com/sahlberg/pop-fe) · [pop-fe2](https://github.com/sahlberg/pop-fe2)
- **The PSL1GHT Development Team** developed the PS3 package-writing tools on which the packaging workflow builds. **Hykem and contributors** developed make_npdata. **OpenSSL contributors** provided SHA-1 functionality used in the Windows package block processor. [PSL1GHT](https://github.com/ps3dev/PSL1GHT) · [OpenSSL](https://github.com/openssl/openssl)
- **Zarh and ManaGunZ contributors** collected PS2 compatibility configurations for PS3. **ps3classics/ps2 and PS3 Developer Wiki contributors** documented the CONFIG format and options. **PCSX2 patch authors and contributors** created and tested the title-specific widescreen patches. [ManaGunZ](https://github.com/Zarh/ManaGunZ) · [PS2 CONFIG guide](https://github.com/ps3classics/ps2/blob/main/config-guide.html) · [PCSX2 patches](https://github.com/PCSX2/pcsx2_patches)
- **Joshua MacDonald and the xdelta contributors** developed xdelta3, which the packaging workflow uses to apply supported binary patches. [xdelta](https://github.com/jmacd/xdelta)

## Disc formats and preservation data

- **Redump contributors** verified disc dumps and compiled the reference checksums used for offline matching. **tsitokely** provided the PS2 catalog snapshot through ps2-iso-renamer. The checks identify known disc revisions and distinguish them from unknown or modified images. [Redump](http://redump.org/) · [ps2-iso-renamer](https://github.com/tsitokely/ps2-iso-renamer)
- **SvenGDK’s OpenBurningSuite** and the **PS3Dec source maintained by al3xtjames** provided PS3 disc format references. **Redrrx (Yacine S.) and contributors** provided a PS3Dec implementation, local key discovery references, and a synthetic encrypted-disc fixture for testing. [OpenBurningSuite](https://github.com/SvenGDK/OpenBurningSuite) · [PS3Dec source](https://github.com/al3xtjames/PS3Dec) · [Redrrx’s ps3dec](https://github.com/Redrrx/ps3dec)

## Covers, manuals, and previews

- **xlenore and the PSX Covers and PS2 Covers contributors** organized cover artwork by game ID, enabling title matching and cover previews. Individual covers and illustrations belong to their respective creators and rights holders. [PSX Covers](https://github.com/xlenore/psx-covers) · [PS2 Covers](https://github.com/xlenore/ps2-covers)
- **Kirkland’s Manual Labor** scanned and preserved PS2 manuals. The **Internet Archive** hosts the collection used for optional online lookup. The original authors and publishers created the manuals. **PDFium contributors** provide the PDF rendering engine for on-PC preview. [PS2 manual collection](https://archive.org/details/kirklands-manual-labor-sony-playstation-2-usa-4k-version) · [PDFium](https://pdfium.googlesource.com/pdfium/)

## Virtual memory cards and save formats

- **Ross Ridge** wrote the original myMC PS2 filesystem and save-format code. **lawlmoeaung and the PS2 Memory Card Manager maintainers** maintained the adaptation used here. It provides filesystem, ECC, compression, and save readers used by PS2 Memory Card Studio. The source snapshot preserves the upstream notice and license with the modules. [PS2 Memory Card Manager](https://github.com/lawlmoeaung/ps2-memory-card-manager) · [Upstream notice](src/PS2MemoryCardStudio-source/UPSTREAM-NOTICES.md) · [Upstream license](src/PS2MemoryCardStudio-source/UPSTREAM-LICENSE.txt)
- **Martin Korth and PSX-SPX contributors** documented the PS1 memory-card layout, including directory frames, save blocks, and checksums. **bucanero and Apollo Save Tool contributors** maintain the console save manager targeted by the card export workflow. [PSX-SPX](https://psx-spx.consoledev.net/) · [Apollo Save Tool for PS3](https://github.com/bucanero/apollo-ps3)

## Desktop runtime and supporting libraries

- **Microsoft and the .NET and WPF contributors** provide the Windows application and UI runtime. **Python Software Foundation, CPython, Tcl/Tk, Tom Schimansky and CustomTkinter contributors, darkdetect contributors, and the PyInstaller team** provide the runtime, UI, theme detection, and bundling used by the PS2 card editor. [.NET WPF](https://github.com/dotnet/wpf) · [Python](https://www.python.org/) · [Tcl/Tk](https://www.tcl.tk/) · [CustomTkinter](https://github.com/TomSchimansky/CustomTkinter) · [darkdetect](https://github.com/albertosottile/darkdetect) · [PyInstaller](https://pyinstaller.org/)
- **Igor Pavlov and 7-Zip contributors**, and the **Cygwin and zlib teams**, provide archive and runtime support used by components in the full Windows build. Component notices accompany the applicable source directories. [7-Zip](https://www.7-zip.org/) · [Cygwin](https://www.cygwin.com/) · [zlib](https://zlib.net/) · [PS4 component notices](src/PS4StudioSuiteComponent/Resources/) · [PS3 component notices](src/PS3StudioPS1/Resources/)

PlayStation systems, platform software, and associated marks belong to Sony Interactive Entertainment and its affiliates. This is an independent community project. Each upstream license applies to the files it identifies. An acknowledgment records a contribution and does not grant permission to redistribute third-party binaries, game art, manuals, or licenses.
