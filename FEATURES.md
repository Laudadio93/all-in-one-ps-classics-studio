# Feature guide

The combined Windows edition has five tabs. Use a Package Studio when the destination is a console home screen, the conversion tab when another tool needs a particular disc-image format, and a Memory Card Studio when you are working with saves.

## PS4 Package Studio

- **Disc input and identity.** Add PS1 BIN or raw 2352-byte ISO images with a matching CUE, or PS2 DVD ISOs. The PS1 packaging path expects one image file referenced by its CUE. Disc metadata, filenames, and the local game-ID catalog help populate the ID. Review the detected title and ID before building.
- **Queues and multi-disc games.** Add several titles, change their order, or remove entries. A queue builds one PKG per game, one at a time. Multi-disc mode groups up to four PS1 discs or five PS2 discs into one package. The first disc supplies its identity and artwork.
- **PS1 settings.** Enable analog controls, skip the PS1 boot logo, or force 60 Hz. A recognized single-disc LibCrypt entry can supply a game-specific setting. The app reports an adjacent SBI file but does not import it. Recognized LibCrypt multi-disc packages are excluded from this workflow.
- **Patches and configurations.** Select a supported PPF2, PPF3, or xdelta patch for a single PS1 disc. The app patches a working copy. A custom PS4 Lua configuration can be selected for a single PS1 or PS2 disc.
- **PS2 settings.** Choose an automatic Lua compatibility profile by game ID, disable MTVU, or apply graphics compatibility settings. Automatic widescreen matches a single disc by its executable CRC. A custom Lua file and automatic widescreen are mutually exclusive. Widescreen takes priority over the automatic compatibility profile.
- **Presentation and output.** Preview a standard or 3D cover, choose a custom icon or background, and see an illustrative PS4 home-screen preview. Select an output folder and watch the estimated progress and time remaining. The studio checks for an existing output before staging and asks the PS4 toolkit to inspect the finished package.

## PS3 Package Studio

- **PS1 or PS2 to PS3.** Recognizable images select their system automatically. The PS1 workflow accepts CUE, BIN, IMG, and CCD inputs, subject to engine support. Keep the track files with the descriptor. The built-in PS2 workflow accepts one DVD ISO per package. Queue several ISOs for separate packages. The interface also supports a user-selected compatible pop-fe or pop-fe2 engine.
- **Disc order and swapping.** Reorder a queue or switch to multi-disc mode for a supported engine. PS1 multi-disc builds support up to five discs and optional in-game disc swapping. Removing a disc selects the next available row for quick removal.
- **PS1 configuration.** Choose the alternate PS1 emulator when appropriate, consult the offline LibCrypt index, or apply a custom `.ps3config` to one CUE disc. Supported PPF2, PPF3 and xdelta patches operate on a temporary copy of a single-file, single-disc CUE/BIN. Match patches to the correct game revision.
- **PS2 configuration.** Automatic profile selection uses CUSTOM, NET, GX, then SOFT priority and extracts the selected match. Choose a specific profile, disable it, supply a custom CONFIG, or generate an advanced configuration with the available cache, performance, pressure-button, and multitap settings. An optional community report opens for further compatibility research.
- **PS2 widescreen.** The studio matches the game serial and boot executable checksum and applies supported static writes from the patch database. Availability depends on the game and revision.
- **PS3 menu presentation.** Preview standard or 3D artwork, select an icon and background, and inspect an illustrative XMB menu. Engines that support menu audio offer automatic sound, silence, or a custom WAV. The UI exposes the options available for the current engine and build mode.
- **Build activity.** Follow copy, encryption, configuration, and package-writing stages as applicable. The app reports percentage, estimated remaining time, completion location, cancellation, and recent tool output on error. The native writer accelerates known bundled runtimes using OpenSSL and up to four workers. Custom engines retain their own writer.
- **Manual-free output.** The studio checks staged external packages for recognized console manual files. If it finds them, it rebuilds the staged PKG without the manual and verifies the resulting file list before moving the output into place. This extra pass can need additional time and working space.

## Disc checks, covers, and manuals on both package tabs

