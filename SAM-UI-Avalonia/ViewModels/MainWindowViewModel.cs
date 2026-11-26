using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Compress.StructuredZip;
using SAM_UI_Avalonia.Core;
using SAM_UI_Avalonia.Fx;
using SAM_UI_Avalonia.Models;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using TrrntZip;

namespace SAM_UI_Avalonia.ViewModels;

/// <summary>
/// Workflow:
///   1. STAGE  - drop files/folders anywhere on the window, use FILES / FOLDER, or type a path.
///               Nothing starts yet.
///   2. PREVIEW (optional) - scans the staged paths with the current input filter and lists
///               every item that would be processed, with sizes and totals.
///   3. START  - scans (if not already previewed) and queues the items. Each row goes
///               Queued -> Processing (with progress) -> result.
/// Staging/starting while a job is running adds to the running job.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly SamConfig _cfg;

    /// <summary>Rows in the output grid.</summary>
    [ObservableProperty]
    private ObservableCollection<samFile> _files = new();

    /// <summary>One entry per worker thread (not shown; used to track the workers).</summary>
    public ObservableCollection<procStatus> ProcessStats { get; } = new();

    /// <summary>Row for each fileId handed out by FileAdder. UI thread only.</summary>
    private readonly Dictionary<int, samFile> _rowsByFileId = new();

    /// <summary>Row each worker thread is currently processing. UI thread only.</summary>
    private readonly Dictionary<int, samFile> _rowsByThreadId = new();

    /// <summary>Queued rows waiting for a worker, by full path. UI thread only.</summary>
    private readonly Dictionary<string, Queue<samFile>> _queuedByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Result of the last PREVIEW, reused by START if nothing changed since.</summary>
    private ScanResult? _preview;

    private CancellationTokenSource? _scanCts;
    private readonly Stopwatch _jobClock = new();
    private readonly DispatcherTimer _statsTimer;

    /// <summary>Raised when a row starts processing, so the view can scroll to it.</summary>
    public event Action<samFile>? RowStarted;

    public MainWindowViewModel() : this(Design.IsDesignMode ? new SamConfig() : App.Config) { }

    public MainWindowViewModel(SamConfig cfg)
    {
        _cfg = cfg;
        _statsTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateTimeStats());

        CpuCount = Environment.ProcessorCount;
        MaxThreadCount = ThreadBudget;

        _selectedInputType = cfg.DefaultInput;
        _selectedOutputType = cfg.DefaultOutput;
        _selectedTheme = Skin.All.Any(s => s.Name == cfg.Theme) ? cfg.Theme : Skin.All[0].Name;
        _effectsOn = cfg.Effects;
        _scanlinesOn = cfg.Scanlines;
        _selectedShape = VectorBackdrop.ShapeNames.Contains(cfg.Shape) ? cfg.Shape : "AUTO";

        if (Design.IsDesignMode)
            return;

        ThreadCount = DefaultThreadsClamped;

        MainQueue.BusyChanged += OnQueueBusyChanged;
        IsRunning = MainQueue.IsBusy;
    }

    // =================================================================== threads (80% cap)

    /// <summary>Logical processors in this PC.</summary>
    public int CpuCount { get; }

    /// <summary>
    /// Never use more than 80% of the logical processors in total (files in parallel x
    /// compressor threads per file), so the OS stays responsive during heavy compression.
    /// </summary>
    public int ThreadBudget => Math.Max(1, (int)Math.Floor(CpuCount * 0.8));

    public string ThreadCapText => $"max {ThreadBudget} of {CpuCount} CPU threads (80% cap)";

    [ObservableProperty]
    private int _maxThreadCount = 1;

    [ObservableProperty]
    private int _threadCount = 1;

    /// <summary>Not saved by itself: threads are part of the user's defaults (★ SAVE AS DEFAULT).</summary>
    partial void OnThreadCountChanged(int value)
    {
        ThreadCountChanged(value);
        RefreshDefaults();
    }

    private void ThreadCountChanged(int value)
    {
        lock (Workers.lockObj)
        {
            Workers.requestedWorkers = value;

            while (Workers.requestedWorkers > Workers.workers)
            {
                CProcessZipAv newProc = new CProcessZipAv
                {
                    ThreadId = Workers.ThreadId,
                    ProcessFileStartCallBack = ProcessFileStartCallback,
                    StatusCallBack = StatusCallBack,
                    ErrorCallBack = ErrorCallBack,
                    ProcessFileEndCallBack = ProcessFileEndCallback,
                    ProcessorClosingCallBack = ProcessorClosingCallback,
                    pauseCancel = MainQueue.pc,
                    workerCount = 1
                };

                // Below normal priority: full speed when the PC is idle, but the desktop
                // always wins when you're using it.
                Thread thread = new Thread(newProc.MigrateZip) { Priority = ThreadPriority.BelowNormal, Name = $"SAM worker {Workers.ThreadId}" };
                thread.Start();

                procStatus ps = new procStatus() { Name = "", Progress = 0, CProcessZip = newProc };

                ProcessStats.Add(ps);
                Workers.workers++;
                Workers.ThreadId++;

                SetWorkerCount();
            }

            int extraWorkers = Workers.workers - Workers.requestedWorkers;
            for (int i = 0; i < extraWorkers; i++)
                MainQueue.bccFile.Add(new cFile() { fileId = -1, filename = "Removing" });
        }
    }

    /// <summary>
    /// Splits the thread budget between the file workers: each file's compressor gets
    /// budget / files threads (at least 1), so the total stays within the 80% cap.
    /// </summary>
    private void SetWorkerCount()
    {
        if (ProcessStats.Count == 0)
            return;

        int perFile = Math.Max(1, ThreadBudget / ProcessStats.Count);
        foreach (procStatus ps in ProcessStats)
            if (ps.CProcessZip != null)
                ps.CProcessZip.workerCount = perFile;

        if (!IsRunning)
        {
            int c = 1;
            foreach (procStatus ps in ProcessStats)
                ps.Name = $"Worker {c++}";
        }
    }

    // =================================================================== state

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(SoftStopCommand))]
    [NotifyCanExecuteChangedFor(nameof(HardStopCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyPropertyChangedFor(nameof(IsSpinning))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StartButtonText))]
    [NotifyPropertyChangedFor(nameof(IsReadyToStart))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    [NotifyPropertyChangedFor(nameof(IsSpinning))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _isPaused;

    /// <summary>Set by Soft Stop; cleared once the last in-progress file has finished.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SoftStopCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(IsReadyToStart))]
    private bool _isSoftStopping;

    /// <summary>True while a PREVIEW or START scan is walking the folders.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(IsSpinning))]
    [NotifyPropertyChangedFor(nameof(IsReadyToStart))]
    private bool _isScanning;

    /// <summary>The busy spinner turns while work runs (or a scan) and freezes while paused.</summary>
    public bool IsSpinning => (IsRunning && !IsPaused) || IsScanning;

    public string PauseButtonText => IsPaused ? "RESUME" : "PAUSE";

    /// <summary>Something is staged and START would begin a new job: the START button pulses.</summary>
    public bool IsReadyToStart => HasStaged && !IsRunning && !IsScanning && !IsSoftStopping;

    public string StartButtonText => IsRunning ? "ADD TO QUEUE" : "START";

    public string StateText
    {
        get
        {
            if (IsScanning) return "SCANNING";
            if (!IsRunning) return HasStaged ? "READY" : "IDLE";
            if (IsSoftStopping) return "STOPPING";
            if (IsPaused) return "PAUSED";
            return "WORKING";
        }
    }

    /// <summary>One-line message/hint at the left of the status bar.</summary>
    [ObservableProperty]
    private string _message = "Drop files or folders anywhere, or use FILES / FOLDER. Nothing starts until you press START.";

    private void OnQueueBusyChanged(bool busy)
    {
        OnUIThread(() =>
        {
            IsRunning = busy;
            if (!busy && MainQueue.pc.Stopping)
                MainQueue.pc.ResetCancel();
        });
    }

    partial void OnIsRunningChanged(bool value)
    {
        if (value)
        {
            _jobClock.Start();
            _statsTimer.Start();
            return;
        }

        // Everything has finished (or been stopped).
        bool stopped = IsSoftStopping || _queuedByPath.Count > 0;
        IsSoftStopping = false;
        IsPaused = false;
        if (MainQueue.pc.Paused)
            MainQueue.pc.UnPause();
        SetWorkerCount();
        MarkLeftoversSkipped();
        _jobClock.Stop();
        _statsTimer.Stop();
        UpdateTimeStats();
        EtaText = "";

        Message = stopped
            ? $"Stopped. {FilesOk:N0} OK, {FilesFailed:N0} failed, {FilesSkipped:N0} skipped/cancelled."
            : $"Job done in {ElapsedText}. {FilesOk:N0} OK, {FilesFailed:N0} failed.";
    }

    // =================================================================== staging

    /// <summary>
    /// The staged path(s). Several paths are shown separated by " | " ('|' can't appear in a
    /// Windows path). The text box is editable, so this is the source of truth.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStaged))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyPropertyChangedFor(nameof(IsReadyToStart))]
    private string _pathText = "";

    public bool HasStaged => StagedPaths().Count > 0;

    partial void OnPathTextChanged(string value) => _preview = null;

    public List<string> StagedPaths() =>
        PathText.Split('|')
            .Select(p => p.Trim().Trim('"').Trim())
            .Where(p => p.Length > 0)
            .ToList();

    /// <summary>Called by drag & drop and the FILES / FOLDER pickers.</summary>
    /// <param name="append">Ctrl held: add to what's already staged.</param>
    public void Stage(IReadOnlyList<string> paths, bool append = false)
    {
        if (paths.Count == 0) return;
        List<string> list = append ? StagedPaths() : new List<string>();
        foreach (string p in paths)
            if (!list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p);
        PathText = string.Join("  |  ", list);

        if (!IsRunning && !IsScanning)
            ClearGrid();

        int files = list.Count(File.Exists), dirs = list.Count(Directory.Exists);
        Message = $"STAGED {Describe(files, dirs)}  ->  {OutputName}  (input filter: {InputName}).  Press PREVIEW to look first, or START.";
    }

    private static string Describe(int files, int dirs)
    {
        var parts = new List<string>();
        if (dirs > 0) parts.Add(dirs == 1 ? "1 folder" : $"{dirs} folders");
        if (files > 0) parts.Add(files == 1 ? "1 file" : $"{files} files");
        return parts.Count == 0 ? "nothing that exists" : string.Join(" + ", parts);
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        _scanCts?.Cancel();
        PathText = "";
        if (!IsRunning)
        {
            ClearGrid();
            Message = "Cleared. Drop files or folders anywhere, or use FILES / FOLDER.";
        }
    }

    private bool CanClear() => PathText.Length > 0 || (!IsRunning && Files.Count > 0);

    private void ClearGrid()
    {
        MainQueue.fileCount = 0;
        _rowsByFileId.Clear();
        _rowsByThreadId.Clear();
        _queuedByPath.Clear();
        Files = new ObservableCollection<samFile>();
        _preview = null;
        ResetCounters();
        ClearCommand.NotifyCanExecuteChanged();
    }

    // =================================================================== preview / start

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task Preview()
    {
        ScanResult? scan = await ScanStaged();
        if (scan == null) return;

        _preview = scan;
        if (!IsRunning)
        {
            ClearGrid();
            _preview = scan;
            Files = new ObservableCollection<samFile>(scan.Items.Select(i => new samFile
            {
                Name = i.Path, Size = i.Size, Status = "Preview - not queued", IsPreview = true
            }));
            FilesTotal = scan.Items.Count;
            BytesTotal = scan.TotalBytes;
        }

        Message = scan.Items.Count == 0
            ? $"PREVIEW: nothing matches the input filter '{InputName}'. Check the IN setting."
            : $"PREVIEW: {scan.Items.Count:N0} item(s), {samFile.FormatSize(scan.TotalBytes)}  ->  {OutputName}{(IsDryRun ? "  [DRY RUN]" : "")}.  Press START to process them.";
    }

    private bool CanPreview() => HasStaged && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Start()
    {
        ScanResult? scan = _preview != null && _preview.Input == SelectedInputType ? _preview : await ScanStaged();
        _preview = null;
        if (scan == null) return;

        if (scan.Items.Count == 0)
        {
            Message = $"Nothing to do: no items match the input filter '{InputName}'.";
            return;
        }

        bool fresh = !IsRunning;
        if (fresh)
        {
            // New job: drop the previous results (and any preview rows).
            ClearGrid();
            _jobClock.Reset();
        }

        var newRows = new List<samFile>(scan.Items.Count);
        foreach (ScanItem item in scan.Items)
        {
            var row = new samFile { Name = item.Path, Size = item.Size, Status = "Queued", IsQueued = true };
            newRows.Add(row);
            if (!_queuedByPath.TryGetValue(item.Path, out Queue<samFile>? q))
                _queuedByPath[item.Path] = q = new Queue<samFile>();
            q.Enqueue(row);
        }
        Files = fresh ? new ObservableCollection<samFile>(newRows) : new ObservableCollection<samFile>(Files.Concat(newRows));

        FilesTotal += scan.Items.Count;
        BytesTotal += scan.TotalBytes;

        Settings settings = new Settings()
        {
            Repair = SelectedOutputType == OutputType.RepairKeepOriginal,
            InZip = SelectedInputType,
            OutZip = ComboOptions.ZipStructureFromUIIndex(SelectedOutputType),
            DryRun = IsDryRun
        };

        // Hand FileAdder the exact list we scanned (plus any leftover __*.tztmp temp files,
        // which FileAdder deletes), so what runs is exactly what was previewed.
        FileAdder pm = new FileAdder(MainQueue.bccFile, scan.HandOff.ToArray(), null, ProcessFileEndCallback, settings, MainQueue.pc);
        Thread procT = new Thread(pm.ProcFiles) { IsBackground = true, Name = "SAM file adder" };
        procT.Start();

        PathText = "";
        Message = $"{(fresh ? "STARTED" : "ADDED")}: {scan.Items.Count:N0} item(s), {samFile.FormatSize(scan.TotalBytes)}  ->  {OutputName}{(IsDryRun ? "  [DRY RUN - nothing is written]" : "")}";
    }

    private bool CanStart() => HasStaged && !IsScanning && !IsSoftStopping;

    /// <summary>Scans the staged paths off the UI thread. Null if cancelled or nothing valid.</summary>
    private async Task<ScanResult?> ScanStaged()
    {
        List<string> paths = StagedPaths();
        List<string> missing = paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            Message = "PATH NOT FOUND: " + string.Join("  |  ", missing);
            return null;
        }
        if (paths.Count == 0) return null;

        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();
        IsScanning = true;
        Message = "Scanning " + string.Join("  |  ", paths) + " ...";
        try
        {
            InputZipType input = SelectedInputType;
            return await Task.Run(() => Scanner.Scan(paths, input, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            Message = "Scan cancelled.";
            return null;
        }
        catch (Exception ex)
        {
            Message = "Scan failed: " + ex.Message;
            return null;
        }
        finally
        {
            if (ReferenceEquals(_scanCts, cts)) _scanCts = null;
            IsScanning = false;
        }
    }

    // =================================================================== pause / stop

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        IsPaused = !IsPaused;
        if (IsPaused != MainQueue.pc.Paused)
        {
            if (IsPaused)
                MainQueue.pc.Pause();
            else
                MainQueue.pc.UnPause();
        }
        if (IsPaused) _jobClock.Stop(); else _jobClock.Start();
        Message = IsPaused ? "PAUSED. Files in progress are frozen; press RESUME to carry on." : "Resumed.";
    }

    private bool CanPause() => IsRunning && !IsSoftStopping;

    /// <summary>
    /// Soft Stop: drop everything still waiting in the queue, but let the files
    /// that are already being processed finish normally.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSoftStop))]
    private void SoftStop()
    {
        IsSoftStopping = true;

        // Stops FileAdder adding more files and stops workers starting new ones.
        // Also un-pauses, so the in-progress files can run to completion.
        MainQueue.pc.SoftCancel();
        IsPaused = false;
        _jobClock.Start();

        ClearFileQueue();
        Message = "SOFT STOP: finishing the files in progress, the rest of the queue is skipped.";
    }

    private bool CanSoftStop() => IsRunning && !IsSoftStopping;

    /// <summary>
    /// Hard Stop: drop the queue and abort the files in progress immediately.
    /// Their partial temp files are deleted; the original files are untouched.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanHardStop))]
    private void HardStop()
    {
        ClearFileQueue();
        MainQueue.pc.Cancel();
        IsPaused = false;

        // clearing again just in case anything snuck in after the first clear.
        ClearFileQueue();
        Message = "HARD STOP: aborting now. Files in progress are left as they were.";
    }

    private bool CanHardStop() => IsRunning;

    /// <summary>
    /// Empties the pending file queue. <see cref="BlockingCollection{T}"/> has no
    /// Clear method, so drain every item that can be taken without blocking.
    /// "Removing" markers (used to shut down surplus worker threads when the
    /// thread slider is lowered) are put back so those workers still close.
    /// </summary>
    private static void ClearFileQueue()
    {
        BlockingCollection<cFile> queue = MainQueue.bccFile;

        if (queue is null)
            return;

        int removeMarkers = 0;
        while (queue.TryTake(out cFile? item))
        {
            if (item != null && item.fileId == -1 && item.filename == "Removing")
                removeMarkers++;
        }

        for (int i = 0; i < removeMarkers; i++)
            queue.Add(new cFile() { fileId = -1, filename = "Removing" });

        // Draining bypasses the normal worker flow, so re-evaluate the busy state.
        MainQueue.QueueChanged();
    }

    /// <summary>After a stop, rows that never got a worker are marked as skipped.</summary>
    private void MarkLeftoversSkipped()
    {
        foreach (Queue<samFile> q in _queuedByPath.Values)
            foreach (samFile row in q)
            {
                row.IsQueued = false;
                row.Status = "Skipped (stopped)";
                row.Kind = RowKind.Warn;
                FilesSkipped++;
            }
        _queuedByPath.Clear();
    }

    // =================================================================== formats & defaults

    public IReadOnlyList<EnumOption<InputZipType>> InputTypes { get; } = EnumOption<InputZipType>.CreateAll();

    public IReadOnlyList<EnumOption<OutputType>> OutputTypes { get; } = EnumOption<OutputType>.CreateAll();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOffDefault))]
    [NotifyPropertyChangedFor(nameof(DefaultsHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveDefaultsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetDefaultsCommand))]
    private InputZipType _selectedInputType = InputZipType.Zip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOffDefault))]
    [NotifyPropertyChangedFor(nameof(DefaultsHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveDefaultsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetDefaultsCommand))]
    private OutputType _selectedOutputType = OutputType.ZipTorrent;

    partial void OnSelectedInputTypeChanged(InputZipType value) => _preview = null;

    /// <summary>Dry Run is never remembered: it always starts off.</summary>
    [ObservableProperty]
    private bool _isDryRun;

    private string InputName => InputTypes.First(t => t.Value == SelectedInputType).Display;
    private string OutputName => SelectedOutputType == OutputType.RepairKeepOriginal
        ? "REPAIR (keep original format)"
        : OutputTypes.First(t => t.Value == SelectedOutputType).Display;

    private int DefaultThreadsClamped => Math.Clamp(_cfg.DefaultThreads, 1, MaxThreadCount);

    /// <summary>True when IN/OUT/THREADS differ from the saved defaults (shown as a warning).</summary>
    public bool IsOffDefault =>
        SelectedInputType != _cfg.DefaultInput || SelectedOutputType != _cfg.DefaultOutput || ThreadCount != DefaultThreadsClamped;

    public string DefaultsHint
    {
        get
        {
            string d = $"IN {EnumOption<InputZipType>.Describe(_cfg.DefaultInput)}  ->  OUT {EnumOption<OutputType>.Describe(_cfg.DefaultOutput)}  ·  {DefaultThreadsClamped} thread(s)";
            return IsOffDefault
                ? $"!! NOT YOUR DEFAULT - yours is: {d}. This resets on next launch unless you save it."
                : $"Your default: {d}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsOffDefault))]
    private void SaveDefaults()
    {
        _cfg.DefaultInput = SelectedInputType;
        _cfg.DefaultOutput = SelectedOutputType;
        _cfg.DefaultThreads = ThreadCount;
        _cfg.Save();
        RefreshDefaults();
        Message = $"Saved as default: {InputName} -> {OutputName}, {ThreadCount} thread(s). SAM-UI always starts on these.";
    }

    [RelayCommand(CanExecute = nameof(IsOffDefault))]
    private void ResetDefaults()
    {
        SelectedInputType = _cfg.DefaultInput;
        SelectedOutputType = _cfg.DefaultOutput;
        ThreadCount = DefaultThreadsClamped;
    }

    private void RefreshDefaults()
    {
        OnPropertyChanged(nameof(IsOffDefault));
        OnPropertyChanged(nameof(DefaultsHint));
        SaveDefaultsCommand.NotifyCanExecuteChanged();
        ResetDefaultsCommand.NotifyCanExecuteChanged();
    }

    // =================================================================== look

    public IReadOnlyList<string> Themes { get; } = Skin.All.Select(s => s.Name).ToList();
    public IReadOnlyList<string> ShapeNames { get; } = VectorBackdrop.ShapeNames;

    [ObservableProperty]
    private string _selectedTheme;

    [ObservableProperty]
    private bool _effectsOn;

    [ObservableProperty]
    private bool _scanlinesOn;

    [ObservableProperty]
    private string _selectedShape;

    partial void OnSelectedThemeChanged(string value)
    {
        Skin.Apply(value);
        _cfg.Theme = value;
        _cfg.Save();
    }

    partial void OnEffectsOnChanged(bool value)
    {
        FxClock.Enabled = value;
        _cfg.Effects = value;
        _cfg.Save();
    }

    partial void OnScanlinesOnChanged(bool value)
    {
        _cfg.Scanlines = value;
        _cfg.Save();
    }

    partial void OnSelectedShapeChanged(string value)
    {
        VectorBackdrop.Mode = value;
        _cfg.Shape = value;
        _cfg.Save();
    }

    // ---- layers (SETUP window)

    public bool ShowScroller
    {
        get => _cfg.ShowScroller;
        set { _cfg.ShowScroller = value; _cfg.Save(); OnPropertyChanged(); }
    }

    public bool ShowCopper
    {
        get => _cfg.ShowCopper;
        set { _cfg.ShowCopper = FxSettings.Copper = value; _cfg.Save(); OnPropertyChanged(); }
    }

    public bool ShowStarfield
    {
        get => _cfg.ShowStarfield;
        set { _cfg.ShowStarfield = FxSettings.Starfield = value; _cfg.Save(); OnPropertyChanged(); }
    }

    public bool ShowObjects
    {
        get => _cfg.ShowObjects;
        set { _cfg.ShowObjects = FxSettings.Objects = value; _cfg.Save(); OnPropertyChanged(); }
    }

    public bool ShowBalls
    {
        get => _cfg.ShowBalls;
        set { _cfg.ShowBalls = FxSettings.Balls = value; _cfg.Save(); OnPropertyChanged(); }
    }

    public bool ShowFloor
    {
        get => _cfg.ShowFloor;
        set { _cfg.ShowFloor = FxSettings.Floor = value; _cfg.Save(); OnPropertyChanged(); }
    }

    public int MinBalls => FxSettings.MinBalls;
    public int MaxBalls => FxSettings.MaxBalls;

    public int BallCount
    {
        get => FxSettings.BallCount;
        set
        {
            FxSettings.BallCount = value;
            _cfg.BallCount = FxSettings.BallCount;
            _cfg.Save();
            OnPropertyChanged();
        }
    }

    public int MinStars => FxSettings.MinStars;
    public int MaxStars => FxSettings.MaxStars;

    public int StarCount
    {
        get => FxSettings.StarCount;
        set
        {
            FxSettings.StarCount = value;
            _cfg.StarCount = FxSettings.StarCount;
            _cfg.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>The "A" easter egg on the main window.</summary>
    public void Boing()
    {
        VectorBackdrop.TriggerBoing();
        Message = "BOING!";
    }

    /// <summary>The + / - easter egg on the main window.</summary>
    public void NudgeBalls(int delta)
    {
        if (!ShowBalls) ShowBalls = true;
        BallCount += delta;
        Message = $"VECTOR BALLS: {BallCount}   (+ / - for more or less)";
    }

    // ---- startup song (SETUP)

    public const string LastPlayedChoice = "(LAST SONG PLAYED)";
    private Dictionary<string, string>? _tuneByName;

    /// <summary>"(LAST SONG PLAYED)" plus every tune in the Tunes folder.</summary>
    public IReadOnlyList<string> StartupTuneChoices
    {
        get
        {
            _tuneByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Design.IsDesignMode && App.Jukebox != null)
            {
                foreach (string p in App.Jukebox.TunesIn(Audio.Jukebox.TunesFolder))
                    _tuneByName[Path.GetFileName(p)] = p;
                // a startup song picked from another folder (OPEN) stays selectable
                if (!string.IsNullOrEmpty(_cfg.StartupTune) && File.Exists(_cfg.StartupTune))
                    _tuneByName.TryAdd(Path.GetFileName(_cfg.StartupTune), _cfg.StartupTune);
            }
            return new[] { LastPlayedChoice }.Concat(_tuneByName.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)).ToList();
        }
    }

    public string SelectedStartupTune
    {
        get => !string.IsNullOrEmpty(_cfg.StartupTune) && File.Exists(_cfg.StartupTune)
            ? Path.GetFileName(_cfg.StartupTune)
            : LastPlayedChoice;
        set
        {
            if (value == null) return;
            _cfg.StartupTune = value == LastPlayedChoice ? null
                : (_tuneByName ?? new()).TryGetValue(value, out string? p) ? p : null;
            _cfg.Save();
            OnPropertyChanged();
        }
    }

    public bool AutoPlayMusic
    {
        get => _cfg.AutoPlayMusic;
        set { _cfg.AutoPlayMusic = value; _cfg.Save(); OnPropertyChanged(); }
    }

    /// <summary>What the sine scroller shows: the user's text from SETUP, or the built-in one.</summary>
    public string ScrollText => string.IsNullOrWhiteSpace(_cfg.ScrollText) ? DefaultScrollText : _cfg.ScrollText;

    private string? _scrollEdit;

    /// <summary>The SETUP text box. Can be empty while editing (the scroller then shows the built-in text).</summary>
    public string ScrollEditText
    {
        get => _scrollEdit ??= ScrollText;
        set
        {
            _scrollEdit = (value ?? "").Replace("\r", " ").Replace("\n", " ");
            _cfg.ScrollText = string.IsNullOrWhiteSpace(_scrollEdit) || _scrollEdit == DefaultScrollText ? null : _scrollEdit;
            _cfg.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScrollText));
        }
    }

    [RelayCommand]
    private void ResetScrollText() => ScrollEditText = DefaultScrollText;

    public const string DefaultScrollText =
        "EGGMAN PRESENTS  -  SAM-UI  -  THE STRUCTURED ARCHIVE MAKER  ***  " +
        "TORRENTZIP  //  ZIP-ZSTD  //  7Z-ZSTD  //  7Z-LZMA  ***  " +
        "DROP YOUR FILEZ, HIT PREVIEW, HIT START, KICK BACK  ***  " +
        "SAM AND TRRNTZIP CODE BY THE ROMVAULT TEAM - RESPECT!  ***  " +
        "JUKEBOX TUNES BY THE LEGENDS OF THE TRACKER SCENE - CRANK IT UP  ***  " +
        "NEVER MORE THAN 80 PERCENT OF YOUR CPU - YOUR DESKTOP STAYS ALIVE  ***  " +
        "GREETZ TO ALL DATA HOARDERS, DAT MAKERS, ROM PRESERVATIONISTS AND BIT-PERFECT FREAKS  ***  " +
        "REMEMBER: CHECK YOUR OUTPUT FORMAT BEFORE A FIVE HOUR RUN!  ***  WRAP!";

    // =================================================================== counters / status bar

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CounterText))]
    [NotifyPropertyChangedFor(nameof(OverallPercent))]
    private int _filesDone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CounterText))]
    [NotifyPropertyChangedFor(nameof(OverallPercent))]
    private int _filesTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultText))]
    private int _filesOk;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultText))]
    private int _filesFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultText))]
    private int _filesSkipped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BytesText))]
    private long _bytesTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BytesText))]
    private long _bytesDone;

    [ObservableProperty]
    private string _elapsedText = "00:00:00";

    [ObservableProperty]
    private string _etaText = "";

    [ObservableProperty]
    private string _speedText = "";

    public string CounterText => FilesTotal == 0
        ? "FILES  -"
        : $"FILES  {FilesDone:N0} / {FilesTotal:N0}  ({OverallPercent:0.0}%)";

    public string ResultText => $"OK {FilesOk:N0}  FAIL {FilesFailed:N0}  SKIP {FilesSkipped:N0}";

    public string BytesText => BytesTotal <= 0
        ? ""
        : $"{samFile.FormatSize(BytesDone)} / {samFile.FormatSize(BytesTotal)}";

    /// <summary>Overall progress (0-100) for the status bar.</summary>
    public double OverallPercent => FilesTotal <= 0 ? 0 : Math.Min(100.0, FilesDone * 100.0 / FilesTotal);

    private void ResetCounters()
    {
        FilesDone = FilesTotal = FilesOk = FilesFailed = FilesSkipped = 0;
        BytesDone = BytesTotal = 0;
        ElapsedText = "00:00:00";
        EtaText = SpeedText = "";
    }

    private static string Clock(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    /// <summary>Elapsed, throughput and a size-based ETA (includes the in-progress files' %).</summary>
    private void UpdateTimeStats()
    {
        TimeSpan el = _jobClock.Elapsed;
        ElapsedText = Clock(el);
        if (!IsRunning) { SpeedText = ""; return; }

        double partial = 0;
        foreach (samFile r in _rowsByThreadId.Values)
            if (r.Size > 0) partial += r.Size * (r.Progress / 100.0);
        double done = BytesDone + partial;

        if (el.TotalSeconds < 3 || done <= 0)
        {
            EtaText = "ETA --:--:--";
            SpeedText = "";
            return;
        }
        double rate = done / el.TotalSeconds;
        SpeedText = samFile.FormatSize((long)rate) + "/s";
        double left = Math.Max(0, BytesTotal - done);
        EtaText = IsPaused ? "ETA paused" : "ETA " + Clock(TimeSpan.FromSeconds(Math.Min(left / rate, 359999)));
    }

    // =================================================================== worker callbacks

    /// <summary>
    /// All of the callbacks below are raised from the TrrntZip worker threads.
    /// Avalonia only allows the UI thread to touch bound collections and
    /// observable properties, so every update is posted to the dispatcher.
    /// </summary>
    private static void OnUIThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private void ProcessorClosingCallback(int processId)
    {
        if (processId == -1)
            return;
        OnUIThread(() =>
        {
            lock (Workers.lockObj)
            {
                foreach (procStatus proc in ProcessStats)
                {
                    if (proc.CProcessZip?.ThreadId == processId)
                    {
                        ProcessStats.Remove(proc);
                        break;
                    }
                }
                SetWorkerCount();
            }
        });
    }

    private void ProcessFileStartCallback(int processId, int fileId, string filename, long size)
    {
        OnUIThread(() =>
        {
            samFile? row = null;
            if (_queuedByPath.TryGetValue(filename, out Queue<samFile>? q) && q.Count > 0)
            {
                row = q.Dequeue();
                if (q.Count == 0) _queuedByPath.Remove(filename);
            }
            if (row == null)
            {
                // Not from our scan (should not happen) - show it anyway.
                row = new samFile { Name = filename };
                Files.Add(row);
            }
            if (row.Size < 0 && size >= 0) row.Size = size; // the worker reports the file size too

            row.IsQueued = false;
            row.Status = "Processing";
            row.Progress = 0;
            row.IsProcessing = true;
            row.Kind = RowKind.Working;

            _rowsByFileId[fileId] = row;
            _rowsByThreadId[processId] = row;
            RowStarted?.Invoke(row);
        });
    }

    private void ProcessFileEndCallback(int processId, int fileId, TrrntZipStatus trrntZipStatus, ZipStructure zipStruct)
    {
        if (processId == -1)
            return;

        OnUIThread(() =>
        {
            // Files skipped because of a stop were never started, so have no row.
            if (!_rowsByFileId.TryGetValue(fileId, out samFile? row))
                return;

            _rowsByFileId.Remove(fileId);
            if (_rowsByThreadId.TryGetValue(processId, out samFile? active) && ReferenceEquals(active, row))
                _rowsByThreadId.Remove(processId);

            FilesDone++;
            if (row.Size > 0 && (trrntZipStatus & TrrntZipStatus.Cancel) == 0) BytesDone += row.Size;

            string name = StructuredArchive.GetZipStructureName(zipStruct);
            string status;
            RowKind kind = RowKind.Good;

            // A hard stop can come back as Cancel combined with other flags (e.g. Cancel | NeedsRepaired).
            bool cancelled = (trrntZipStatus & TrrntZipStatus.Cancel) != 0;
            if (cancelled)
                trrntZipStatus = TrrntZipStatus.Cancel;

            switch (trrntZipStatus)
            {
                case TrrntZipStatus.ValidTrrntzip:
                    status = $"Already valid {name}";
                    break;
                case TrrntZipStatus.Trrntzipped:
                    status = $"Converted to {name}";
                    break;
                case TrrntZipStatus.NeedsRepaired:
                    status = $"Needs RePack to {name}";
                    kind = RowKind.Warn;
                    break;
                case TrrntZipStatus.Trrntzipped | TrrntZipStatus.NeedsRepaired:
                    status = $"RePacked to {name}";
                    break;
                case TrrntZipStatus.Cancel:
                    status = "Cancelled (original unchanged)";
                    kind = RowKind.Warn;
                    break;
                default:
                    status = trrntZipStatus.ToString();
                    const TrrntZipStatus errors = TrrntZipStatus.CorruptZip | TrrntZipStatus.ErrorOutputFile |
                                                  TrrntZipStatus.SourceFileLocked | TrrntZipStatus.CatchError |
                                                  TrrntZipStatus.RepeatFilesFound;
                    kind = (trrntZipStatus & errors) != 0 ? RowKind.Bad : RowKind.Good;
                    break;
            }

            if (kind == RowKind.Bad) FilesFailed++;
            else if (trrntZipStatus == TrrntZipStatus.Cancel) FilesSkipped++;
            else FilesOk++;

            row.Status = status;
            row.Kind = kind;
            row.Progress = 100;
            row.IsProcessing = false;
        });
    }

    private void StatusCallBack(int processId, int fileId, int percent)
    {
        OnUIThread(() =>
        {
            // Progress is reported per file, so it can never land on the wrong row.
            if (_rowsByFileId.TryGetValue(fileId, out samFile? row))
                row.Progress = percent;
        });
    }

    private void ErrorCallBack(int processId, string message)
    {
    }
}

