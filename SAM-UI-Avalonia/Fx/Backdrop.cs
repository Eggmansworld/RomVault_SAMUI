using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SAM_UI_Avalonia.Core;

namespace SAM_UI_Avalonia.Fx;

/// <summary>Which background layers are on (mirrors the SETUP window / settings file).</summary>
public static class FxSettings
{
    public static bool Starfield { get; set; } = true;
    public static bool Objects { get; set; } = true;
    public static bool Balls { get; set; } = true;
    public static bool Floor { get; set; } = true;
    public static bool Copper { get; set; } = true;

    public const int MinBalls = 1, MaxBalls = 128;
    private static int _ballCount = 24;
    public static int BallCount
    {
        get => _ballCount;
        set => _ballCount = Math.Clamp(value, MinBalls, MaxBalls);
    }

    public static void Load(SamConfig c)
    {
        Starfield = c.ShowStarfield;
        Objects = c.ShowObjects;
        Balls = c.ShowBalls;
        Floor = c.ShowFloor;
        Copper = c.ShowCopper;
        BallCount = c.BallCount;
    }
}

/// <summary>
/// Backdrop of the output list: parallax starfield, a scrolling wireframe floor that fades in
/// and out over a minute, a 3D vector object floating around the screen, and a bouncing
/// formation of shaded vector balls that morphs between shapes. Every layer can be switched
/// off in SETUP; + / - on the keyboard changes the number of balls.
/// </summary>
public sealed class VectorBackdrop : FxControl
{
    private readonly record struct V3(double X, double Y, double Z);

    private sealed record Shape(string Name, V3[] Verts, (int A, int B)[] Edges, bool Dots, int[][]? Faces = null);

    /// <summary>"AUTO" cycles through every object; otherwise the name of one object.</summary>
    public static string Mode { get; set; } = "AUTO";

    public static readonly string[] ShapeNames;
    private static readonly Shape[] Shapes;
    private const double CycleSeconds = 16;
    private const double FloorCycleSeconds = 60;

    private readonly (double X, double Y, int Layer)[] _stars;

    static VectorBackdrop()
    {
        Shapes = new[]
        {
            Glenz(),
            MakeAuto("OCTAHEDRON", new[] { new V3(1, 0, 0), new V3(-1, 0, 0), new V3(0, 1, 0), new V3(0, -1, 0), new V3(0, 0, 1), new V3(0, 0, -1) }),
            new Shape("TESSERACT", Array.Empty<V3>(), Array.Empty<(int, int)>(), false), // 4D: built every frame
            MakeAuto("ICOSAHEDRON", Icosa()),
            Helix(),
            MakeAuto("DODECAHEDRON", Dodeca()),
            StarTetra(),
            Torus(),
            MakeAuto("CUBE", Cube()),
            DotSphere(),
        };
        ShapeNames = new[] { "AUTO" }.Concat(Shapes.Select(s => s.Name)).Concat(new[] { "NONE" }).ToArray();
    }

    public VectorBackdrop()
    {
        var rng = new Random(1911);
        _stars = Enumerable.Range(0, 180).Select(_ => (rng.NextDouble(), rng.NextDouble(), rng.Next(3))).ToArray();
        Skin.Changed += () => _ballBrushes = null;
    }

    // =================================================================== shape builders