| Feature | What it checks or displays |
| --- | --- |
| Disc and PS1 audio indicators | Presence of the selected disc and referenced track files. Supported CUE checks inspect track count and INDEX 01 timing. These indicators do not test audio playback. |
| Offline disc verification | SHA-1 matches against bundled Redump snapshots for supported PS2 DVD ISOs and PS1 CUE/BIN layouts. Multi-track verification expects one BIN per track, one INDEX 01 at 00:00:00, and no gap directives. PS2 CD images, CHD images, and merged multi-track BINs are outside this check. |
| Artwork before creation | Looks up a cover by game ID, reports availability and previews the selected game. Choose standard or 3D artwork where available. Local cache entries can be reused offline. A manually selected icon has priority. |
| Optional online lookup | Retrieves matching covers when enabled. Missing PS2 manuals may be found through the indexed U.S. collection, verified, and cached for offline viewing. Coverage varies by title and region. |
| PC manual viewer | Opens rendered PDFs and ZIP/CBZ archives of page images with Previous and Next controls. PS1 lookup checks adjacent manual files. PS2 lookup checks adjacent archives and a local library named by game ID, such as `SLUS-20591.zip`. PS3 Package Studio also provides a file picker. |
| Manual status | Reports whether a manual can be read on the PC. Both PS3 and PS4 packages exclude manuals. A missing, damaged, or downloading preview does not prevent a PS3 package build. |

A catalog mismatch can mean a different region, revision, modified dump, or missing catalog entry. A match identifies a known dump. Console compatibility is a separate question.

## PS2 & PS3 Encrypt / Decrypt

| Operation | Input and key details |
| --- | --- |
| PS2 Classics | Convert a PS2 DVD ISO to PS3 PS2 Classics `ISO.BIN.ENC` or decrypt it back to an ISO. The full build supports its CEX placeholder configuration and a matching user-supplied 16-byte klicensee. Encryption can add format padding and a footer. |
| PS3 disc ISO | Encrypt or decrypt the mapped regions of a supported ISO using its own disc key. Accepts 16 raw bytes or 32 hexadecimal characters. Browsing for an ISO can find an identically named `.dkey` beside it. A name match alone cannot verify a key. |

Both operations write a separate output, report progress, and support cancellation. PS3 input must have complete 2048-byte sectors and a supported region map. IRD import is outside this workflow. Preserve the source and compare converted output with a trusted reference where available.

## PS2 Memory Card Studio

- Combine multiple **PSU, MAX, CBS, SPS, XPS, and PS2 PSV** saves on one virtual card. Add files in more than one selection.
- Choose **8, 16, 32, or 64 MiB** and either **ECC** or **non-ECC**. The studio includes matching compressed blank templates and expands the chosen template when needed.
- Check the selected capacity before creating the card. The UI reports saves that could not be imported, including duplicate save directories. The write is staged so an out-of-space failure does not replace the destination with a partial card.
- See the selected size on the card preview. Choose the filename and output folder. The app remembers the chosen folder and makes a backup when replacing an existing card.
- Export an ECC `.ps2` or non-ECC `.vmc`. For Apollo Save Tool, place the image in **`/PS2/VMC/`** on the USB drive.

## PS1 Memory Card Studio

- Create a blank card, import one or more **MCS** saves, or bring saves across from standard raw **128 KiB** card images.
- Each card holds **15 usable 8 KiB save blocks**. The reader checks directory frames, checksums, lengths, and block chains. Duplicate names, overlapping chains, malformed sources, and overflow are rejected.
- Export the raw card as **MCR, MCD, VM1, or VMC**. The app reports imported saves, remaining blocks, and the output path. Replacing an existing card creates a backup.
- For Apollo Save Tool, place the image in **`/PS1/VMC/`**. PS1 PSV and headered containers such as VMP are outside this importer.

## Desktop behavior

One window hosts all five tabs with coordinated headers, themed dialogs, build logs, and local About notices. Components are loaded on demand and cached in the Windows temporary folder. The host uses embedded SHA-256 fingerprints to recognize its components. Tabs retain their scroll position during the session. Closing an idle suite closes its child studios together. A memory-card write in progress can finish and report its result before closing.
