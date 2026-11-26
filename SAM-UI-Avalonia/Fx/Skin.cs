using Avalonia;
using Avalonia.Media;

namespace SAM_UI_Avalonia.Fx;

public sealed record Scheme(
    string Name,
    Color Bg, Color Panel, Color Border, Color Text, Color Dim,
    Color Accent, Color Accent2, Color Good, Color Warn, Color Bad,
    double CopperHue, double CopperSpread, Color Star);

/// <summary>
/// Warez-style colour schemes. Applying one pushes brushes into the Application resources,
/// so every {DynamicResource Sam*} binding updates live.
/// </summary>
public static class Skin
{
    public static readonly Scheme[] All =
    {
        new("KEYGEN VIOLET",
            Color.Parse("#07040F"), Color.Parse("#E0110826"), Color.Parse("#B13CFF"), Color.Parse("#E8E4FF"), Color.Parse("#8A80B0"),
            Color.Parse("#FF4FD8"), Color.Parse("#3FE0FF"), Color.Parse("#5CFF8A"), Color.Parse("#FFD84A"), Color.Parse("#FF4A5A"),
            290, 140, Color.Parse("#CFC8FF")),
        new("PHOSPHOR TERMiNAL",
            Color.Parse("#010A02"), Color.Parse("#E0041506"), Color.Parse("#2BCB4B"), Color.Parse("#C9FFD2"), Color.Parse("#5E9A68"),
            Color.Parse("#4BFF6A"), Color.Parse("#E5FF4B"), Color.Parse("#4BFF6A"), Color.Parse("#E5FF4B"), Color.Parse("#FF5A3A"),
            110, 50, Color.Parse("#B8FFC4")),
        new("AMBER BBS",
            Color.Parse("#0C0700"), Color.Parse("#E0160E02"), Color.Parse("#C77A00"), Color.Parse("#FFCF7A"), Color.Parse("#A07A3A"),
            Color.Parse("#FFB000"), Color.Parse("#FFE29A"), Color.Parse("#FFE29A"), Color.Parse("#FF8A00"), Color.Parse("#FF4A2A"),
            35, 25, Color.Parse("#FFD58A")),
        new("iCE CRACKTRO",
            Color.Parse("#010A10"), Color.Parse("#E0051824"), Color.Parse("#1FB8D8"), Color.Parse("#DDF8FF"), Color.Parse("#6F9DAD"),
            Color.Parse("#3FE0FF"), Color.Parse("#B4FF3F"), Color.Parse("#5CFF8A"), Color.Parse("#FFD84A"), Color.Parse("#FF5A5A"),
            195, 70, Color.Parse("#BFF4FF")),
        new("BLOOD COPPER",
            Color.Parse("#0E0306"), Color.Parse("#E01C0710"), Color.Parse("#FF3A3A"), Color.Parse("#FFE6DA"), Color.Parse("#B07A80"),
            Color.Parse("#FF6A3A"), Color.Parse("#FFC23A"), Color.Parse("#7CFF9A"), Color.Parse("#FFC23A"), Color.Parse("#FF3A5A"),
            355, 50, Color.Parse("#FFD0C0")),
    };

    public static Scheme Current { get; private set; } = All[0];
    public static event Action? Changed;

    public static void Apply(string name)
    {
        Current = All.FirstOrDefault(s => s.Name == name) ?? All[0];
        Scheme c = Current;
        var r = Application.Current!.Resources;
        // Fluent's own accent (check boxes, sliders, combo highlights) follows the theme too.
        r["SystemAccentColor"] = c.Accent;
        r["SystemAccentColorLight1"] = c.Accent;
        r["SystemAccentColorLight2"] = c.Accent2;
        r["SystemAccentColorLight3"] = c.Accent2;
        r["SystemAccentColorDark1"] = c.Accent;
        r["SystemAccentColorDark2"] = c.Border;
        r["SystemAccentColorDark3"] = c.Border;
        r["CheckBoxCheckBackgroundFillChecked"] = new SolidColorBrush(c.Accent);
        r["CheckBoxCheckBackgroundFillCheckedPointerOver"] = new SolidColorBrush(c.Accent2);
        r["CheckBoxCheckBackgroundFillCheckedPressed"] = new SolidColorBrush(c.Accent2);
        r["CheckBoxCheckBackgroundStrokeChecked"] = new SolidColorBrush(c.Accent);
        r["CheckBoxCheckGlyphForegroundChecked"] = new SolidColorBrush(c.Bg);
        r["CheckBoxCheckGlyphForegroundCheckedPointerOver"] = new SolidColorBrush(c.Bg);
        r["SliderThumbBackground"] = new SolidColorBrush(c.Accent);
        r["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(c.Accent2);
        r["SliderThumbBackgroundPressed"] = new SolidColorBrush(c.Accent2);
        r["SliderTrackValueFill"] = new SolidColorBrush(c.Accent);
        r["SliderTrackValueFillPointerOver"] = new SolidColorBrush(c.Accent2);
        r["SliderTrackValueFillPressed"] = new SolidColorBrush(c.Accent2);
        r["SliderTrackFill"] = new SolidColorBrush(WithAlpha(c.Dim, 120));
        r["SliderTrackFillPointerOver"] = new SolidColorBrush(WithAlpha(c.Dim, 160));

        r["SamBg"] = new SolidColorBrush(c.Bg);
        r["SamPanel"] = new SolidColorBrush(c.Panel);
        r["SamBorder"] = new SolidColorBrush(c.Border);
        r["SamText"] = new SolidColorBrush(c.Text);
        r["SamDim"] = new SolidColorBrush(c.Dim);
        r["SamAccent"] = new SolidColorBrush(c.Accent);
        r["SamAccent2"] = new SolidColorBrush(c.Accent2);
        r["SamGood"] = new SolidColorBrush(c.Good);
        r["SamWarn"] = new SolidColorBrush(c.Warn);
        r["SamBad"] = new SolidColorBrush(c.Bad);
        r["SamHover"] = new SolidColorBrush(WithAlpha(c.Accent, 60));
        r["SamSelect"] = new SolidColorBrush(WithAlpha(c.Accent2, 120));
        r["SamRowBg"] = new SolidColorBrush(WithAlpha(c.Bg, 150));
        r["SamHeaderBg"] = new SolidColorBrush(WithAlpha(c.Panel, 235));
        r["SamProgressFill"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(WithAlpha(c.Accent, 40), 0),
                new GradientStop(WithAlpha(c.Accent, 120), 0.85),
                new GradientStop(WithAlpha(c.Accent2, 200), 1),
            },
        };
        r["SamAnsiBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(c.Accent2, 0),
                new GradientStop(c.Accent, 0.55),
                new GradientStop(WithAlpha(c.Border, 255), 1),
            },
        };
        Changed?.Invoke();
    }

    /// <summary>Copper-bar colour: hue sweeps around the scheme's base hue.</summary>
    public static Color Copper(double t, double brightness = 1)
    {
        Scheme s = Current;
        double h = s.CopperHue + Math.Sin(t) * s.CopperSpread * 0.5;
        return Hsv(h, 0.85, Math.Clamp(brightness, 0, 1));
    }

    public static Color Hsv(double h, double s, double v, byte a = 255)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromArgb(a, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
