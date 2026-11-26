# SAM-UI (Eggman Edition) – change log

## v1.4.1 – 2026-11-26
- **Fix:** with THREADS set to 1 as the saved default, START did nothing until threads were raised to 2. The thread count started at 1 inside the app, so setting it to the saved 1 at start-up never created a worker. It now always creates the workers at start-up.
- **Sortable columns:** click FILENAME (A-Z / Z-A by file name), SIZE (by actual bytes) or STATUS. The sort is kept when more files are added or a job starts.

## v1.4.0 – 2026-11-26 – the list is the queue
- **Dropped or picked items go straight into the list** as **Queued**, scanned with the IN filter and shown with sizes. The status bar shows `QUEUED n (size)`. PREVIEW is gone because the list *is* the preview. The PATH box is now "type a path, press ENTER / ADD".
- Drops keep adding to the list. After a finished job, the next drop starts a fresh list. Changing IN re-scans what was dropped, and rows you removed stay removed.
- START processes the Queued rows; their status becomes "Waiting for a worker", then Processing, then the result. While a job runs, new drops queue up and **ADD TO QUEUE** adds them to it.
- **Select and remove rows:** click, Ctrl+click or Shift+click (Ctrl+A for all), then **DEL**, the **REMOVE** button or right-click > Remove. Rows being processed can't be removed. Rows already waiting for a worker are pulled back out of the worker queue. A confirmation dialog appears, with "don't ask again"; it can be switched back on or off in SETUP > LIST.
- **SAVE LIST** (Ctrl+S, or right-click) saves every row's filename, size and status to **CSV** (Excel friendly) or tab-separated **TXT**.
- **Copy rows:** Ctrl+C or right-click > Copy line(s) copies filename, size and status, tab-separated. "Copy filename(s)" copies just the paths.
- Selected rows are highlighted. SETUP scrolls if it's taller than the screen.

## v1.3.2 – 2026-11-26
- The output list remembers the widths you drag the DONE, FILENAME and SIZE columns to (STATUS fills the rest). They're saved when SAM-UI closes.
- The sine scroller text can be edited in SETUP and updates live as you type. **↺ DEFAULT TEXT** brings back the built-in text, and an empty box shows it too.

