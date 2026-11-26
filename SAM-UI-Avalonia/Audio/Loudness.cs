using SAM_UI_Avalonia.Audio.Sid;

namespace SAM_UI_Avalonia.Audio;

/// <summary>
/// Loudness normalisation for the jukebox. Tracker modules carry their own master / mixing
/// volume, so some play far louder than others. Before a tune starts, its first
/// <see cref="AnalyseSeconds"/> are rendered silently in the background and measured (RMS and
/// peak); playback then gets a gain that brings it to <see cref="TargetRms"/>, capped so the
/// measured peaks never clip. Results are cached per tune for the session.
/// </summary>
public static class Loudness
{
    public const double AnalyseSeconds = 45;
    /// <summary>About -20 dBFS RMS.</summary>
    public const double TargetRms = 3300;
    private const double MinGain = 0.2, MaxGain = 3.0;

    private static readonly Dictionary<string, double> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ITuneSource Open(string path, int sub) =>
        Path.GetExtension(path).Equals(".sid", StringComparison.OrdinalIgnoreCase)
            ? new SidSource(path, sub)
            : new ModSource(path);

    /// <summary>Gain for a tune (measures it the first time). Call off the UI thread.</summary>
    public static double GainFor(string path, int sub)
    {
        string key = path + "#" + sub;
        lock (Cache)
            if (Cache.TryGetValue(key, out double g)) return g;

        var (rms, peak) = Measure(path, sub);
        double gain = rms < 1 ? 1 : TargetRms / rms;
        if (peak > 0) gain = Math.Min(gain, 32000.0 / peak); // never push the loudest bit into clipping
        gain = Math.Clamp(gain, MinGain, MaxGain);
        lock (Cache) Cache[key] = gain;
        return gain;
    }

    public static (double Rms, int Peak) Measure(string path, int sub)
    {
        using ITuneSource src = Open(path, sub);
        const int frames = 4096;
        short[] buf = new short[frames * 2];
        long total = (long)(StreamOut.Rate * AnalyseSeconds), done = 0;
        double sum = 0;
        long n = 0;
        int peak = 0;
        while (done < total)
        {
            int got = src.Render(buf, frames);
            if (got <= 0) break;
            for (int i = 0; i < got * 2; i++)
            {
                int v = buf[i];
                sum += (double)v * v;
                if (v < 0) v = -v;
                if (v > peak) peak = v;
            }
            n += got * 2;
            done += got;
            if (got < frames) break; // song ended
        }
        return (n > 0 ? Math.Sqrt(sum / n) : 0, peak);
    }
}

/// <summary>Plays another source with a fixed gain (and hard limits, just in case).</summary>
public sealed class NormalizedSource : ITuneSource
{
    private readonly ITuneSource _inner;
    private readonly float _gain;

    public NormalizedSource(ITuneSource inner, double gain)
    {
        _inner = inner;
        _gain = (float)gain;
    }

    public string Title => _inner.Title;
    public string Format => _inner.Format;
    public double Duration => _inner.Duration;
    public ITuneSource Inner => _inner;

    public int Render(short[] buffer, int frames)
    {
        int got = _inner.Render(buffer, frames);
        if (Math.Abs(_gain - 1f) > 0.01f)
            for (int i = 0; i < got * 2; i++)
                buffer[i] = (short)Math.Clamp(buffer[i] * _gain, -32767f, 32767f);
        return got;
    }

    public void Dispose() => _inner.Dispose();
}
