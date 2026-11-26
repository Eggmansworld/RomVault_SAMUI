using Avalonia.Threading;
using SAM_UI_Avalonia.Audio.Sid;
using SAM_UI_Avalonia.Core;

namespace SAM_UI_Avalonia.Audio;

public sealed record TuneEntry(string Path, int Sub)
{
    public string Display => System.IO.Path.GetFileName(Path) + (Sub > 0 ? $" #{Sub}" : "");
}

/// <summary>
/// The jukebox (ported from Eggman's Desktop Manager): plays tracker modules (libopenmpt) and
/// SIDs from a folder. Unlike the Desktop Manager it keeps playing while the window is minimised,
/// since a long recompress job is exactly when you want it in the background.
/// It never starts by itself: the user presses play.
/// Runs on the UI thread; audio rendering happens on the StreamOut feeder thread.
/// </summary>
public sealed class Jukebox
{
    public const string DefaultTune = "echoing (banana).mod";

    private readonly SamConfig _cfg;
    private readonly StreamOut _out = new();
    private readonly DispatcherTimer _timer;
    private List<TuneEntry> _list = new();
    private int _index = -1;
    private bool _loading;
    private int _gen;
    private int _failStreak;
    private bool _endFading;
    private HashSet<string>? _exts;

    public TuneEntry? Current => _index >= 0 && _index < _list.Count ? _list[_index] : null;
    public string Title { get; private set; } = "NO TUNE LOADED";
    public string Format { get; private set; } = "";
    public double Duration { get; private set; }
    public double Position => _out.PositionSeconds;
    public bool Playing => _cfg.JukeboxEnabled;
    public int Count => _list.Count;
    public int Index => _index;
    public bool Available => OperatingSystem.IsWindows();
    /// <summary>Peak level of the last audio buffer (0-32767), for the little VU meter.</summary>
    public int Level => _out.IsRunning ? _out.LastPeak : 0;

    public event Action? Changed;