## v1.3.1 – 2026-11-26
- **Fix: per-file progress.** The upstream backend reported progress in 5% steps, so a big archive sat at 0% with no bar for minutes. `TorrentZipRebuild.cs` and `TorrentZipMake.cs` now report every 1% (two lines each; the only change to upstream's SAM/TrrntZip). While a file is still at 0%, which includes the backend's initial read/check of the archive, a thin animated stripe under the name shows it's working.
- **Jukebox start-up song:**
  1. the user's own **START SONG** from SETUP, if set
  2. otherwise the song playing when SAM-UI last closed, or the next one after it if that file is gone (no error)
  3. first run: "echoing (banana)"
  4. no tunes at all: "NO SONGS DETECTED, ADD SOME!"

## v1.3.0 – 2026-11-26
- **Vector balls** are much bigger: the formation fills about three quarters of the output window at the top of each bounce. It now bounces off the bottom edge of the output window, just above the scroller, instead of mid-air. Shadows sit on that edge while the floor is showing.
- **Star density** slider in SETUP (50 to 2,000 stars, default 180), saved to settings.
- Rumour has it something drops by now and then... keep an eye on the screen.
- Bundled tune renamed: "Demons are Forever PAL 50Hz (FROG).mod" (21 tunes).

## v1.2.0 – 2026-11-26
| Area | Change |
|------|--------|
| Jukebox | Auto-plays "echoing (banana)" at start-up (SETUP can switch this off). Volume is remembered; it defaults to 50%. |
| Taskbar | Right-click the SAM-UI taskbar button for **Pause music / Play music** and **Next tune** (Windows jump list). These work while SAM-UI is minimised. |
| 3D | The object now drifts around the whole screen. New objects: GLENZ CUBE (see-through faces), TESSERACT (rotating 4D hypercube), DODECAHEDRON, DNA HELIX. |
| Vector balls | Shaded balls in 3D formations (sphere, ring, cube lattice, helix, ripple sheet, crossed rings) that morph every 8 s while the group bounces and drifts, with floor shadows. Written from scratch, inspired by classic Amiga intros. Ball count (1 to 128) is set in SETUP. |
| Floor | A scrolling wireframe floor under the starfield fades in and out over a 60-second cycle. |
| Window | Opens at 1680×1050 (shrunk to fit smaller screens), centred in the primary screen's work area so it clears the taskbar. Size and maximised state are saved. If the window ends up off every connected screen (monitor unplugged or switched off, display change), it's moved back to the primary screen within 2 seconds. |
| START | Pulses while something is staged and ready to run. |
| Defaults | Threads are now part of the saved default (★ SAVE AS DEFAULT saves IN, OUT and THREADS). The app default is 2 threads. The default note has its own line and is no longer cut off. |
| SETUP (F2) | New popup with theme, 3D object, animated effects, and layer switches for copper bars, sine scroller, starfield, 3D objects, vector balls, wireframe floor and CRT scanlines. Also the ball count and music auto-play. The theme controls are gone from the main window. |
| Tunes | 21 bundled tunes (adds "Demons are Forever (FROG).mod"). |

## v1.1.0 – 2026-11-26 – rebased on current upstream
- Whole tree now matches RomVault RVWorld `329823a` (2026-11-03), including upstream's reworked
  SAM/TrrntZip backend (merged PR #36). Only SAM-UI-Avalonia differs from upstream.
- Adapted to the new backend API: `StatusCallback(threadId, fileId, percent)` (progress now maps
  to rows by file, not by thread), `ProcessFileStartCallback(..., size)`, `PauseCancel.Stopping`.
- Fix: a hard-stopped file is reported as `Cancel | NeedsRepaired` by the new backend. It now
  shows as "Cancelled (original unchanged)" instead of a green OK, and doesn't count towards
  bytes done.

## v1.0.0 – 2026-11-26 – full UI revamp

| Area | Change |
|------|--------|
| Layout | Sidebar gone. Top = animated header + jukebox, then a 3-row control deck (PATH / IN-OUT-THREADS / THEME + run controls). Output list is now **full window width**. Status bar at the bottom. Default window 1280x820. |
| Icon / logos | New `Assets/SAMUI.ico` (window + exe). RomVault `chip.ico`, `RomVault.png` and `title.png` removed. |
| Staging (no more instant start) | Dropping or picking files **only stages** them in the PATH box. Nothing runs until **START**. **PREVIEW** (F5) lists exactly what would be processed, with sizes and a total, without touching anything. START while running = **ADD TO QUEUE**. |
| Drop anywhere | Whole window is a drop target with a big "DROP TO STAGE" overlay. Ctrl+drop adds to what's already staged. |
| Pickers | **FILES...** (Ctrl+O) and **FOLDER...** (Ctrl+D), multi-select. PATH box is editable (several paths separated by `|`; Enter = preview). Paths on the command line are staged too (Explorer "Send To" shortcut works). |
| Format safety | IN/OUT are **not** remembered from the last run. App always starts on saved defaults; changing them turns the boxes yellow with "NOT YOUR DEFAULT – resets on next launch". **★ SAVE AS DEFAULT** / **↺ BACK TO DEFAULT**. Dry Run always starts off. |
| Counters | Status bar: state, `FILES done / total (%)`, OK / FAIL / SKIP, bytes done / total, MB/s, elapsed, size-based ETA (includes in-progress %), overall progress bar, message line. |
| Progress | Per-file progress bar drawn **behind the filename**; narrow **DONE** column shows `%` / OK / ERR / `--` / `...`. Rows go Queued → Processing → coloured result. After a stop, unprocessed rows show "Skipped (stopped)". |
| Run controls | PAUSE = orange, SOFT STOP = yellow, HARD STOP = red (faded when disabled). |
| Threads | Capped at **80% of logical CPUs** in total (files in parallel × compressor threads per file). Worker threads run at below-normal priority. |
| Look | 5 themes: KEYGEN VIOLET, PHOSPHOR TERMiNAL, AMBER BBS, iCE CRACKTRO, BLOOD COPPER. Copper bars + waving pixel logo, sine scroller, raster bar, starfield + rotating 3D vector objects behind the list (cube, octahedron, icosahedron, star, torus, dot-ball, auto-cycle or none), CRT scanlines, ANSI art (empty-list screen, NFO). FX toggle = still frame, zero CPU. Effects pause while minimised. |
| Jukebox | Ported from Eggman's Desktop Manager: MOD/S3M/XM/IT via libopenmpt 0.8.10, C64 SID emulation. 20 bundled tunes in `Tunes\`, users add their own. Prev/Play/Next/Shuffle/Open, volume, VU meter, TUNES DiR, GET MORE MUSiC. Starts silent; keeps playing when minimised. |
| NFO | F1 / NFO button: how-to, keys, safety notes, credits, greetz. |
| Settings | `SAM-UI.settings.json` next to the exe (falls back to `%LocalAppData%\SAM-UI`). `Tzipcfg.xml` is no longer used by this UI. |
| Closing | Closing while busy is refused with a status-bar message (stop first). |

### New / changed files (SAM-UI-Avalonia)
`App.axaml(.cs)`, `SAM-UI-Avalonia.csproj`, `Views/MainWindow.axaml(.cs)`, `Views/NfoWindow.cs`,
`ViewModels/MainWindowViewModel.cs`, `Models/Scanner.cs`, `Models/ComboOptions.cs`,
`Core/SamConfig.cs`, `Fx/*` (Skin, Effects, PixelFont, Ansi), `Audio/*` (Jukebox, OpenMpt,
StreamOut, Sid/*), `Assets/SAMUI.ico`, `Tunes/*`, `Native/*.dll`, `THIRD_PARTY_NOTICES.txt`.
No changes to SAM/TrrntZip in this release.

### Build / release
`publish-samui.ps1` → `artifacts\SAM-UI_v<ver>_win-x64.zip` (self-contained single exe +
`Native\` + `Tunes\`) and installs it to `C:\Utilities\RVWorld-SAMUI`.

## v0 – 2026-10-14 – first UI fixes
Pause stops spinner, Soft/Hard Stop, Size column, per-row progress, resizable columns,
themed background. (Merged upstream by the RomVault author on 2026-10-30, reworked.)
