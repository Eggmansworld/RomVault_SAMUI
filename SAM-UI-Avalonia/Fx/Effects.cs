using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace SAM_UI_Avalonia.Fx;

/// <summary>
/// Shared ~30 fps clock for every effect. Runs only while at least one effect is on screen,
/// and the effects stop animating (draw one still frame) when <see cref="Enabled"/> is off.
/// </summary>
public static class FxClock
{
    private static readonly DispatcherTimer Timer = new(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick?.Invoke());
    private static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
    private static int _users;
    private static bool _enabled = true;
    private static bool _suspended;

    public static event Action? Tick;
    /// <summary>Raised when effects are switched on/off so static frames can redraw.</summary>
    public static event Action? EnabledChanged;

    public static double T => Watch.Elapsed.TotalSeconds;

    /// <summary>User setting: animated effects on/off.</summary>
    public static bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Update(); EnabledChanged?.Invoke(); }
    }

    /// <summary>True while the window is minimised: no point drawing frames nobody sees.</summary>
    public static bool Suspended
    {
        get => _suspended;
        set { _suspended = value; Update(); }
    }

    public static void Acquire() { _users++; Update(); }
    public static void Release() { if (--_users < 0) _users = 0; Update(); }

    private static void Update()
    {
        bool run = _users > 0 && _enabled && !_suspended;
        if (run && !Timer.IsEnabled) Timer.Start();
        else if (!run && Timer.IsEnabled) Timer.Stop();
    }
}

public abstract class FxControl : Control
{
    private bool _attached;

    protected FxControl() => IsHitTestVisible = false;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        FxClock.Tick += OnTick;
        FxClock.EnabledChanged += InvalidateVisual;
        Skin.Changed += InvalidateVisual;
        FxClock.Acquire();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (!_attached) return;
        _attached = false;
        FxClock.Tick -= OnTick;
        FxClock.EnabledChanged -= InvalidateVisual;
        Skin.Changed -= InvalidateVisual;
        FxClock.Release();
    }

    private void OnTick()
    {
        if (IsEffectivelyVisible) InvalidateVisual();
    }

    /// <summary>Time to draw with: frozen when effects are off.</summary>
    protected static double Now => FxClock.Enabled ? FxClock.T : 12.3;

    protected static IBrush B(Color c) => new ImmutableSolidColorBrush(c);
}

// =====================================================================================
// Header: bouncing copper bars behind a waving pixel-font logo
// =====================================================================================

public sealed class CopperHeader : FxControl
{
    public static readonly StyledProperty<string> Line1Property = AvaloniaProperty.Register<CopperHeader, string>(nameof(Line1), "SAM-UI");
    public static readonly StyledProperty<string> Line2Property = AvaloniaProperty.Register<CopperHeader, string>(nameof(Line2), "");
    /// <summary>Width kept free on the right (for the jukebox panel).</summary>
    public static readonly StyledProperty<double> ReserveRightProperty = AvaloniaProperty.Register<CopperHeader, double>(nameof(ReserveRight), 0);

    public string Line1 { get => GetValue(Line1Property); set => SetValue(Line1Property, value); }
    public string Line2 { get => GetValue(Line2Property); set => SetValue(Line2Property, value); }
    public double ReserveRight { get => GetValue(ReserveRightProperty); set => SetValue(ReserveRightProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        double t = Now;
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 10 || h < 10) return;

        // copper bars
        for (int i = 0; i < (FxSettings.Copper ? 4 : 0); i++)
        {
            double cy = h / 2 + Math.Sin(t * 1.6 + i * 0.9) * (h * 0.36);
            const int band = 12;
            for (int k = -band; k <= band; k++)
            {
                double br = 1 - Math.Abs(k) / (double)band;
                Color c = Skin.Copper(t * 0.7 + i * 1.3, br * 0.55);
                ctx.FillRectangle(B(Skin.WithAlpha(c, (byte)(100 * br + 25))), new Rect(0, Math.Round(cy + k), w, 1));
            }
        }

        double area = Math.Max(100, w - ReserveRight);
        DrawText(ctx, Line1, t, area, 0.07, 0.56, true, 0.95);
        DrawText(ctx, Line2, t + 0.8, area, 0.72, 0.18, false, 0.9);
    }

