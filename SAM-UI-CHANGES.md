# SAM-UI (Eggman Edition) – change log

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
