using System.Windows;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace WmplWrap.Desktop;

internal enum DesktopTheme { Light, Dark }

/// <summary>
/// Updates the shared brush instances used by the WPF views, so an appearance change is live
/// and does not require parallel light and dark layouts.
/// </summary>
internal static class ThemeManager
{
    public const int DefaultAccentHue = 207;

    private static DesktopTheme _currentTheme = DesktopTheme.Light;

    public static int NormalizeHue(int value) => ((value % 360) + 360) % 360;

    public static Brush AccentBrush => System.Windows.Application.Current?.TryFindResource("Brush.Accent") as Brush
        ?? Brushes.DodgerBlue;

    public static string GraphLabelColorHex => _currentTheme == DesktopTheme.Dark ? "#ACC0D1" : "#567085";

    public static string GraphRuleColorHex => _currentTheme == DesktopTheme.Dark ? "#3B5368" : "#DCE7F3";

    public static void Apply(DesktopTheme theme, int accentHue)
    {
        _currentTheme = theme;
        var palette = theme == DesktopTheme.Dark ? Dark : Light;
        var hue = NormalizeHue(accentHue);
        var accent = FromHsl(hue, theme == DesktopTheme.Dark ? 72 : 76, theme == DesktopTheme.Dark ? 58 : 40);
        var accentPressed = FromHsl(hue, theme == DesktopTheme.Dark ? 68 : 78, theme == DesktopTheme.Dark ? 46 : 31);
        var accentLight = FromHsl(hue, theme == DesktopTheme.Dark ? 48 : 86, theme == DesktopTheme.Dark ? 21 : 92);
        var rail = FromHsl(hue, theme == DesktopTheme.Dark ? 63 : 52, theme == DesktopTheme.Dark ? 14 : 29);
        var railDeep = FromHsl(hue, theme == DesktopTheme.Dark ? 66 : 61, theme == DesktopTheme.Dark ? 10 : 23);
        var railHover = FromHsl(hue, theme == DesktopTheme.Dark ? 57 : 48, theme == DesktopTheme.Dark ? 23 : 35);

        SetBrush("Brush.AppBackground", palette.AppBackground);
        SetBrush("Brush.Surface", palette.Surface);
        SetBrush("Brush.SurfaceMuted", palette.SurfaceMuted);
        SetBrush("Brush.Rail", rail);
        SetBrush("Brush.RailDeep", railDeep);
        SetBrush("Brush.RailText", palette.RailText);
        SetBrush("Brush.RailHover", railHover);
        SetBrush("Brush.Ink", palette.Ink);
        SetBrush("Brush.MutedInk", palette.MutedInk);
        SetBrush("Brush.Rule", palette.Rule);
        SetBrush("Brush.Warning", palette.Warning);
        SetBrush("Brush.Success", palette.Success);
        SetBrush("Brush.Error", palette.Error);
        SetBrush("Brush.Accent", accent);
        SetBrush("Brush.AccentPressed", accentPressed);
        SetBrush("Brush.AccentLight", accentLight);
        SetBrush("Brush.AccentText", IsDark(accent) ? Colors.White : Color.FromRgb(19, 31, 42));
    }

    private static void SetBrush(string key, Color color)
    {
        var resources = System.Windows.Application.Current?.Resources;
        if (resources is not null)
            resources[key] = new SolidColorBrush(color);
    }

    private static bool IsDark(Color color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255d;
            return normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B) < 0.36;
    }

    private static Color FromHsl(int hue, double saturation, double lightness)
    {
        var h = NormalizeHue(hue) / 360d;
        var s = Math.Clamp(saturation / 100d, 0, 1);
        var l = Math.Clamp(lightness / 100d, 0, 1);
        if (s == 0)
        {
            var gray = (byte)Math.Round(l * 255);
            return Color.FromRgb(gray, gray, gray);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return Color.FromRgb(ToByte(HueToRgb(p, q, h + 1d / 3d)), ToByte(HueToRgb(p, q, h)), ToByte(HueToRgb(p, q, h - 1d / 3d)));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1d / 6d) return p + (q - p) * 6 * t;
        if (t < 1d / 2d) return q;
        if (t < 2d / 3d) return p + (q - p) * (2d / 3d - t) * 6;
        return p;
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

    private sealed record ThemePalette(
        Color AppBackground,
        Color Surface,
        Color SurfaceMuted,
        Color Rail,
        Color RailDeep,
        Color RailText,
        Color RailHover,
        Color Ink,
        Color MutedInk,
        Color Rule,
        Color Warning,
        Color Success,
        Color Error);

    private static readonly ThemePalette Light = new(
        Color.FromRgb(231, 238, 247), Color.FromRgb(248, 251, 255), Color.FromRgb(220, 231, 243),
        Color.FromRgb(35, 74, 113), Color.FromRgb(23, 58, 96), Color.FromRgb(184, 214, 237), Color.FromRgb(45, 91, 132),
        Color.FromRgb(24, 44, 62), Color.FromRgb(86, 112, 133), Color.FromRgb(183, 201, 220),
        Color.FromRgb(147, 91, 22), Color.FromRgb(35, 117, 72), Color.FromRgb(155, 44, 44));

    private static readonly ThemePalette Dark = new(
        Color.FromRgb(15, 24, 33), Color.FromRgb(23, 35, 47), Color.FromRgb(32, 49, 65),
        Color.FromRgb(13, 37, 57), Color.FromRgb(9, 28, 44), Color.FromRgb(211, 230, 244), Color.FromRgb(25, 61, 91),
        Color.FromRgb(239, 246, 252), Color.FromRgb(172, 192, 209), Color.FromRgb(59, 83, 104),
        Color.FromRgb(229, 161, 76), Color.FromRgb(77, 183, 122), Color.FromRgb(241, 118, 118));
}
