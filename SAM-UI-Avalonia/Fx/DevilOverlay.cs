using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SAM_UI_Avalonia.Fx;

/// <summary>
/// The devil's escape (only when the user pressed D): a see-through, click-through, always-on-top
/// window carries the sprite out of SAM-UI, flies it around the screen SAM-UI is on for
/// <see cref="RoamSeconds"/>, then it leaves through the nearest of the left, right or top edges.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DevilOverlay : Window
{
    private const double RoamSeconds = 10, BlendSeconds = 1.2, ExitSeconds = 1.4;

    private readonly SpriteView _view;
    private readonly DispatcherTimer _timer;
    private readonly double _scale, _wPx, _hPx;
    private readonly PixelPoint _startPx;
    private readonly double _seed = Random.Shared.NextDouble() * 100;
    private PixelRect _area;            // the screen we roam (work area, so we stay above the taskbar)
    private double _x, _y, _lastX;
    private double _elapsed;
    private long _lastTicks;
    private (double X, double Y)? _exitFrom, _exitTo;

    private static DevilOverlay? _current;

    /// <param name="topLeft">Sprite's top-left on screen (pixels).</param>
    /// <param name="px">Size of one sprite pixel (DIPs).</param>
    public static void Launch(PixelPoint topLeft, double px, double scale, bool faceLeft)
    {
        if (_current != null) return;
        try
        {
            _current = new DevilOverlay(topLeft, px, scale, faceLeft);
            _current.Show();
        }
        catch
        {
            _current = null;
        }
    }

    private DevilOverlay(PixelPoint topLeft, double px, double scale, bool faceLeft)
    {
        _scale = scale;
        double wDip = (DevilEgg.SpriteW + 1) * px, hDip = (DevilEgg.SpriteH + 1) * px; // +1 for the shadow
        _wPx = wDip * scale;
        _hPx = hDip * scale;
        _startPx = topLeft;
        _x = _lastX = topLeft.X;
        _y = topLeft.Y;

        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = wDip;
        Height = hDip;
        Position = topLeft;
        Title = "DEViL!";
        Content = _view = new SpriteView { Px = px, FaceLeft = faceLeft, IsHitTestVisible = false };

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => Step());
        Opened += (_, _) =>
        {
            MakeClickThrough();
            Screen? s = Screens.ScreenFromPoint(new PixelPoint((int)(_x + _wPx / 2), (int)(_y + _hPx / 2))) ?? Screens.Primary ?? Screens.All[0];
            _area = s.WorkingArea;
            _lastTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _timer.Start();
        };
        Closed += (_, _) => { _timer.Stop(); _current = null; };
    }

    private void Step()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double dt = Math.Clamp((now - _lastTicks) / (double)System.Diagnostics.Stopwatch.Frequency, 0, 0.05);
        _lastTicks = now;
        _elapsed += dt;

        if (_elapsed < RoamSeconds)
        {
            // the same swooping flight as inside the window, but across the whole screen
            double e = _elapsed;
            double ax = Math.Max(0, (_area.Width - _wPx) / 2), ay = Math.Max(0, (_area.Height - _hPx) / 2);
            double cx = _area.X + _area.Width / 2.0, cy = _area.Y + _area.Height / 2.0;
            double sweep = Math.Sin(e * 1.9 + _seed) * 0.85 + Math.Sin(e * 0.47 + _seed * 2) * 0.15;
            double px = cx + sweep * ax - _wPx / 2;
            double py = cy + Math.Sin(e * 0.9 + _seed) * ay * 0.85 - _hPx / 2 + Math.Sin(e * 9) * 6 * _scale;
            // ease out of the window into the flight path
            double b = Math.Clamp(e / BlendSeconds, 0, 1);
            b = b * b * (3 - 2 * b);
            _x = _startPx.X + (px - _startPx.X) * b;
            _y = _startPx.Y + (py - _startPx.Y) * b;
        }
        else
        {
            if (_exitTo == null)
            {
                // leave through the nearest of left / right / top - never the bottom
                double left = _x - _area.X, right = _area.Right - (_x + _wPx), top = _y - _area.Y;
                double m = Math.Min(left, Math.Min(right, top));
                _exitFrom = (_x, _y);
                _exitTo = m == top ? (_x, _area.Y - _hPx - 20)
                        : m == left ? (_area.X - _wPx - 20, _y)
                        : (_area.Right + 20, _y);
            }
            double k = Math.Clamp((_elapsed - RoamSeconds) / ExitSeconds, 0, 1);
            double ease = k * k; // accelerate away
            _x = _exitFrom!.Value.X + (_exitTo.Value.X - _exitFrom.Value.X) * ease;
            _y = _exitFrom.Value.Y + (_exitTo.Value.Y - _exitFrom.Value.Y) * ease;
            if (k >= 1) { Close(); return; }
        }

        if (Math.Abs(_x - _lastX) > 0.5) _view.FaceLeft = _x < _lastX;
        _lastX = _x;
        _view.Tick = (int)(_elapsed * 12);
        Position = new PixelPoint((int)_x, (int)_y);
        _view.InvalidateVisual();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    private void MakeClickThrough()
    {
        try
        {
            IntPtr h = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (h == IntPtr.Zero) return;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        }
        catch { /* not fatal */ }
    }

    private sealed class SpriteView : Control
    {
        public double Px { get; set; }
        public bool FaceLeft { get; set; }
        public int Tick { get; set; }

        public override void Render(DrawingContext ctx) => DevilEgg.DrawSprite(ctx, 0, 0, Px, Tick, FaceLeft);
    }
}