    private static V3[] Cube()
    {
        var v = new List<V3>();
        for (int i = 0; i < 8; i++) v.Add(new V3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        return v.ToArray();
    }

    /// <summary>Translucent "glenz" cube: filled faces you can see through, plus edges.</summary>
    private static Shape Glenz()
    {
        Shape c = MakeAuto("GLENZ CUBE", Cube());
        // vertex index bits: 1 = +X, 2 = +Y, 4 = +Z; faces wound consistently (outward normals)
        int[][] faces =
        {
            new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, // -Z, +Z
            new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, // -Y, +Y
            new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 }, // -X, +X
        };
        return c with { Faces = faces };
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

    private static V3[] Dodeca()
    {
        double p = (1 + Math.Sqrt(5)) / 2, q = 1 / p;
        var v = new List<V3>();
        foreach (int a in new[] { -1, 1 })
            foreach (int b in new[] { -1, 1 })
            {
                foreach (int c in new[] { -1, 1 }) v.Add(new V3(a, b, c));
                v.Add(new V3(0, a * q, b * p));
                v.Add(new V3(a * q, b * p, 0));
                v.Add(new V3(a * p, 0, b * q));
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

    /// <summary>Double helix with rungs.</summary>
    private static Shape Helix()
    {
        const int n = 26;
        var v = new List<V3>();
        var e = new List<(int, int)>();
        for (int i = 0; i < n; i++)
        {
            double a = i * Math.PI * 4 / n, y = i * 2.0 / (n - 1) - 1;
            v.Add(new V3(0.55 * Math.Cos(a), y * 1.3, 0.55 * Math.Sin(a)));
            v.Add(new V3(-0.55 * Math.Cos(a), y * 1.3, -0.55 * Math.Sin(a)));
            int s1 = i * 2, s2 = i * 2 + 1;
            if (i > 0) { e.Add((s1 - 2, s1)); e.Add((s2 - 2, s2)); }
            if (i % 2 == 0) e.Add((s1, s2));
        }
        return new Shape("DNA HELIX", Normalise(v.ToArray()), e.ToArray(), false);
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

    /// <summary>4D hypercube rotated in the XW and ZW planes, projected 4D -> 3D.</summary>
    private static (V3[] Verts, (int, int)[] Edges) Tesseract(double t)
    {
        var v = new V3[16];
        double a = t * 0.7, b = t * 0.45;
        double ca = Math.Cos(a), sa = Math.Sin(a), cb = Math.Cos(b), sb = Math.Sin(b);
        for (int i = 0; i < 16; i++)
        {
            double x = (i & 1) == 0 ? -1 : 1, y = (i & 2) == 0 ? -1 : 1, z = (i & 4) == 0 ? -1 : 1, w = (i & 8) == 0 ? -1 : 1;
            double x1 = x * ca - w * sa, w1 = x * sa + w * ca;   // XW
            double z1 = z * cb - w1 * sb, w2 = z * sb + w1 * cb; // ZW
            double f = 1.6 / (2.8 - w2);
            v[i] = new V3(x1 * f, y * f, z1 * f);
        }
        var e = new List<(int, int)>();
        for (int i = 0; i < 16; i++)
            for (int k = 0; k < 4; k++)
            {
                int j = i ^ (1 << k);
                if (j > i) e.Add((i, j));
            }
        return (v, e.ToArray());
    }

    private static V3[] Normalise(V3[] v)
    {
        double max = v.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z));
        return v.Select(p => new V3(p.X / max, p.Y / max, p.Z / max)).ToArray();
    }

    private static double Dist(V3 a, V3 b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>Rotate (X, Y, Z order) and perspective-project. Near: 0 = back, 1 = front.</summary>
    private static (Point P, double Near, double Z) Project(V3 v, double ax, double ay, double az, double cx, double cy, double radius)
    {
        double sx = Math.Sin(ax), cxr = Math.Cos(ax), sy = Math.Sin(ay), cyr = Math.Cos(ay), sz = Math.Sin(az), czr = Math.Cos(az);
        double y1 = v.Y * cxr - v.Z * sx, z1 = v.Y * sx + v.Z * cxr;
        double x2 = v.X * cyr + z1 * sy, z2 = -v.X * sy + z1 * cyr;
        double x3 = x2 * czr - y1 * sz, y3 = x2 * sz + y1 * czr;
        double f = 3.0 / (z2 + 3.4);
        return (new Point(cx + x3 * radius * f, cy + y3 * radius * f), Math.Clamp((1 - z2) / 2, 0, 1), z2);
    }

    // =================================================================== drawing

    public override void Render(DrawingContext ctx)
    {
        Scheme s = Skin.Current;
        double w = Bounds.Width, h = Bounds.Height;
        ctx.FillRectangle(B(s.Bg), new Rect(Bounds.Size));
        if (w < 20 || h < 20) return;
        double t = Now;

        if (FxSettings.Starfield) DrawStars(ctx, s, t, w, h);
        double floorAlpha = FxSettings.Floor ? 0.5 - 0.5 * Math.Cos(t * Math.PI * 2 / FloorCycleSeconds) : 0;
        if (floorAlpha > 0.01) DrawFloor(ctx, s, t, w, h, floorAlpha);
        if (FxSettings.Objects && Mode != "NONE") DrawObject(ctx, t, w, h);
        if (FxSettings.Balls) DrawBalls(ctx, t, w, h, floorAlpha);
    }

    private void DrawStars(DrawingContext ctx, Scheme s, double t, double w, double h)
    {
        foreach (var (x, y, layer) in _stars)
        {
            double speed = (layer + 1) * 0.010;
            double px = ((x - t * speed) % 1 + 1) % 1 * w;
            double size = layer + 1;
            byte a = (byte)(45 + layer * 45);
            ctx.FillRectangle(B(Skin.WithAlpha(s.Star, a)), new Rect(Math.Round(px), Math.Round(y * h), size, size));
        }
    }

    // ---- scrolling wireframe floor

    private static double HorizonY(double h) => h * 0.52;

    private static void DrawFloor(DrawingContext ctx, Scheme s, double t, double w, double h, double alpha)
    {
        double yH = HorizonY(h), depth = h - yH, cx = w / 2;
        const double far = 18;
        Color col = s.Accent2;

        // lines across, scrolling towards the viewer
        double scroll = t * 1.6 % 1;
        for (int k = 0; k < (int)far; k++)
        {
            double d = k + 1 - scroll;
            if (d < 1) continue;
            double y = yH + depth / d;
            double a = alpha * Math.Pow(1 / d, 0.6);
            ctx.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(Skin.WithAlpha(col, (byte)(150 * a))), 1), new Point(0, y), new Point(w, y));
        }

        // lines running to the vanishing point
        double spacing = w / 9;
        for (int i = -14; i <= 14; i++)
        {
            var near = new Point(cx + i * spacing, h);
            var farP = new Point(cx + i * spacing / far, yH + depth / far);
            ctx.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(Skin.WithAlpha(col, (byte)(90 * alpha))), 1), near, farP);
        }

