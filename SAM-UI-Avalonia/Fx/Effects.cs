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
// Backdrop of the output window: parallax starfield + rotating 3D vector objects
// =====================================================================================

public sealed class VectorBackdrop : FxControl
{
    private readonly record struct V3(double X, double Y, double Z);

    private sealed record Shape(string Name, V3[] Verts, (int A, int B)[] Edges, bool Dots);

    /// <summary>"AUTO" cycles through every object; otherwise the name of one object.</summary>
    public static string Mode { get; set; } = "AUTO";

    public static readonly string[] ShapeNames;

    private static readonly Shape[] Shapes;
    private const double CycleSeconds = 18;

    private readonly (double X, double Y, int Layer)[] _stars;

    static VectorBackdrop()
    {
        Shapes = new[]
        {
            MakeAuto("CUBE", Cube()),
            MakeAuto("OCTAHEDRON", new[] { new V3(1, 0, 0), new V3(-1, 0, 0), new V3(0, 1, 0), new V3(0, -1, 0), new V3(0, 0, 1), new V3(0, 0, -1) }),
            MakeAuto("ICOSAHEDRON", Icosa()),
            StarTetra(),
            Torus(),
            DotSphere(),
        };
        ShapeNames = new[] { "AUTO" }.Concat(Shapes.Select(s => s.Name)).Concat(new[] { "NONE" }).ToArray();
    }

    public VectorBackdrop()
    {
        var rng = new Random(1911);
        _stars = Enumerable.Range(0, 180).Select(_ => (rng.NextDouble(), rng.NextDouble(), rng.Next(3))).ToArray();
    }

    // ---- shape builders (all scaled to radius 1)

