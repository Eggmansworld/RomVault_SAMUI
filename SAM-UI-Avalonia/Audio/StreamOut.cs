using System.Runtime.InteropServices;

namespace SAM_UI_Avalonia.Audio;

/// <summary>Something that renders 44.1 kHz interleaved stereo 16-bit audio.</summary>
public interface ITuneSource : IDisposable
{
    /// <summary>Fills up to <paramref name="frames"/> stereo frames; returns how many were written (fewer = song ended).</summary>
    int Render(short[] buffer, int frames);
    string Title { get; }
    string Format { get; }
    /// <summary>Length in seconds, or 0 when unknown.</summary>
    double Duration { get; }
}

/// <summary>
/// Streaming waveOut player: a feeder thread keeps four buffers queued. Gain ramps give fades;
/// when a fade to silence finishes with pause requested, the device is closed so nothing runs while idle.
/// </summary>
public sealed class StreamOut
{
    public const int Rate = 44100;
    private const int Frames = 2048, Buffers = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }

    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr hwo, uint dev, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, uint flags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr hwo);

    private const uint CALLBACK_EVENT = 0x50000, WHDR_DONE = 1;

    private readonly object _gate = new();
    private ITuneSource? _source;
    private Thread? _thread;
    private volatile bool _stop;
    private float _gain, _target = 1f, _step;
    private bool _pauseWhenSilent;
    private long _framesPlayed;

    /// <summary>Raised on the feeder thread when the source runs out.</summary>
    public event Action? Ended;

    public float Volume { get; set; } = 0.6f;
    /// <summary>Peak of the source (before volume/fade) in the last buffer: used for silence detection.</summary>
    public volatile int LastPeak;
    public bool IsRunning => _thread != null;
    public double PositionSeconds => Interlocked.Read(ref _framesPlayed) / (double)Rate;
    public ITuneSource? Source { get { lock (_gate) return _source; } }

    /// <summary>Swaps in a new source (disposing the old one) and plays it with a short fade-in.</summary>
    public void Play(ITuneSource source, double fadeInSeconds = 0.05)
    {
        ITuneSource? old;
        lock (_gate)
        {
            old = _source;
            _source = source;
            Interlocked.Exchange(ref _framesPlayed, 0);
            _gain = 0;
            FadeLocked(1f, fadeInSeconds, false);
        }
        old?.Dispose();
        EnsureThread();
    }

    /// <summary>Continue the current source (after a fade-out pause).</summary>
    public void Resume(double fadeInSeconds = 0.4)
    {
        lock (_gate)
        {
            if (_source == null) return;
            FadeLocked(1f, fadeInSeconds, false);
        }
        EnsureThread();
    }

    /// <summary>Fade to silence over <paramref name="seconds"/>, then close the device (the source keeps its position).</summary>
    public void FadeOutAndPause(double seconds)
    {
        lock (_gate)
        {
            if (_thread == null) return;
            FadeLocked(0f, seconds, true);
        }
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(1000);
        _thread = null;
        lock (_gate) { _source?.Dispose(); _source = null; }
    }

    private void FadeLocked(float target, double seconds, bool pause)
    {
        _target = target;
        _pauseWhenSilent = pause;
        _step = seconds <= 0 ? 1f : (float)(1.0 / (seconds * Rate));
    }

    private volatile bool _exiting;
    private ITuneSource? _endedSource;

    private void EnsureThread()
    {
        var t = _thread;
        if (t != null && _exiting) t.Join(1500); // a fade-out pause is just finishing: let it close
        if (_thread != null) return;
        _stop = false;
        _exiting = false;
        _thread = new Thread(Feed) { IsBackground = true, Name = "SAM jukebox", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Feed()
    {
        using var ev = new AutoResetEvent(false);
        var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = 2, nSamplesPerSec = Rate, wBitsPerSample = 16, nBlockAlign = 4, nAvgBytesPerSec = Rate * 4 };
        if (waveOutOpen(out var hwo, 0xFFFFFFFF, ref fmt, ev.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT) != 0)
        {
            _thread = null;
            return;
        }
        int hdrSize = Marshal.SizeOf<WAVEHDR>();
        var hdrs = new IntPtr[Buffers];
        var datas = new IntPtr[Buffers];
        var pcm = new short[Frames * 2];
        bool ended = false, paused = false;
        try
        {
            for (int i = 0; i < Buffers; i++)
            {
                datas[i] = Marshal.AllocHGlobal(Frames * 4);
                hdrs[i] = Marshal.AllocHGlobal(hdrSize);
                Marshal.StructureToPtr(new WAVEHDR { lpData = datas[i], dwBufferLength = Frames * 4 }, hdrs[i], false);
                waveOutPrepareHeader(hwo, hdrs[i], hdrSize);
                Fill(hwo, hdrs[i], datas[i], pcm, hdrSize, ref ended, ref paused);
            }
            while (!_stop && !paused)
            {
                ev.WaitOne(100);
                for (int i = 0; i < Buffers && !_stop && !paused; i++)
                {
                    var h = Marshal.PtrToStructure<WAVEHDR>(hdrs[i]);
                    if ((h.dwFlags & WHDR_DONE) != 0) Fill(hwo, hdrs[i], datas[i], pcm, hdrSize, ref ended, ref paused);
                }
                if (ended) break;
            }
            if (ended && !_stop) { _exiting = true; Thread.Sleep(Frames * Buffers * 1000 / Rate); } // let the tail play out
        }
        finally
        {
            waveOutReset(hwo);
            for (int i = 0; i < Buffers; i++)
            {
                if (hdrs[i] != IntPtr.Zero) { waveOutUnprepareHeader(hwo, hdrs[i], hdrSize); Marshal.FreeHGlobal(hdrs[i]); }
                if (datas[i] != IntPtr.Zero) Marshal.FreeHGlobal(datas[i]);
            }
            waveOutClose(hwo);
            _thread = null;
        }
        bool fire;
        lock (_gate) fire = ended && !_stop && _endedSource != null && ReferenceEquals(_endedSource, _source);
        if (fire) Ended?.Invoke();
    }

    private void Fill(IntPtr hwo, IntPtr hdr, IntPtr data, short[] pcm, int hdrSize, ref bool ended, ref bool paused)
    {
        int got;
        lock (_gate)
        {
            got = _source == null || ended ? 0 : _source.Render(pcm, Frames);
            if (got < Frames && _source != null && !ended) { ended = true; _endedSource = _source; }
            Array.Clear(pcm, got * 2, (Frames - got) * 2);
            int peak = 0;
            for (int i = 0; i < got * 2; i += 7) peak = Math.Max(peak, Math.Abs((int)pcm[i]));
            LastPeak = peak;
            float vol = Volume;
            for (int f = 0; f < Frames; f++)
            {
                if (_gain < _target) _gain = Math.Min(_target, _gain + _step);
                else if (_gain > _target) _gain = Math.Max(_target, _gain - _step);
                float g = _gain * _gain * vol; // squared ramp sounds smoother
                pcm[f * 2] = (short)(pcm[f * 2] * g);
                pcm[f * 2 + 1] = (short)(pcm[f * 2 + 1] * g);
            }
            Interlocked.Add(ref _framesPlayed, got);
            if (_pauseWhenSilent && _gain <= 0f) { paused = true; _exiting = true; }
        }
        Marshal.Copy(pcm, 0, data, pcm.Length);
        var h = Marshal.PtrToStructure<WAVEHDR>(hdr);
        h.dwFlags &= ~WHDR_DONE;
        Marshal.StructureToPtr(h, hdr, false);
        waveOutWrite(hwo, hdr, hdrSize);
    }
}
