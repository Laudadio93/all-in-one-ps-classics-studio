# Combined-edition release notes

**19 November 2026 · Combined edition**

Download the portable Windows ZIP from the release assets, extract it, and run **PlayStation Classics Studio.exe**. The standalone edition includes its engines, runtimes, and blank cards. GitHub also provides source archives.

This is the first public source snapshot for **All in One PS Classics Studio**. The earlier Package Studio and Memory Card Studio repositories remain separate historical projects. This snapshot documents the integrated Windows application through the PC-only manuals update.

## Changes from the earlier separate studios

| Area | Combined-edition changes |
| --- | --- |
| Windows application | One portable executable hosts five tabs. Added lazy startup, verified component caching, tab scroll restoration, coordinated shutdown, matching headers, and Windows-themed dialogs. |
| PS4 packaging | Added a PS4 destination for PS1 and PS2 games, sequential queues, multi-disc mode, game-ID lookup, PS1 controls and patching, PS2 Lua profiles and widescreen, package checks, and home-screen preview. |
| PS3 packaging | Integrated the PS1 and PS2 package workflows, retained the existing queue, artwork and compatibility controls, and added accelerated final PKG writing and more detailed live activity. |
| Disc checking and presentation | Integrated offline disc catalogs, track and audio checks, standard and 3D cover previews, local caches, optional online lookups, and decoded manual previews. |
| Image conversion | Added a dedicated tab for PS2 Classics image conversion and PS3 disc ISO encryption/decryption with a matching key. |
| Memory cards | Integrated both card studios with separate PS1 and PS2 tabs, bundled blank cards, capacity checks, per-system previews, selected output folders, and Apollo paths. |

## Latest change — manuals stay on the PC

PS3 Package Studio now follows the PS4 manual-preview workflow. Select a game, choose **Open manual on this PC**, and read its PDF or page archive. The selected game's preview and status follow the queue selection. A missing, corrupt, or still-downloading manual leaves package creation available.

The built-in PS1 engine is prevented from picking up a manual beside the disc or from its catalog. The PS2 builder omits its former manual-writing step. If an external engine automatically adds a recognized console manual, the studio checks and rebuilds that package before delivering it.

## Windows verification

- Rebuilt the standalone Windows executable from this source snapshot and the 42 matching local build inputs. Launched that rebuilt executable, opened all five tabs, revisited both memory-card tabs, and confirmed the process exited after the window closed.
- Opened the standalone suite and exercised its five tabs and shutdown.
- Built a PS1-to-PS3 package with a valid manual beside the test disc and verified the package contained no manual.
- Built PS2-to-PS3 packages with automatic, SOFT, disabled, and custom profile selections. The four outputs passed the package checks.
- Created a small external-engine PKG fixture with PS1 and PS2 manual entries. Independent extraction of the rebuilt PKG confirmed the game payload was byte-for-byte identical and the manual entries were absent. A second pass left the clean package alone.
- Tested queue selection, CBZ and PDF preview, viewer opening, and continued build eligibility with missing and corrupt manuals.
- Verified that the executable in the local Windows ZIP matched the locally built executable by SHA-256. Console gameplay remains unverified by these checks.

The complete feature inventory describes format-specific limits and offline behavior. [Feature guide](https://github.com/Laudadio93/all-in-one-ps-classics-studio/blob/source-2026.11.19/FEATURES.md) · [Credits](https://github.com/Laudadio93/all-in-one-ps-classics-studio/blob/source-2026.11.19/CREDITS.md)