    private static V3[] Cube()
    {
        var v = new List<V3>();
        for (int i = 0; i < 8; i++) v.Add(new V3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        return v.ToArray();
    }

    private static V3[] Icosa()
    {
        double p = (1 + Math.Sqrt(5)) / 2;
        var v = new List<V3>();
        foreach (int a in new[] { -1, 1 })
            foreach (int b in new[] { -1, 1 })
            {
                v.Add(new V3(0, a, b * p));
                v.Add(new V3(a, b * p, 0));
                v.Add(new V3(b * p, 0, a));
            }
        return v.ToArray();
    }

    /// <summary>Normalises the vertices and joins every pair at the shortest distance.</summary>
    private static Shape MakeAuto(string name, V3[] verts)
    {
        verts = Normalise(verts);
        double min = double.MaxValue;
        for (int i = 0; i < verts.Length; i++)
            for (int j = i + 1; j < verts.Length; j++)
                min = Math.Min(min, Dist(verts[i], verts[j]));
        var edges = new List<(int, int)>();
        for (int i = 0; i < verts.Length; i++)
            for (int j = i + 1; j < verts.Length; j++)
                if (Dist(verts[i], verts[j]) < min * 1.01) edges.Add((i, j));
        return new Shape(name, verts, edges.ToArray(), false);
    }

    private static Shape StarTetra()
    {
        var v = new[]
        {
            new V3(1, 1, 1), new V3(1, -1, -1), new V3(-1, 1, -1), new V3(-1, -1, 1),
            new V3(-1, -1, -1), new V3(-1, 1, 1), new V3(1, -1, 1), new V3(1, 1, -1),
        };
        var e = new List<(int, int)>();
        for (int k = 0; k < 2; k++)
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    e.Add((k * 4 + i, k * 4 + j));
        return new Shape("STAR", Normalise(v), e.ToArray(), false);
    }

    private static Shape Torus()
    {
        const int major = 18, minor = 8;
        var v = new List<V3>();
        var e = new List<(int, int)>();
        for (int i = 0; i < major; i++)
        {
            double a = i * Math.PI * 2 / major;
            for (int j = 0; j < minor; j++)
            {
                double b = j * Math.PI * 2 / minor;
                double r = 1 + 0.42 * Math.Cos(b);
                v.Add(new V3(r * Math.Cos(a), 0.42 * Math.Sin(b), r * Math.Sin(a)));
                int idx = i * minor + j;
                e.Add((idx, i * minor + (j + 1) % minor));
                e.Add((idx, ((i + 1) % major) * minor + j));
            }
        }
        return new Shape("TORUS", Normalise(v.ToArray()), e.ToArray(), false);
    }

    private static Shape DotSphere()
    {
        const int n = 220;
        var v = new V3[n];
        double g = Math.PI * (3 - Math.Sqrt(5));
        for (int i = 0; i < n; i++)
        {
            double y = 1 - (i + 0.5) * 2 / n, r = Math.Sqrt(1 - y * y), a = g * i;
            v[i] = new V3(Math.Cos(a) * r, y, Math.Sin(a) * r);
        }
        return new Shape("DOTBALL", v, Array.Empty<(int, int)>(), true);
    }

    private static V3[] Normalise(V3[] v)
    {
        double max = v.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z));
        return v.Select(p => new V3(p.X / max, p.Y / max, p.Z / max)).ToArray();
    }

    private static double Dist(V3 a, V3 b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    // ---- drawing

    public override void Render(DrawingContext ctx)
    {
        Scheme s = Skin.Current;
        double w = Bounds.Width, h = Bounds.Height;
        ctx.FillRectangle(B(s.Bg), new Rect(Bounds.Size));
        if (w < 20 || h < 20) return;
        double t = Now;

        // starfield (scrolls right to left, three speeds)
        foreach (var (x, y, layer) in _stars)
        {
            double speed = (layer + 1) * 0.010;
            double px = ((x - t * speed) % 1 + 1) % 1 * w;
            double size = layer + 1;
            byte a = (byte)(45 + layer * 45);
            ctx.FillRectangle(B(Skin.WithAlpha(s.Star, a)), new Rect(Math.Round(px), Math.Round(y * h), size, size));
        }

        if (Mode == "NONE") return;

        Shape shape;
        double scale = 1;
        if (Mode == "AUTO")
        {
            int idx = (int)(t / CycleSeconds) % Shapes.Length;
            shape = Shapes[idx];
            double ph = t % CycleSeconds;
            double env = Math.Min(Math.Min(ph, CycleSeconds - ph), 1.2) / 1.2; // zoom in / out between objects
            scale = env * env * (3 - 2 * env);
        }
        else shape = Shapes.FirstOrDefault(x => x.Name == Mode) ?? Shapes[0];

        double cx = w * 0.70, cy = h * 0.52;
        double radius = Math.Min(w * 0.26, h * 0.40) * scale;
        if (radius < 2) return;

        double ax = t * 0.53, ay = t * 0.71, az = t * 0.29;
        double sx = Math.Sin(ax), cxr = Math.Cos(ax), sy = Math.Sin(ay), cyr = Math.Cos(ay), sz = Math.Sin(az), czr = Math.Cos(az);

        var pts = new (Point P, double Near)[shape.Verts.Length];
        for (int i = 0; i < shape.Verts.Length; i++)
        {
            V3 v = shape.Verts[i];
            // rotate X, Y, Z
            double y1 = v.Y * cxr - v.Z * sx, z1 = v.Y * sx + v.Z * cxr;
            double x2 = v.X * cyr + z1 * sy, z2 = -v.X * sy + z1 * cyr;
            double x3 = x2 * czr - y1 * sz, y3 = x2 * sz + y1 * czr;
            double f = 3.0 / (z2 + 3.4);
            pts[i] = (new Point(cx + x3 * radius * f, cy + y3 * radius * f), (1 - z2) / 2); // Near: 0 back .. 1 front
        }

        if (shape.Dots)
        {
            for (int i = 0; i < pts.Length; i++)
            {
                var (p, near) = pts[i];
                Color c = Skin.Copper(t * 0.8 + i * 0.03, 0.6 + 0.4 * near);
                double d = 1.5 + near * 3;
                ctx.FillRectangle(B(Skin.WithAlpha(c, (byte)(40 + 150 * near))), new Rect(p.X - d / 2, p.Y - d / 2, d, d));
            }
            return;
        }

        for (int i = 0; i < shape.Edges.Length; i++)
        {
            var (a, b) = shape.Edges[i];
            double near = (pts[a].Near + pts[b].Near) / 2;
            Color c = Skin.Copper(t * 0.8 + i * 0.04, 0.55 + 0.45 * near);
            // soft glow pass + crisp line
            ctx.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(Skin.WithAlpha(c, (byte)(18 + 40 * near))), 5), pts[a].P, pts[b].P);
            ctx.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(Skin.WithAlpha(c, (byte)(50 + 150 * near))), 1.4), pts[a].P, pts[b].P);
        }
    }
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
        for (int i = 0; i < 4; i++)
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