    public Jukebox(SamConfig cfg)
    {
        _cfg = cfg;
        _cfg.JukeboxEnabled = false; // never blast music at start-up
        _out.Volume = cfg.JukeboxVolume / 100f;
        _out.Ended += () => Dispatcher.UIThread.Post(() => { if (!_endFading) Next(); });
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Tick());
    }

    public HashSet<string> Extensions
    {
        get
        {
            if (_exts != null) return _exts;
            _exts = OpenMpt.SupportedExtensions();
            _exts.Add(".sid");
            return _exts;
        }
    }

    public static string TunesFolder => Path.Combine(AppContext.BaseDirectory, "Tunes");

    /// <summary>Pick the remembered tune, or the default one in the Tunes folder (loaded silently).</summary>
    public void Init()
    {
        if (!Available) { Title = "JUKEBOX NEEDS WINDOWS"; Changed?.Invoke(); return; }
        string? path = _cfg.JukeboxTune;
        int sub = _cfg.JukeboxSub;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            string def = Path.Combine(TunesFolder, DefaultTune);
            path = File.Exists(def) ? def : null;
            sub = 0;
            if (path == null && Directory.Exists(TunesFolder))
                path = Directory.EnumerateFiles(TunesFolder).Where(IsTune).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }
        if (path == null) { Changed?.Invoke(); return; }
        LoadFolder(path, sub);
        Changed?.Invoke();
    }

    /// <summary>Auto-play at start-up: always the default tune ("echoing (banana)").</summary>
    public void PlayDefault()
    {
        if (!Available) return;
        string def = Path.Combine(TunesFolder, DefaultTune);
        if (File.Exists(def)) Open(def);
        else if (Current != null) StartCurrent(forcePlay: true);
    }

    public bool IsTune(string f) => Extensions.Contains(Path.GetExtension(f));

    private void LoadFolder(string file, int sub)
    {
        string dir = Path.GetDirectoryName(file)!;
        var list = new List<TuneEntry>();
        foreach (string f in Directory.EnumerateFiles(dir).Where(IsTune).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetExtension(f).Equals(".sid", StringComparison.OrdinalIgnoreCase))
            {
                var (songs, start) = SidSource.SongInfo(f);
                List<int>? picks = Picks(f);
                if (picks != null)
                {
                    foreach (int n in picks.Where(n => n >= 1 && n <= songs)) list.Add(new TuneEntry(f, n));
                }
                else
                {
                    // the tune's own start song first (usually the title music), then the rest
                    if (start >= 1 && start <= songs) list.Add(new TuneEntry(f, start));
                    for (int i = 1; i <= songs; i++) if (i != start) list.Add(new TuneEntry(f, i));
                }
            }
            else list.Add(new TuneEntry(f, 0));
        }
        _list = list;
        if (list.Any(e => e.Sub > 0 && Picks(e.Path) == null)) _ = PruneSidEffectsAsync(list);
        _index = list.FindIndex(e => e.Path.Equals(file, StringComparison.OrdinalIgnoreCase) && (sub <= 0 || e.Sub == sub || e.Sub == 0));
        if (_index < 0) _index = list.FindIndex(e => e.Path.Equals(file, StringComparison.OrdinalIgnoreCase));
        if (_index < 0 && list.Count > 0) _index = 0;
        if (Current != null) Title = Path.GetFileNameWithoutExtension(Current.Path);
    }

    /// <summary>
    /// Game SIDs carry their one-second sound effects as subtunes. Render the first few seconds of each
    /// subtune off-thread and drop the ones that have gone silent, so next/previous only lands on music.
    /// </summary>
    private async Task PruneSidEffectsAsync(List<TuneEntry> list)
    {
        HashSet<TuneEntry> drop = await Task.Run(() =>
        {
            var set = new HashSet<TuneEntry>();
            short[] buf = new short[4096 * 2];
            foreach (TuneEntry e in list.Where(e => e.Sub > 0 && Picks(e.Path) == null))
            {
                try
                {
                    using var src = new SidSource(e.Path, e.Sub);
                    int peakLate = 0;
                    for (int f = 0; f < StreamOut.Rate * 6; f += 4096)
                    {
                        src.Render(buf, 4096);
                        if (f > StreamOut.Rate * 3)
                            for (int i = 0; i < buf.Length; i += 5) peakLate = Math.Max(peakLate, Math.Abs((int)buf[i]));
                    }
                    if (peakLate < 60) set.Add(e);
                }
                catch { set.Add(e); }
            }
            return set;
        });
        if (!ReferenceEquals(list, _list) || drop.Count == 0) return;
        // never drop everything from a file
        foreach (var g in list.Where(e => e.Sub > 0).GroupBy(e => e.Path))
            if (g.All(drop.Contains)) drop.Remove(g.First());
        TuneEntry? cur = Current;
        _list = list.Where(e => !drop.Contains(e)).ToList();
        _index = cur == null ? 0 : Math.Max(0, _list.IndexOf(cur));
        if (cur != null && drop.Contains(cur)) _index = Math.Max(0, _list.FindIndex(e => e.Path == cur.Path));
        Changed?.Invoke();
    }

    private static string PickKey(string path) => Path.GetFileName(path).ToLowerInvariant();

    public List<int>? Picks(string path) =>
        _cfg.SidPicks.TryGetValue(PickKey(path), out List<int>? l) && l.Count > 0 ? l : null;

    public void Shutdown() => _out.Stop();

    /// <summary>Raised when play/pause state changes (for the taskbar jump list).</summary>
    public event Action<bool>? PlayingChanged;
    private bool _lastPlaying;

    private void RaisePlaying()
    {
        if (_lastPlaying == _cfg.JukeboxEnabled) return;
        _lastPlaying = _cfg.JukeboxEnabled;
        PlayingChanged?.Invoke(_lastPlaying);
    }

    // ------------------------------------------------------------------ transport

    public void TogglePlay()
    {
        if (!Available || _list.Count == 0) return;
        _cfg.JukeboxEnabled = !_cfg.JukeboxEnabled;
        if (_cfg.JukeboxEnabled)
        {
            if (_out.Source != null) { _out.Resume(0.2); _timer.Start(); }
            else StartCurrent();
        }
        else _out.FadeOutAndPause(0.3);
        RaisePlaying();
        Changed?.Invoke();
    }

    private readonly List<TuneEntry> _history = new();
    private readonly HashSet<TuneEntry> _shufflePlayed = new();
    private readonly Random _rng = new();

    public bool Shuffle => _cfg.JukeboxShuffle;

    public void ToggleShuffle()
    {
        _cfg.JukeboxShuffle = !_cfg.JukeboxShuffle;
        _cfg.Save();
        _shufflePlayed.Clear();
        if (Current != null) _shufflePlayed.Add(Current);
        if (_cfg.JukeboxShuffle && _cfg.JukeboxEnabled) Next();
        else Changed?.Invoke();
    }

    public void Next()
    {
        if (_list.Count == 0) return;
        if (Current != null) { _history.Add(Current); if (_history.Count > 200) _history.RemoveAt(0); }
        if (_cfg.JukeboxShuffle && _list.Count > 1)
        {
            var left = _list.Where(e => !_shufflePlayed.Contains(e) && e != Current).ToList();
            if (left.Count == 0) { _shufflePlayed.Clear(); left = _list.Where(e => e != Current).ToList(); }
            TuneEntry pick = left[_rng.Next(left.Count)];
            _shufflePlayed.Add(pick);
            _index = _list.IndexOf(pick);
            StartCurrent(forcePlay: true);
            return;
        }
        Step(+1);
    }

    public void Previous()
    {
        // like a CD player: back to the start first, previous tune if already near the start
        if (_out.Source != null && Position > 4) { StartCurrent(forcePlay: true); return; }
        while (_history.Count > 0)
        {
            TuneEntry prev = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            int i = _list.IndexOf(prev);
            if (i >= 0) { _index = i; StartCurrent(forcePlay: true); return; }
        }
        Step(-1);
    }

    private void Step(int dir)
    {
        if (_list.Count == 0) return;
        _index = (_index + dir + _list.Count) % _list.Count;
        StartCurrent(forcePlay: true);
    }

    public void Open(string path)
    {
        if (!File.Exists(path)) return;
        LoadFolder(path, 0);
        StartCurrent(forcePlay: true);
    }

    public void SetVolume(int v)
    {
        _cfg.JukeboxVolume = Math.Clamp(v, 0, 100);
        _out.Volume = _cfg.JukeboxVolume / 100f;
    }

    private async void StartCurrent(bool forcePlay = false)
    {
        TuneEntry? e = Current;
        if (e == null) return;
        int gen = ++_gen;
        if (forcePlay) _cfg.JukeboxEnabled = true;
        _cfg.JukeboxTune = e.Path;
        _cfg.JukeboxSub = e.Sub;
        _cfg.Save();
        _endFading = false;
        _loading = true;
        Title = e.Display;
        Changed?.Invoke();
        try
        {
            ITuneSource src = await Task.Run<ITuneSource>(() =>
                Path.GetExtension(e.Path).Equals(".sid", StringComparison.OrdinalIgnoreCase)
                    ? new SidSource(e.Path, e.Sub)
                    : new ModSource(e.Path));
            if (gen != _gen) { src.Dispose(); return; } // user skipped while loading
            Title = src.Title;
            Format = src.Format;
            Duration = src.Duration > 0 ? src.Duration : (src is SidSource ? _cfg.SidSeconds : 0);
            _failStreak = 0;
            _out.Play(src);
            if (_cfg.JukeboxEnabled) _timer.Start();
            else _out.FadeOutAndPause(0);
        }
        catch (Exception ex)
        {
            Log.Warn($"Jukebox can't play {e.Display}: {ex.Message}");
            Title = "CAN'T PLAY: " + e.Display;
            if (gen == _gen && ++_failStreak < Math.Min(_list.Count, 10)) { _loading = false; Step(+1); return; }
        }
        finally
        {
            _loading = false;
            RaisePlaying();
            Changed?.Invoke();
        }
    }

    private DateTime _quietSince = DateTime.MaxValue;

    private void Tick()
    {
        // SID sound-effect subtunes go silent after a second: move on after 3 s of silence
        if (_out.Source is SidSource && _out.IsRunning && !_endFading && Position > 1)
        {
            if (_out.LastPeak < 60) { if (_quietSince == DateTime.MaxValue) _quietSince = DateTime.Now; }
            else _quietSince = DateTime.MaxValue;
            if ((DateTime.Now - _quietSince).TotalSeconds >= 3) { _quietSince = DateTime.MaxValue; Next(); return; }
        }
        else _quietSince = DateTime.MaxValue;

        // SIDs never end by themselves: fade and move on after the configured time
        if (_out.Source is SidSource && !_endFading && Position >= _cfg.SidSeconds && _cfg.SidSeconds > 0)
        {
            _endFading = true;
            _out.FadeOutAndPause(3.0);
            DispatcherTimer.RunOnce(() => { if (_endFading) Next(); }, TimeSpan.FromSeconds(3.2));
        }
        Changed?.Invoke();
        if (!_out.IsRunning && !_loading) _timer.Stop();
    }
}