        // glowing horizon
        ctx.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(Skin.WithAlpha(s.Accent, (byte)(120 * alpha))), 2),
            new Point(0, yH + depth / far), new Point(w, yH + depth / far));
    }

    // ---- floating 3D object

    private static void DrawObject(DrawingContext ctx, double t, double w, double h)
    {
        Shape shape;
        double scale = 1;
        if (Mode == "AUTO")
        {
            shape = Shapes[(int)(t / CycleSeconds) % Shapes.Length];
            double ph = t % CycleSeconds;
            double env = Math.Min(Math.Min(ph, CycleSeconds - ph), 1.2) / 1.2; // zoom in / out between objects
            scale = env * env * (3 - 2 * env);
        }
        else shape = Shapes.FirstOrDefault(x => x.Name == Mode) ?? Shapes[0];

        // drift around the whole screen on a slow Lissajous path
        double cx = w * (0.5 + 0.32 * Math.Sin(t * 0.11)), cy = h * (0.48 + 0.24 * Math.Sin(t * 0.17 + 0.7));
        double radius = Math.Min(w * 0.22, h * 0.36) * scale;
        if (radius < 2) return;
        double ax = t * 0.53, ay = t * 0.71, az = t * 0.29;

        V3[] verts = shape.Verts;
        (int A, int B)[] edges = shape.Edges;
        if (shape.Name == "TESSERACT") (verts, edges) = Tesseract(t);

        var pts = new (Point P, double Near, double Z)[verts.Length];
        for (int i = 0; i < verts.Length; i++) pts[i] = Project(verts[i], ax, ay, az, cx, cy, radius);

        if (shape.Dots)
        {
            for (int i = 0; i < pts.Length; i++)
            {
                var (p, near, _) = pts[i];
                Color c = Skin.Copper(t * 0.8 + i * 0.03, 0.6 + 0.4 * near);
                double d = 1.5 + near * 3;
                ctx.FillRectangle(B(Skin.WithAlpha(c, (byte)(40 + 150 * near))), new Rect(p.X - d / 2, p.Y - d / 2, d, d));
            }
            return;
        }

        if (shape.Faces != null)
        {
            // glenz: back faces first (dim), then front faces (brighter), all see-through
            foreach (bool front in new[] { false, true })
                for (int f = 0; f < shape.Faces.Length; f++)
                {
                    int[] face = shape.Faces[f];
                    Point p0 = pts[face[0]].P, p1 = pts[face[1]].P, p2 = pts[face[2]].P;
                    double cross = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
                    if ((cross < 0) != front) continue;
                    var geo = new StreamGeometry();
                    using (StreamGeometryContext g = geo.Open())
                    {
                        g.BeginFigure(p0, true);
                        for (int k = 1; k < face.Length; k++) g.LineTo(pts[face[k]].P);
                        g.EndFigure(true);
                    }
                    Color c = Skin.Copper(t * 0.6 + f * 0.9, front ? 0.95 : 0.5);
                    ctx.DrawGeometry(B(Skin.WithAlpha(c, (byte)(front ? 70 : 35))), null, geo);
                }
        }

        for (int i = 0; i < edges.Length; i++)
        {
            var (a, b) = edges[i];
            double near = (pts[a].Near + pts[b].Near) / 2;
            Color c = Skin.Copper(t * 0.8 + i * 0.04, 0.55 + 0.45 * near);
            ctx.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(Skin.WithAlpha(c, (byte)(18 + 40 * near))), 5), pts[a].P, pts[b].P);
            ctx.DrawLine(new ImmutablePen(new ImmutableSolidColorBrush(Skin.WithAlpha(c, (byte)(50 + 150 * near))), 1.4), pts[a].P, pts[b].P);
        }
    }

    // ---- vector balls

    private const double FormationSeconds = 8, MorphSeconds = 1.6;
    private const int FormationCount = 6;
    private IBrush[]? _ballBrushes;

    private IBrush[] BallBrushes()
    {
        if (_ballBrushes != null) return _ballBrushes;
        var list = new IBrush[12];
        for (int i = 0; i < list.Length; i++)
        {
            Color c = Skin.Copper(i * 0.55, 1);
            Color dark = Color.FromRgb((byte)(c.R / 5), (byte)(c.G / 5), (byte)(c.B / 5));
            list[i] = new RadialGradientBrush
            {
                Center = new RelativePoint(0.38, 0.35, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.32, 0.28, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Colors.White, 0),
                    new GradientStop(c, 0.32),
                    new GradientStop(dark, 1),
                },
            };
        }
        return _ballBrushes = list;
    }

    /// <summary>Position of ball i of n in formation f (roughly within a unit sphere).</summary>
    private static V3 Formation(int f, int i, int n, double t)
    {
        double u = n <= 1 ? 0 : i / (double)(n - 1), a = i * Math.PI * 2 / n;
        switch (f)
        {
            case 0: // sphere
            {
                double y = 1 - (i + 0.5) * 2 / n, r = Math.Sqrt(Math.Max(0, 1 - y * y)), g = i * Math.PI * (3 - Math.Sqrt(5));
                return new V3(Math.Cos(g) * r, y, Math.Sin(g) * r);
            }
            case 1: // wobbling ring
                return new V3(Math.Cos(a), 0.3 * Math.Sin(a * 3 + t * 3), Math.Sin(a));
            case 2: // cube lattice
            {
                int side = (int)Math.Ceiling(Math.Pow(n, 1 / 3.0));
                int x = i % side, y = i / side % side, z = i / (side * side);
                double k = side <= 1 ? 0 : 1.6 / (side - 1);
                return new V3(x * k - 0.8, y * k - 0.8, z * k - 0.8);
            }
            case 3: // helix
            {
                double h = u * Math.PI * 4;
                return new V3(0.7 * Math.Cos(h), u * 2 - 1, 0.7 * Math.Sin(h));
            }
            case 4: // rippling sheet
            {
                int side = (int)Math.Ceiling(Math.Sqrt(n));
                double x = (i % side) / (double)Math.Max(1, side - 1) * 2 - 1, z = (i / side) / (double)Math.Max(1, side - 1) * 2 - 1;
                return new V3(x, 0.35 * Math.Sin(x * 3 + t * 2.5) * Math.Cos(z * 2), z);
            }
            default: // two crossed rings
            {
                double b = (i / 2) * Math.PI * 4 / n;
                return i % 2 == 0 ? new V3(Math.Cos(b), Math.Sin(b), 0) : new V3(0, Math.Sin(b), Math.Cos(b));
            }
        }
    }

    private void DrawBalls(DrawingContext ctx, double t, double w, double h, double floorAlpha)
    {
        int n = FxSettings.BallCount;
        IBrush[] brushes = BallBrushes();

        // morph between formations
        int step = (int)(t / FormationSeconds);
        int fA = step % FormationCount, fB = (step + 1) % FormationCount;
        double ph = t % FormationSeconds, m = 0;
        if (ph > FormationSeconds - MorphSeconds)
        {
            m = (ph - (FormationSeconds - MorphSeconds)) / MorphSeconds;
            m = m * m * (3 - 2 * m);
        }

        // the whole group drifts sideways and bounces on the floor
        double floorY = h * 0.86;
        double radius = Math.Min(w, h) * 0.17;
        double cx = w * (0.5 + 0.34 * Math.Sin(t * 0.09 + 2.4));
        double cy = floorY - radius * 1.25 - Math.Abs(Math.Sin(t * 1.8)) * h * 0.16;
        double ax = 0.35 + 0.2 * Math.Sin(t * 0.3), ay = t * 0.8, az = 0.1 * Math.Sin(t * 0.5);
        double ballR = radius * 0.13 * Math.Clamp(Math.Sqrt(24.0 / n), 0.45, 1.8);

        var balls = new (Point P, double Z, double F, int I)[n];
        for (int i = 0; i < n; i++)
        {
            V3 a = Formation(fA, i, n, t), b = Formation(fB, i, n, t);
            var v = new V3(a.X + (b.X - a.X) * m, a.Y + (b.Y - a.Y) * m, a.Z + (b.Z - a.Z) * m);
            var (p, near, z) = Project(v, ax, ay, az, cx, cy, radius);
            balls[i] = (p, z, 3.0 / (z + 3.4), i);
        }

        // shadows on the floor while it's visible
        if (floorAlpha > 0.05)
        {
            IBrush shadow = B(Color.FromArgb((byte)(60 * floorAlpha), 0, 0, 0));
            double lift = (floorY - cy) / (h * 0.5);
            foreach (var b in balls)
            {
                double r = ballR * b.F * (1.1 - lift * 0.4);
                ctx.DrawEllipse(shadow, null, new Point(b.P.X, floorY + b.Z * radius * 0.08), r * 1.2, r * 0.35);
            }
        }

        // far to near
        Array.Sort(balls, (x, y) => y.Z.CompareTo(x.Z));
        foreach (var b in balls)
            ctx.DrawEllipse(brushes[b.I % brushes.Length], null, b.P, ballR * b.F, ballR * b.F);
    }
}
