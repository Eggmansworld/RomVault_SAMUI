using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SAM_UI_Avalonia.Audio;
using SAM_UI_Avalonia.Fx;
using SAM_UI_Avalonia.ViewModels;
using TrrntZip;

namespace SAM_UI_Avalonia.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    private samFile? _scrollTarget;
    private readonly DispatcherTimer _scrollTimer;

    public MainWindow()
    {
        InitializeComponent();

        AnsiLogo.Text = Ansi.Logo;
        AnsiHowTo.Text = Ansi.HowTo;

        // Drop anywhere on the window (header, controls or the list).
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        DataContextChanged += OnDataContextChanged;
        OnDataContextChanged(this, EventArgs.Empty);

        Closing += OnClosing;
        KeyDown += OnKey;
        PathBox.KeyDown += OnPathKey;

        // Follow the newest row being processed, at most 4x a second.
        _scrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            if (_scrollTarget is { } row) FilesGrid.ScrollIntoView(row, null);
            _scrollTarget = null;
        });
        _scrollTimer.Start();

        if (Design.IsDesignMode) return;

        // size + placement: always on the primary screen, never off-screen
        PlaceWindow();
        RestoreColumnWidths();
        Opened += (_, _) =>
        {
            if (App.Config.WindowMaximized) WindowState = WindowState.Maximized;
            EnsureOnScreen();
        };
        Screens.Changed += (_, _) => Dispatcher.UIThread.Post(EnsureOnScreen, DispatcherPriority.Background);
        _screenTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) =>
        {
            // also catches "monitor switched off / unplugged" when Windows doesn't tell us
            EnsureOnScreen();
        });
        _screenTimer.Start();

        // jukebox
        Jukebox jb = App.Jukebox;
        JbVolume.Value = App.Config.JukeboxVolume;
        JbVolume.ValueChanged += (_, _) => jb.SetVolume((int)JbVolume.Value);
        jb.Changed += UpdateJukebox;
        Opened += (_, _) =>
        {
            jb.Init();
            UpdateJukebox();
            if (App.Config.AutoPlayMusic) jb.PlayStartup();
        };
    }

    // =================================================================== placement

    private readonly DispatcherTimer? _screenTimer;
    private const double TitleBarAllowance = 40; // window frame + title bar, in DIPs

    /// <summary>Saved size (default 1680x1050), shrunk to fit, centred in the primary screen's work area.</summary>
    private void PlaceWindow()
    {
        Screen? scr = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (scr == null) return;
        FitAndCenter(scr, App.Config.WindowWidth, App.Config.WindowHeight);
    }

    private void FitAndCenter(Screen scr, double wantW, double wantH)
    {
        double s = scr.Scaling;
        PixelRect wa = scr.WorkingArea; // excludes the taskbar
        double maxW = wa.Width / s, maxH = wa.Height / s - TitleBarAllowance;
        Width = Math.Max(Math.Min(wantW, maxW), Math.Min(MinWidth, maxW));
        Height = Math.Max(Math.Min(wantH, maxH), Math.Min(MinHeight, maxH));
        int pw = (int)(Width * s), ph = (int)((Height + TitleBarAllowance) * s);
        Position = new PixelPoint(wa.X + Math.Max(0, (wa.Width - pw) / 2), wa.Y + Math.Max(0, (wa.Height - ph) / 2));
    }

    /// <summary>
    /// If the title bar isn't on any connected screen (a monitor was unplugged, switched off or
    /// Windows lost track), bring the window back to the centre of the primary screen.
    /// </summary>
    private void EnsureOnScreen()
    {
        if (WindowState == WindowState.Minimized || !IsVisible) return;
        IReadOnlyList<Screen> all = Screens.All;
        if (all.Count == 0) return;

        double s = DesktopScaling;
        var titleBar = new PixelRect(Position.X, Position.Y, Math.Max(1, (int)(Bounds.Width * s)), (int)(TitleBarAllowance * s));
        bool visible = all.Any(sc =>
        {
            PixelRect i = sc.WorkingArea.Intersect(titleBar);
            return i.Width >= 150 * s && i.Height >= 8;
        });
        if (visible) return;

        Screen target = Screens.Primary ?? all[0];
        WindowState = WindowState.Normal;
        FitAndCenter(target, App.Config.WindowWidth, App.Config.WindowHeight);
        if (_vm != null) _vm.Message = "SAM-UI was off-screen (display change) - moved back to your main screen.";
    }

    /// <summary>DONE, FILENAME and SIZE keep the widths the user dragged them to; STATUS fills the rest.</summary>
    private void RestoreColumnWidths()
    {
        List<double> w = App.Config.ColumnWidths;
        for (int i = 0; i < Math.Min(w.Count, FilesGrid.Columns.Count - 1); i++)
            if (w[i] >= 30 && w[i] <= 4000)
                FilesGrid.Columns[i].Width = new DataGridLength(w[i]);
    }

    private void RememberColumnWidths()
    {
        var widths = new List<double>();
        for (int i = 0; i < FilesGrid.Columns.Count - 1; i++)
        {
            double a = FilesGrid.Columns[i].ActualWidth;
            if (double.IsNaN(a) || a <= 0) return; // grid not laid out yet: keep what was saved
            widths.Add(Math.Round(a));
        }
        App.Config.ColumnWidths = widths;
    }

    /// <summary>Remember the size the user picks (normal state only) and whether it's maximised.</summary>
    private void RememberSize()
    {
        if (Design.IsDesignMode) return;
        if (WindowState == WindowState.Normal && Width > 0 && Height > 0)
        {
            App.Config.WindowWidth = Math.Round(Width);
            App.Config.WindowHeight = Math.Round(Height);
        }
        if (WindowState != WindowState.Minimized)
            App.Config.WindowMaximized = WindowState == WindowState.Maximized;
    }

    private void Setup_Click(object? sender, RoutedEventArgs e) => new SetupWindow { DataContext = DataContext }.ShowDialog(this);

    // =================================================================== view model hookup

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.RowStarted -= OnRowStarted;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = DataContext as MainWindowViewModel;
        if (_vm != null)
        {
            _vm.RowStarted += OnRowStarted;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
        HookFiles();
    }

    private ObservableCollectionHook? _hooked;

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The collection is replaced on each new job: move the hook over.
        if (e.PropertyName == nameof(MainWindowViewModel.Files)) HookFiles();
    }

    private void HookFiles()
    {
        _hooked?.Dispose();
        _hooked = _vm == null ? null : new ObservableCollectionHook(_vm.Files, OnFilesChanged);
        UpdateEmptyState();
    }

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyState();

    private void UpdateEmptyState() => EmptyState.IsVisible = _vm == null || _vm.Files.Count == 0;

    private void OnRowStarted(samFile row) => _scrollTarget = row;

    private sealed class ObservableCollectionHook : IDisposable
    {
        private readonly INotifyCollectionChanged _c;
        private readonly NotifyCollectionChangedEventHandler _h;
        public ObservableCollectionHook(INotifyCollectionChanged c, NotifyCollectionChangedEventHandler h) { _c = c; _h = h; _c.CollectionChanged += _h; }
        public void Dispose() => _c.CollectionChanged -= _h;
    }

    // =================================================================== window

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            FxClock.Suspended = WindowState == WindowState.Minimized; // music keeps playing, effects rest
            RememberSize();
            if (WindowState != WindowState.Minimized)
                Dispatcher.UIThread.Post(EnsureOnScreen, DispatcherPriority.Background);
        }
        else if (change.Property == ClientSizeProperty)
            RememberSize();
    }

    /// <summary>
    /// The window may only be closed while nothing is being processed, i.e. no
    /// worker is busy, the queue is empty and no files are being added.
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (MainQueue.IsBusy)
        {
            e.Cancel = true;
            if (_vm != null) _vm.Message = "!! STILL WORKING - press SOFT STOP or HARD STOP first, then close SAM-UI.";
            return;
        }

        RememberSize();
        RememberColumnWidths();
        App.Config.Save();
        Workers.RemoveAllWorks();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_vm == null) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.O when ctrl: PickFiles(); break;
            case Key.D when ctrl: PickFolder(); break;
            case Key.Enter when ctrl: if (_vm.StartCommand.CanExecute(null)) _vm.StartCommand.Execute(null); break;
            case Key.S when ctrl: SaveList_Click(this, new RoutedEventArgs()); break;
            case Key.Delete when e.Source is not TextBox: Remove_Click(this, new RoutedEventArgs()); break;
            case Key.Pause: if (_vm.PauseCommand.CanExecute(null)) _vm.PauseCommand.Execute(null); break;
            case Key.F1: Nfo_Click(this, new RoutedEventArgs()); break;
            case Key.F2: Setup_Click(this, new RoutedEventArgs()); break;
            // easter egg: more / fewer vector balls (not while typing in a text box)
            case Key.OemPlus or Key.Add when e.Source is not TextBox: _vm.NudgeBalls(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 8 : 1); break;
            case Key.OemMinus or Key.Subtract when e.Source is not TextBox: _vm.NudgeBalls(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -8 : -1); break;
            case Key.A when e.Source is not TextBox && e.KeyModifiers == KeyModifiers.None: _vm.Boing(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnPathKey(object? sender, KeyEventArgs e)
    {
        // Enter in the path box = ADD (Ctrl+Enter = START, handled by the window).
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _vm?.AddPathCommand.CanExecute(null) == true)
        {
            _vm.AddPathCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Nfo_Click(object? sender, RoutedEventArgs e) => new NfoWindow().ShowDialog(this);

    // =================================================================== drag & drop

    private static List<string> GetPaths(DragEventArgs e)
    {
        var files = e.DataTransfer?.TryGetFiles();
        if (files is null)
            return new List<string>();

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool files = e.DataTransfer?.Contains(DataFormat.File) == true;
        e.DragEffects = files ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = files;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        DropOverlay.IsVisible = false;
        if (_vm == null) return;

        List<string> paths = GetPaths(e);
        if (paths.Count == 0)
            return;

        // Added to the list as Queued - nothing starts until START.
        _vm.Stage(paths);
        Activate();
    }

    // =================================================================== list: remove / copy / save

    private List<samFile> SelectedRows() =>
        FilesGrid.SelectedItems.OfType<samFile>().OrderBy(r => _vm?.Files.IndexOf(r) ?? 0).ToList();

    private async void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        List<samFile> rows = SelectedRows();
        if (rows.Count == 0)
        {
            _vm.Message = "Select the rows to remove first (click, Ctrl+click, Shift+click), then press DEL.";
            return;
        }
        if (App.Config.ConfirmRemove)
        {
            var (ok, dontAsk) = await ConfirmDialog.Ask(this, "REMOVE FROM LIST",
                rows.Count == 1 ? $"Remove this row from the list?\n\n{rows[0].Name}" : $"Remove {rows.Count:N0} rows from the list?",
                "Don't ask again (change it in SETUP)");
            if (dontAsk) _vm.ConfirmRemove = false;
            if (!ok) return;
        }
        _vm.RemoveRows(rows);
    }

    private void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
        {
            CopyRows(namesOnly: false);
            e.Handled = true;
        }
    }

    private void CopyLines_Click(object? sender, RoutedEventArgs e) => CopyRows(namesOnly: false);
    private void CopyNames_Click(object? sender, RoutedEventArgs e) => CopyRows(namesOnly: true);
    private void SelectAll_Click(object? sender, RoutedEventArgs e) => FilesGrid.SelectAll();

    private async void CopyRows(bool namesOnly)
    {
        if (_vm == null || Clipboard == null) return;
        List<samFile> rows = SelectedRows();
        if (rows.Count == 0) { _vm.Message = "Select a row first, then Ctrl+C (or right-click > Copy)."; return; }
        await Clipboard.SetTextAsync(MainWindowViewModel.RowsAsText(rows, namesOnly));
        _vm.Message = $"Copied {rows.Count:N0} line(s) to the clipboard.";
    }

    private async void SaveList_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (_vm.Files.Count == 0) { _vm.Message = "The list is empty - nothing to save."; return; }
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "SAM-UI - save the list",
            SuggestedFileName = $"SAM-UI list {DateTime.Now:yyyy-MM-dd HHmm}",
            DefaultExtension = "csv",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV (Excel)") { Patterns = new[] { "*.csv" } },
                new FilePickerFileType("Text (tab separated)") { Patterns = new[] { "*.txt" } },
            },
        });
        string? path = file?.TryGetLocalPath();
        if (path == null) return;
        try
        {
            bool csv = !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
            await File.WriteAllTextAsync(path, _vm.ListAsFile(csv), new System.Text.UTF8Encoding(true));
            _vm.Message = $"Saved {_vm.Files.Count:N0} row(s) to {path}";
        }
        catch (Exception ex)
        {
            _vm.Message = "Could not save the list: " + ex.Message;
        }
    }

    // =================================================================== pickers

    private void PickFiles_Click(object? sender, RoutedEventArgs e) => PickFiles();
    private void PickFolder_Click(object? sender, RoutedEventArgs e) => PickFolder();

    private async Task<IStorageFolder?> StartFolder()
    {
        string? first = _vm?.Files.LastOrDefault()?.Name;
        string? dir = first == null ? null : Directory.Exists(first) ? first : Path.GetDirectoryName(first);
        if (dir == null || !Directory.Exists(dir)) return null;
        return await StorageProvider.TryGetFolderFromPathAsync(new Uri(dir));
    }

    private async void PickFiles()
    {
        if (_vm == null) return;
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "SAM-UI - pick files to process",
            AllowMultiple = true,
            SuggestedStartLocation = await StartFolder(),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Archives (zip, 7z)") { Patterns = new[] { "*.zip", "*.7z" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        });
        List<string> paths = files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
        if (paths.Count > 0) _vm.Stage(paths);
    }

    private async void PickFolder()
    {
        if (_vm == null) return;
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "SAM-UI - pick folder(s) to process",
            AllowMultiple = true,
            SuggestedStartLocation = await StartFolder(),
        });
        List<string> paths = folders.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
        if (paths.Count > 0) _vm.Stage(paths);
    }

    // =================================================================== jukebox

    private static string Clock(double s) => s <= 0 ? "--:--" : $"{(int)s / 60:00}:{(int)s % 60:00}";

    private void UpdateJukebox()
    {
        Jukebox jb = App.Jukebox;
        JbTitle.Text = jb.Current == null ? (jb.Available ? Jukebox.NoSongsMessage : jb.Title) : jb.Title;
        ToolTip.SetTip(JbTitle, jb.Current?.Path);
        JbTime.Text = jb.Current == null ? "" : $"{Clock(jb.Position)}/{Clock(jb.Duration)}";
        JbProgress.Value = jb.Duration > 0 ? Math.Min(1, jb.Position / jb.Duration) : 0;
        JbPlay.Content = jb.Playing ? " || " : " > ";
        string count = jb.Count > 0 ? $"{jb.Index + 1}/{jb.Count}" : "";
        JbFormat.Text = string.Join(" · ", new[] { jb.Format, count }.Where(x => !string.IsNullOrEmpty(x)));
        JbShuffle.Classes.Set("on", jb.Shuffle);
    }

    private void JbPlay_Click(object? sender, RoutedEventArgs e) => App.Jukebox.TogglePlay();
    private void JbShuffle_Click(object? sender, RoutedEventArgs e) => App.Jukebox.ToggleShuffle();
    private void JbNext_Click(object? sender, RoutedEventArgs e) => App.Jukebox.Next();
    private void JbPrev_Click(object? sender, RoutedEventArgs e) => App.Jukebox.Previous();

    private void JbMore_Click(object? sender, RoutedEventArgs e) => OpenShell("https://modarchive.org/");

    private void JbFolder_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Jukebox.TunesFolder);
        OpenShell(Jukebox.TunesFolder);
    }

    private static void OpenShell(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* nothing useful to do */ }
    }

    private async void JbOpen_Click(object? sender, RoutedEventArgs e)
    {
        Jukebox jb = App.Jukebox;
        if (!jb.Available) return;
        List<string> exts = jb.Extensions.Select(x => "*" + x).OrderBy(x => x).ToList();
        string? start = jb.Current != null ? Path.GetDirectoryName(jb.Current.Path) : Jukebox.TunesFolder;
        var opts = new FilePickerOpenOptions
        {
            Title = "Open a tune",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Chiptunes & modules") { Patterns = exts },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        };
        if (start != null && Directory.Exists(start))
            opts.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(new Uri(start));
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(opts);
        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path != null) jb.Open(path);
    }
}