    private void DrawText(DrawingContext ctx, string text, double t, double area, double top, double heightFrac, bool wave, double maxWidthFrac)
    {
        if (string.IsNullOrEmpty(text)) return;
        int cols = text.Length * (PixelFont.W + 1) - 1;
        double hgt = Bounds.Height;
        double px = Math.Floor(Math.Min(hgt * heightFrac / PixelFont.H, area * maxWidthFrac / cols));
        if (px < 1) px = 1;
        double x0 = Math.Round(16 + (area - 32 - cols * px) / 2), y0 = Math.Round(hgt * top);
        IBrush shadow = B(Color.FromArgb(170, 0, 0, 0));
        for (int ci = 0; ci < text.Length; ci++)
        {
            bool[,] g = PixelFont.Glyph(text[ci]);
            double dy = wave ? Math.Round(Math.Sin(t * 3 + ci * 0.7) * px * 0.7) : 0;
            for (int y = 0; y < PixelFont.H; y++)
            {
                Color col = Skin.Copper(t * 1.2 + y * 0.35 + ci * 0.05, 1);
                IBrush brush = B(Color.FromArgb(255, (byte)Math.Min(255, col.R + 50), (byte)Math.Min(255, col.G + 50), (byte)Math.Min(255, col.B + 50)));
                for (int x = 0; x < PixelFont.W; x++)
                {
                    if (!g[y, x]) continue;
                    double rx = x0 + (ci * (PixelFont.W + 1) + x) * px, ry = y0 + y * px + dy;
                    double sz = px - (px >= 4 ? 1 : 0);
                    ctx.FillRectangle(shadow, new Rect(rx + Math.Max(1, px * 0.4), ry + Math.Max(1, px * 0.4), sz, sz));
                    ctx.FillRectangle(brush, new Rect(rx, ry, sz, sz));
                }
            }
        }
    }
}

// =====================================================================================
// Sine scroller
// =====================================================================================

public sealed class SineScroller : FxControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<SineScroller, string>(nameof(Text), "");
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private double _offset;
    private double _lastT;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        string text = "          " + Text + "          ";
        if (Text.Length == 0 || h < 8) return;
        double t = FxClock.T;
        if (FxClock.Enabled) _offset += Math.Clamp(t - _lastT, 0, 0.2) * 140;
        _lastT = t;

        double px = Math.Max(2, Math.Floor(h / 12));
        double cw = (PixelFont.W + 1) * px;
        double total = text.Length * cw;
        if (_offset > total) _offset -= total;

        int first = (int)Math.Floor(_offset / cw);
        double startX = -(_offset - first * cw);
        int visible = (int)(w / cw) + 2;
        for (int n = 0; n < visible; n++)
        {
            int ci = (first + n) % text.Length;
            double x = startX + n * cw;
            double y = (h - PixelFont.H * px) / 2 + (FxClock.Enabled ? Math.Sin(t * 3.2 + x * 0.012) * (h * 0.2) : 0);
            bool[,] g = PixelFont.Glyph(text[ci]);
            for (int gy = 0; gy < PixelFont.H; gy++)
            {
                IBrush brush = B(Skin.Copper(t * 2 + x * 0.004 + gy * 0.2, 1));
                for (int gx = 0; gx < PixelFont.W; gx++)
                    if (g[gy, gx])
                        ctx.FillRectangle(brush, new Rect(Math.Round(x + gx * px), Math.Round(y + gy * px), px, px));
            }
        }
    }
}

/// <summary>A thin animated raster bar used as a divider.</summary>
public sealed class CopperRule : FxControl
{
    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        double t = Now;
        const double seg = 6;
        for (double x = 0; x < w; x += seg)
            ctx.FillRectangle(B(Skin.Copper(t * 2.2 - x * 0.006, 0.95)), new Rect(x, 0, seg, h));
    }
}

/// <summary>Little 12-band "VU meter" for the jukebox, driven by the output level.</summary>
public sealed class VuMeter : FxControl
{
    public static Func<int>? LevelSource { get; set; }
    private readonly double[] _bars = new double[12];
    private readonly Random _rng = new();

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        double level = Math.Clamp((LevelSource?.Invoke() ?? 0) / 22000.0, 0, 1);
        double bw = w / _bars.Length;
        for (int i = 0; i < _bars.Length; i++)
        {
            double target = level * (0.45 + 0.55 * _rng.NextDouble());
            _bars[i] = target > _bars[i] ? target : _bars[i] * 0.82;
            double bh = Math.Max(1, _bars[i] * h);
            ctx.FillRectangle(B(Skin.Copper(i * 0.25 + Now, 1)), new Rect(i * bw + 1, h - bh, Math.Max(1, bw - 2), bh));
        }
    }
}

/// <summary>Faint CRT scanlines over the whole window.</summary>
public sealed class Scanlines : Control
{
    public Scanlines() => IsHitTestVisible = false;

    public override void Render(DrawingContext ctx)
    {
        var b = new ImmutableSolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        for (double y = 0; y < Bounds.Height; y += 3) ctx.FillRectangle(b, new Rect(0, y, Bounds.Width, 1));
    }
}
