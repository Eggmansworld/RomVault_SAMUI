using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SAM_UI_Avalonia.Fx;

/// <summary>
/// "D" easter egg: a little flying devil (original pixel art, in the spirit of the classic
/// Amiga intros) that swoops left and right across the output window for 30-45 seconds.
/// </summary>
public sealed class DevilEgg
{
    private const int W = 26, H = 20;
    private static readonly char[][,] Frames = BuildFrames();
    private static readonly Dictionary<char, IBrush> Pal = new()
    {
        ['R'] = new ImmutableSolidColorBrush(Color.FromRgb(214, 24, 34)),   // body
        ['r'] = new ImmutableSolidColorBrush(Color.FromRgb(140, 10, 20)),   // shade
        ['O'] = new ImmutableSolidColorBrush(Color.FromRgb(255, 140, 40)),  // belly
        ['H'] = new ImmutableSolidColorBrush(Color.FromRgb(246, 232, 196)), // horns
        ['Y'] = new ImmutableSolidColorBrush(Color.FromRgb(255, 224, 0)),   // eyes
        ['K'] = new ImmutableSolidColorBrush(Color.FromRgb(24, 0, 6)),      // black
        ['W'] = new ImmutableSolidColorBrush(Colors.White),                 // teeth
        ['w'] = new ImmutableSolidColorBrush(Color.FromRgb(96, 12, 60)),    // wing membrane
        ['v'] = new ImmutableSolidColorBrush(Color.FromRgb(150, 30, 90)),   // wing bones
    };
    private static readonly IBrush Shadow = new ImmutableSolidColorBrush(Color.FromArgb(110, 0, 0, 0));

    private bool _active;
    private double _start, _until, _seed;
    private int _side;

    public bool Active => _active;

    /// <summary>This visit ends by escaping onto the desktop (only when the user pressed D).</summary>
    private bool _escape;

    /// <summary>Set when an escaping devil reaches its exit: where it is, so a desktop window can take over.</summary>
    public (double X, double Y, double Px, bool FaceLeft)? Handoff { get; set; }

    /// <summary>"D" again while it's here: leave now (and escape).</summary>
    public void LeaveNow(double t, bool escape)
    {
        _escape |= escape;
        _until = Math.Min(_until, t + 2.0);
    }

    public void Start(double t, bool escape = false)
    {
        _escape = escape;
        Handoff = null;
        _active = true;
        _start = t;
        _until = t + 30 + Random.Shared.NextDouble() * 15; // 30-45 s
        _seed = Random.Shared.NextDouble() * 100;
        _side = Random.Shared.Next(2) == 0 ? -1 : 1;
    }

    /// <summary>Draws the devil; returns false once it has flown away.</summary>
    public bool Draw(DrawingContext ctx, double t, double w, double h)
    {
        if (!_active) return false;
        double px = Math.Clamp(h / 70, 4, 11);
        double sw = W * px, sh = H * px;
        double margin = sw * 0.6;

        // fast left-right swoops (about 3 s per sweep), slower up and down, plus a flappy bob
        double e = t - _start;
        double sweep = Math.Sin(e * 1.9 + _seed) * 0.85 + Math.Sin(e * 0.47 + _seed * 2) * 0.15;
        double x = w / 2 + sweep * (w / 2 - margin);
        double y = h * (0.42 + 0.26 * Math.Sin(e * 0.63 + _seed)) + Math.Sin(e * 9) * px * 1.5;
        double vx = (Math.Cos(e * 1.9 + _seed) * 1.9 * 0.85 + Math.Cos(e * 0.47 + _seed * 2) * 0.47 * 0.15) * (w / 2 - margin);

        // fly in from one side, and out again at the end
        double inT = Math.Clamp(e / 2.0, 0, 1), outT = Math.Clamp((t - (_until - 2.0)) / 2.0, 0, 1);
        double off = _side < 0 ? -sw : w + sw;
        if (inT < 1) { x = off + (x - off) * Smooth(inT); vx = -_side; }
        if (_escape && t >= _until - 2.0)
        {
            Handoff = (x, y, px, vx < 0);
            _active = false;
            return false;
        }
        if (outT > 0)
        {
            double exit = vx >= 0 ? w + sw : -sw;
            x += (exit - x) * Smooth(outT);
        }
        if (t >= _until) { _active = false; return false; }

        DrawSprite(ctx, x - sw / 2, y - sh / 2, px, (int)(e * 12), vx < 0);
        return true;
    }

