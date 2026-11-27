# RomVault's SAM-UI (Eggman Edition)

Eggman's devilish twist on RomVault's boring SAMUI app. Enjoy!

**SAM-UI (Eggman Edition)** is a fork of the SAM-UI-Avalonia front-end from
[RomVault's RVWorld](https://github.com/RomVault/RVWorld). It bulk recompresses files, folders and
zip/7z archives into TorrentZip, Zip-ZSTD, 7Z-ZSTD and 7Z-LZMA, using RomVault's SAM/TrrntZip engine.

<img width="2524" height="1622" alt="image" src="https://github.com/user-attachments/assets/a25ae073-06c6-4664-aae8-94fcd79cfdd9" />

---

## Quick start
1. Download the latest zip from the **Releases** page, unzip it anywhere, and run `SAM-UI-Avalonia.exe`.
   It's self-contained (no .NET install needed), Windows x64.
2. **Add** files or folders: drop them anywhere on the window, use **FILES...** / **FOLDER...**, or type a
   path and press Enter. They land in the list as **Queued**, with sizes. Nothing starts yet.
3. **Check** the list and the **IN** / **OUT** formats. Remove or reorder rows if you like.
4. Press **START** (it pulses when it's ready) and fire up the jukebox.

## What's different

### The list is the queue
- Everything you add is scanned with the **IN** filter (ZIP, 7Z, ZIP & 7Z, Files, Directories, All) and listed straight away as **Queued**. No separate preview step.
- **The list is processed top to bottom**, even while a job is running.
- **Select** rows with click, Ctrl+click or Shift+click (Ctrl+A for all), then:
  - **Remove** them: **DEL**, the REMOVE button or right-click. Rows being processed are left alone. Optional confirmation; switch it in SETUP.
  - **Move** them: **MOVE ► ⤒ TOP / ▲ / ▼ / ⤓ BOTTOM**, right-click, or Ctrl+Home / Ctrl+Up / Ctrl+Down / Ctrl+End. **TOP means next in line**: straight under the file being processed.
  - **Copy** them: Ctrl+C or right-click copies filename, size and status. "Copy filename(s)" copies just the paths.
- **Click a column header** (FILENAME, SIZE, SIZE (BYTES), STATUS) to sort. This re-orders the real list, so files are processed in the order you see.
- **Got something urgent?** Add files while a job is running (drop, pick, type, or Explorer "Send To") and SAM-UI asks **TOP (NEXT UP)** or **BOTTOM**. TOP files are processed as soon as a worker is free.
- **SAVE LIST** (Ctrl+S) writes every row's filename, size and status to **CSV** (opens in Excel) or tab-separated **TXT**.
- Columns: DONE (% / OK / ERR), FILENAME (with the progress bar drawn behind it), SIZE, **SIZE (BYTES)** and STATUS. They're resizable, and the widths are remembered.

### Processing and safety
- **No more wrong-format surprises.** IN / OUT / THREADS always start on **your saved defaults** (★ SAVE AS DEFAULT). A one-off change is shown in yellow ("NOT YOUR DEFAULT") until the next launch. **DRY RUN** is never remembered.
- **Never more than 80% of your CPU threads**, and workers run at below-normal priority, so the PC stays usable during hours-long jobs. The app default is 2 threads.
- **PAUSE** (orange) freezes everything. **SOFT STOP** (yellow) finishes the files in progress and skips the rest. **HARD STOP** (red) aborts immediately and leaves the originals unchanged.
- **Status bar:** state, files done / total and %, OK / FAIL / SKIP, size done / total, speed, elapsed time and a size-based ETA.
- **Per-file progress in 1% steps** (the upstream engine reports every 5%), plus an animated stripe while a big archive is still being checked.

### Window and Windows niceties
- Opens at **1680×1050** (shrunk to fit smaller screens), centred above the taskbar. The size and maximised state are remembered.
- **Off-screen rescue:** if the window ends up on a screen that's been switched off or unplugged, it's moved back to your main screen automatically.
- **One SAM-UI at a time.** Launching it again with files (Explorer "Send To", a shortcut, or dropping onto the exe) adds them to the open window. Launching it with nothing just brings the window back.
  Tip: put a shortcut to `SAM-UI-Avalonia.exe` in your SendTo folder (`shell:sendto` in Explorer's address bar).
- **Taskbar right-click:** Pause / Play music and Next tune, even while minimised.

### Look and feel
- **Warez style:** 5 themes (KEYGEN VIOLET, PHOSPHOR TERMiNAL, AMBER BBS, iCE CRACKTRO, BLOOD COPPER), copper bars behind a waving pixel logo, an **editable sine scroller**, a starfield with adjustable density, a scrolling wireframe floor, floating 3D vector objects (glenz cube, 4D tesseract, dodecahedron, DNA helix and more), bouncing vector balls, CRT scanlines and ANSI art.
- **SETUP (F2)** switches every layer on or off and sets the theme, 3D object, ball count, star density and scroller text. The ANiMATED EFFECTS switch freezes everything to a still frame (zero CPU). Effects pause while the window is minimised.
- The **NFO (F1)** has the how-to, keyboard shortcuts and credits.
- Rumour has it the app has a few visitors of its own... keep your eyes open. 👀

<img width="2524" height="1622" alt="image" src="https://github.com/user-attachments/assets/b420c719-0223-4558-8feb-273b96899504" />

### Jukebox
- Plays **MOD / S3M / XM / IT** (via libopenmpt) and **C64 SID** tunes while you crunch. **25 handpicked tunes** are bundled; drop more into the `Tunes` folder (**TUNES DiR**) or grab them at modarchive.org (**GET MORE MUSiC**).
- **Auto-plays** at start-up (switch in SETUP). First run starts with "God = Martin Galway". After that it carries on with the song that was playing when you closed it, or a **START SONG** you choose in SETUP.
- **NORMALIZE SONG VOLUMES:** every tune is measured before it plays and evened out, so the loud ones don't blast you.
- **SID tunes play to the end** (optional 3 / 5 / 10 minute cap for ones that loop forever).
- Volume is remembered (default 40%). Shuffle, previous / next, OPEN any tune. Keeps playing when minimised.

## Keyboard
| Key | Action | Key | Action |
|-----|--------|-----|--------|
| Ctrl+O | pick files | Ctrl+D | pick folders |
| Enter (PATH box) | add that path | Ctrl+Enter | START / ADD TO QUEUE |
| DEL | remove selected rows | Ctrl+C | copy selected rows |
| Ctrl+Up / Down | move rows | Ctrl+Home / End | rows to top / bottom |
| Ctrl+S | save the list | Pause | pause / resume |
| F1 | NFO | F2 | SETUP |

## Settings
Everything is saved in `SAM-UI.settings.json` next to the exe (or in `%LocalAppData%\SAM-UI` if that
folder is read-only): your defaults, window size, column widths, theme and effects, jukebox options.

## Build
- Requires the .NET 10 SDK.
- Dev build: `build-samui.cmd`
- Release (self-contained win-x64 zip): `.\publish-samui.ps1`

See [SAM-UI-CHANGES.md](SAM-UI-CHANGES.md) for the full change history.

## Licences
- RomVault / RVWorld code (everything this fork didn't add): Apache License 2.0, see [LICENSE](LICENSE).
- Eggman's additions (SAM-UI Eggman Edition UI, jukebox, effects): MIT, see [LICENSE-EGGMAN-MIT](LICENSE-EGGMAN-MIT).
- libopenmpt and companions: BSD-3 and others, see `SAM-UI-Avalonia/THIRD_PARTY_NOTICES.txt`.
- Bundled tunes: all credit to their original artists (artist names in the file names, if known).

## Hints
There are 2 easter eggs in the app. I won't spoil it if you like trying to discover them. If you must know what they are, click below for the spoilers:

<details>
<summary>Tell me what the easter eggs are!!!</summary>

--- 

- In the setup menu, ensure "ALLOW EASTER EGGS" is checked
- press the "A" key and watch the boing ball, it will escape the app about 40 seconds later and bounce off the screen. It will bounce across multiple monitors!
- press the "D" key and watch the flying devil, he escapes the app about 30 seconds later. He will fly around the desktop for 10 seconds, then exit the nearest screen edge.

Both escaped objects can be clicked through.

The boing ball and flying devil are timed to come out at 25 and 30 minute intervals and only stay on-screen for less than a minute. They will only escape the confines of the app window if you bring them out manually with a keypress. Otherwise, they will stay within the confines of the app window and behave themselves.

--- 

</details>
