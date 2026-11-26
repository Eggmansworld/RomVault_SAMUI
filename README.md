# RomVault_SAMUI

Eggman's devilish twist on RomVault's boring SAMUI app. Enjoy!

**SAM-UI (Eggman Edition)** is a fork of the SAM-UI-Avalonia front-end from
[RomVault's RVWorld](https://github.com/RomVault/RVWorld). It recompresses files, folders and
zip/7z archives into TorrentZip, Zip-ZSTD, 7Z-ZSTD and 7Z-LZMA, using RomVault's SAM/TrrntZip engine.

<img width="2524" height="1622" alt="image" src="https://github.com/user-attachments/assets/a25ae073-06c6-4664-aae8-94fcd79cfdd9" />

---

## What's different
- **Full-width output list.** The controls sit across the top and there's a status bar at the bottom (counts, OK/FAIL/SKIP, size, speed, ETA).
- **Stage → Preview → Start.** Drop files or folders anywhere on the window, use the FILES / FOLDER pickers, or type a path. Nothing runs until you press START. PREVIEW lists exactly what would be processed, with sizes.
- **No more wrong-format surprises.** IN/OUT always start on your saved defaults, and a one-off change is shown in yellow until the next launch.
- **Never more than 80% of your CPU threads.** Workers also run at below-normal priority, so the PC stays usable.
- **Progress bars behind the filenames**, plus coloured PAUSE (orange) / SOFT STOP (yellow) / HARD STOP (red).
- **Warez look.** 5 themes, copper bars, a sine scroller, 3D vector objects, CRT scanlines and ANSI art. The FX switch turns the animation off.
- **Jukebox.** Plays MOD/S3M/XM/IT (libopenmpt) and C64 SID tunes while you crunch. 25 handpicked memorable tunes are bundled; drop more into `Tunes\`.

See [SAM-UI-CHANGES.md](SAM-UI-CHANGES.md) for details.

## Build
- Requires the .NET 10 SDK.
- Dev build: `build-samui.cmd`
- Release (self-contained win-x64 zip): `.\publish-samui.ps1`

Prebuilt Windows releases are on the Releases page. Unzip and run `SAM-UI-Avalonia.exe`.

## Licences
- RomVault / RVWorld code (everything this fork didn't add): Apache License 2.0, see [LICENSE](LICENSE).
- Eggman's additions (SAM-UI Eggman Edition UI, jukebox, effects): MIT, see [LICENSE-EGGMAN-MIT](LICENSE-EGGMAN-MIT).
- libopenmpt and companions: BSD-3 and others, see `SAM-UI-Avalonia/THIRD_PARTY_NOTICES.txt`.
- Bundled tunes: all credit to their original artists (artist names in the file names, if known).