// =====================================================================================

public enum RowKind { Neutral, Working, Good, Warn, Bad }

public partial class samFile : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _status = "";

    /// <summary>Original size in bytes (folder total in Directory mode), -1 if unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private long _size = -1;

    /// <summary>Percent complete (0-100) while this file is being processed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    [NotifyPropertyChangedFor(nameof(IsStarting))]
    private int _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(IsStarting))]
    private bool _isProcessing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private bool _isQueued;

    [ObservableProperty]
    private bool _isPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGood))]
    [NotifyPropertyChangedFor(nameof(IsWarn))]
    [NotifyPropertyChangedFor(nameof(IsBad))]
    [NotifyPropertyChangedFor(nameof(IsWorking))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private RowKind _kind;

    public bool IsGood => Kind == RowKind.Good;
    public bool IsWarn => Kind == RowKind.Warn;
    public bool IsBad => Kind == RowKind.Bad;
    public bool IsWorking => Kind == RowKind.Working;

    /// <summary>
    /// Processing but no progress reported yet (the backend first reads/checks the whole archive,
    /// which can take minutes on big files): an animated stripe shows it's alive.
    /// </summary>
    public bool IsStarting => IsProcessing && Progress == 0;

    /// <summary>0..1 for the progress fill drawn behind the filename.</summary>
    public double ProgressFraction => Math.Clamp(Progress / 100.0, 0, 1);

    public string ProgressText => Kind switch
    {
        RowKind.Working => $"{Progress}%",
        RowKind.Good => "OK",
        RowKind.Warn => "--",
        RowKind.Bad => "ERR",
        _ => IsQueued ? "..." : "",
    };

    public string SizeText => FormatSize(Size);

    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return "";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.00} {units[unit]}";
    }
}

public partial class procStatus : ObservableObject
{
    public CProcessZipAv? CProcessZip;

    [ObservableProperty]
    private string _name = "";
    [ObservableProperty]
    private int _progress = 0;
}