    /// <summary>Paints the sprite with its top-left at (x0, y0). <paramref name="tick"/> drives the wing flap.</summary>
    public static void DrawSprite(DrawingContext ctx, double x0, double y0, double px, int tick, bool faceLeft)
    {
        int frame = tick % 4;
        char[,] g = Frames[frame == 3 ? 1 : frame];
        for (int pass = 0; pass < 2; pass++)
            for (int row = 0; row < H; row++)
                for (int col = 0; col < W; col++)
                {
                    char c = g[row, faceLeft ? W - 1 - col : col];
                    if (c == '.') continue;
                    double rx = Math.Round(x0 + col * px), ry = Math.Round(y0 + row * px);
                    if (pass == 0) ctx.FillRectangle(Shadow, new Rect(rx + px * 0.6, ry + px * 0.6, px, px));
                    else ctx.FillRectangle(Pal[c], new Rect(rx, ry, px, px));
                }
    }

    public const int SpriteW = W, SpriteH = H;

    private static double Smooth(double v) => v * v * (3 - 2 * v);

    // ------------------------------------------------------------------ the sprite

    private static char[][,] BuildFrames()
    {
        // body (10 wide), drawn at columns 8-17; faces right (the sprite is mirrored to face left)
        string[] body =
        {
            ".H......H.", ".HH....HH.", "..HRRRRH..", ".RRRRRRRR.", ".RRYYRRYYR",
            ".RRYKRRYKR", ".RRRRRRRRr", ".RKWKWKWKr", "..RKKKKRr.", "...RRRRr..",
            "..RRRRRRr.", ".RRRRRRRRr", ".RROOOORRr", ".RROOOORRr", "..RRRRRRr.",
            "..RR..RR..", "..RR..RR..", ".KK...KK..", "..........", "..........",
        };
        // left wing, columns 0-7 (the right wing is its mirror); three flap positions
        string[][] wings =
        {
            new[] { // up
                "........", "........", "v.......", "wv......", "wwv.....", ".wwv....", ".wwwv...",
                "..wwwv..", "..wwwwv.", "...wwwwv", "....wwwv", ".......v", "........", "........",
                "........", "........", "........", "........", "........", "........",
            },
            new[] { // level
                "........", "........", "........", "........", "........", "........", "........",
                "........", "vvvv....", "wwwwvvv.", ".wwwwwwv", "..ww.wwv", "........", "........",
                "........", "........", "........", "........", "........", "........",
            },
            new[] { // down
                "........", "........", "........", "........", "........", "........", "........",
                "........", "........", "........", "....wwwv", "..wwwwv.", ".wwwwv..", "wwwv....",
                "wwv.....", "wv......", "........", "........", "........", "........",
            },
        };
        // arrow-tipped tail, curling down and back (left)
        (int Row, int Col)[] tail = { (16, 8), (16, 9), (17, 6), (17, 7), (18, 4), (18, 5), (17, 3), (18, 2), (18, 3), (19, 3) };

        var frames = new char[3][,];
        for (int f = 0; f < 3; f++)
        {
            var g = new char[H, W];
            for (int r = 0; r < H; r++)
                for (int c = 0; c < W; c++)
                    g[r, c] = '.';
            for (int r = 0; r < H; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char wc = wings[f][r][c];
                    if (wc == '.') continue;
                    g[r, c] = wc;               // left wing
                    g[r, W - 1 - c] = wc;       // right wing (mirrored)
                }
                for (int c = 0; c < 10; c++)
                    if (body[r][c] != '.') g[r, 8 + c] = body[r][c];
            }
            foreach (var (row, col) in tail) if (g[row, col] == '.') g[row, col] = 'R';
            frames[f] = g;
        }
        return frames;
    }
}
