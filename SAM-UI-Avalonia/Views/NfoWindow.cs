using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SAM_UI_Avalonia.Audio;
using SAM_UI_Avalonia.Fx;

namespace SAM_UI_Avalonia.Views;

/// <summary>The NFO: about / help / credits, warez style.</summary>
public sealed class NfoWindow : Window
{
    public NfoWindow()
    {
        Title = "SAM-UI.NFO";
        Width = 760;
        Height = 700;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Icon;

        string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

        var header = new TextBlock
        {
            Text = Ansi.Logo,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 6),
        };
        header.Bind(TextBlock.ForegroundProperty, header.GetResourceObservable("SamAnsiBrush"));

        var body = new TextBlock
        {
            Text = Body(ver),
            FontSize = 12,
            LineHeight = 17,
            Margin = new Thickness(26, 6, 26, 20),
            TextWrapping = TextWrapping.NoWrap,
        };

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(body);

        var root = new Panel();
        root.Children.Add(new VectorBackdrop());
        root.Children.Add(new ScrollViewer { Content = stack });
        root.Children.Add(new Scanlines());
        Content = root;

        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.F1) Close(); };
    }

    private static string Body(string ver)
    {
        string tunes = "";
        try
        {
            if (Directory.Exists(Jukebox.TunesFolder))
                tunes = string.Join("\n", Directory.EnumerateFiles(Jukebox.TunesFolder)
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Select(n => "      ♫ " + n));
        }
        catch { }

        return
$@"  ░▒▓ RELEASE iNFO ▓▒░

    SAM-UI v{ver}  -  Eggman Edition
    TorrentZip / Zip-ZSTD / 7Z-ZSTD / 7Z-LZMA structured recompressor

  ░▒▓ HOW iT WORKS ▓▒░

    [1] ADD     Drop files or folders ANYWHERE on the window, use
                FILES... / FOLDER..., or type a path and press ENTER.
                Everything matching the IN filter lands in the list
                as ""Queued"", with sizes. Nothing starts yet!
    [2] CHECK   IN  = which files to pick up (ZIP, 7Z, Files, Dirs...)
                OUT = the format to make. Changing IN re-scans the list.
                Select rows (Ctrl/Shift+click) and press DEL to drop them.
                Right-click a row to copy it, SAVE LIST writes a CSV.
                The list is processed top to bottom: MOVE rows, or click a
                column header to sort the list (and the processing order).
    [3] START   Rows go  Queued -> Processing (bar behind the name)
                -> result. START while running = ADD TO QUEUE.

  ░▒▓ RUN CONTROLS ▓▒░

    PAUSE       (orange) freeze everything, RESUME carries on
    SOFT STOP   (yellow) finish the files in progress, skip the rest
    HARD STOP   (red)    abort now - originals are left unchanged

  ░▒▓ SAFETY FEATURES ▓▒░

    * IN/OUT always start on YOUR DEFAULTS. Change them for a one-off
      job and the boxes turn yellow; it resets next launch unless you
      press  ★ SAVE AS DEFAULT.  No more 5 hour runs in the wrong format.
    * DRY RUN is never remembered - it is always off at start-up.
    * Never more than 80% of your CPU threads, worker threads run at
      below-normal priority. Your desktop stays usable.

  ░▒▓ KEYS ▓▒░

    CTRL+O  pick files          CTRL+D      pick folders
    DEL     remove rows         CTRL+ENTER  start
    CTRL+C  copy rows           CTRL+S      save the list
    CTRL+UP / DOWN   move rows      CTRL+HOME / END  rows to top / bottom
    PAUSE   pause / resume      F1          this NFO
    F2      SETUP (themes, effects, music)
    ENTER in the PATH box = add that path

  ░▒▓ JUKEBOX ▓▒░

    Plays MOD / S3M / XM / IT (via libopenmpt) and C64 SID tunes while
    you crunch. Auto-plays at start-up (switch off in SETUP). Keeps playing
    when minimised - right-click SAM-UI on the taskbar to pause or skip.
    Add your own tunes to the Tunes folder (TUNES DiR), or
    grab more at modarchive.org.

    Bundled tunes - all credit to the original artists:
{tunes}

  ░▒▓ CREDiTZ ▓▒░

    SAM, TrrntZip and the compression libraries:
        the RomVault / RVWorld team (Apache License 2.0)
    Module playback: libopenmpt 0.8.10 (BSD-3) - see THIRD_PARTY_NOTICES.txt
    Jukebox, effects and this edition's UI: Eggman

  ░▒▓ GREETZ ▓▒░

    All DAT makers, ROM preservationists, data hoarders,
    tracker musicians and everybody who still owns a floppy drive.

                       - ESC to close -
";
    }
}
