using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SAM_UI_Avalonia.Fx;

/// <summary>
/// The Boing ball's escape: when it leaves the SAM-UI window it's handed to this borderless,
/// see-through, always-on-top, click-through window, which carries on with the same speed, spin
/// and bounce, bouncing on the bottom of the screen's work area (just above the taskbar), until it
/// has rolled off the edge of the desktop.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BoingOverlay : Window
{
    private readonly BallView _ball;
    private readonly DispatcherTimer _timer;
    private readonly double _scale, _sizePx, _speedPx, _g, _dir;
    private double _x, _y, _vy, _spin;
    private long _lastTicks;
    private PixelRect _desktop;

    private static BoingOverlay? _current;

    /// <param name="topLeft">Ball's top-left on screen (pixels).</param>
    /// <param name="r">Radius (DIPs).</param>
    /// <param name="speed">Horizontal speed (DIP/s), <paramref name="dir"/> = +1 right / -1 left.</param>
    /// <param name="vy">Vertical speed (DIP/s, down = +).</param>
    /// <param name="g">Gravity (DIP/s²).</param>
    public static void Launch(PixelPoint topLeft, double r, double scale, double dir, double speed, double spin, double vy, double g)
    {
        if (_current != null) return;
        try
        {
            _current = new BoingOverlay(topLeft, r, scale, dir, speed, spin, vy, g);
            _current.Show();
        }
        catch
        {
            _current = null; // no fancy exit then - not worth crashing for
        }
    }

    private BoingOverlay(PixelPoint topLeft, double r, double scale, double dir, double speed, double spin, double vy, double g)
    {
        _scale = scale;
        _sizePx = 2 * r * scale;
        _speedPx = speed * scale;
        _g = g * scale;
        _dir = dir;
        _x = topLeft.X;
        _y = topLeft.Y;
        _vy = vy * scale;
        _spin = spin;

        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = Height = 2 * r;
        Position = topLeft;
        Title = "BOING!";
        Content = _ball = new BallView { Radius = r, Spin = spin, IsHitTestVisible = false };

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => Step());
        Opened += (_, _) =>
        {
            MakeClickThrough();
            _desktop = DesktopBounds();
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

        _x += _dir * _speedPx * dt;
        _vy += _g * dt;
        _y += _vy * dt;

        // bounce on the floor of whichever screen the ball is over (work area = above the taskbar)
        int floor = FloorAt(_x + _sizePx / 2);
        if (_y + _sizePx > floor)
        {
            _y = floor - _sizePx;
            _vy = -Math.Abs(_vy);
        }

        _spin += _dir * dt * 2.4;
        _ball.Spin = _spin;
        Position = new PixelPoint((int)_x, (int)_y);
        _ball.InvalidateVisual();

        // gone off the edge of the desktop?
        if (_x > _desktop.Right + 4 || _x + _sizePx < _desktop.X - 4)
            Close();
    }

    private int FloorAt(double xPx)
    {
        foreach (Screen s in Screens.All)
            if (xPx >= s.Bounds.X && xPx < s.Bounds.Right)
                return s.WorkingArea.Bottom;
        return (Screens.Primary ?? Screens.All[0]).WorkingArea.Bottom;
    }

    private PixelRect DesktopBounds()
    {
        PixelRect r = default;
        bool first = true;
        foreach (Screen s in Screens.All)
        {
            r = first ? s.Bounds : r.Union(s.Bounds);
            first = false;
        }
        return r;
    }

    // ---- clicks go straight through to whatever is under the ball

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

    private sealed class BallView : Control
    {
        public double Radius { get; set; }
        public double Spin { get; set; }

        public override void Render(DrawingContext ctx) =>
            VectorBackdrop.DrawBoingBall(ctx, new Point(Radius, Radius), Radius, Spin);
    }
}
