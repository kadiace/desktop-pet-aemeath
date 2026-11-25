using System.IO;
using System.Windows.Media;

namespace AemeathCompanion.UI;

/// <summary>
/// Common pixel-art color palette extracted directly from Aemeath's 14 GIF sprites
/// (Sakura blush, Berry rose, Digital Ghost cyan, Lavender shadow, and Deep Plum outline).
/// </summary>
public static class PixelPalette
{
    public static readonly Color OutlineDark = Color.FromRgb(0x24, 0x19, 0x28);
    public static readonly Color BasePlum = Color.FromRgb(0x23, 0x18, 0x29);
    public static readonly Color PanelPlum = Color.FromRgb(0x31, 0x21, 0x38);
    public static readonly Color TrackPlum = Color.FromRgb(0x3E, 0x2A, 0x45);
    public static readonly Color PlumBorder = Color.FromRgb(0x68, 0x48, 0x58);
    public static readonly Color BerryRose = Color.FromRgb(0xA0, 0x40, 0x68);
    public static readonly Color PinkMid = Color.FromRgb(0xE8, 0x88, 0xA8);
    public static readonly Color SakuraPink = Color.FromRgb(0xF0, 0xA0, 0xB8);
    public static readonly Color BlushLight = Color.FromRgb(0xFF, 0xD0, 0xE0);
    public static readonly Color BlushGlow = Color.FromRgb(0xFF, 0xE0, 0xE0);
    public static readonly Color CreamWhite = Color.FromRgb(0xFF, 0xF4, 0xF6);
    public static readonly Color Lavender = Color.FromRgb(0xB0, 0xB0, 0xD8);
    public static readonly Color GhostCyan = Color.FromRgb(0x88, 0xE8, 0xE8);
    public static readonly Color CyanGlint = Color.FromRgb(0xD0, 0xFF, 0xFF);

    public static readonly SolidColorBrush OutlineDarkBrush = Freeze(new SolidColorBrush(OutlineDark));
    public static readonly SolidColorBrush BasePlumBrush = Freeze(new SolidColorBrush(BasePlum));
    public static readonly SolidColorBrush PanelPlumBrush = Freeze(new SolidColorBrush(PanelPlum));
    public static readonly SolidColorBrush TrackPlumBrush = Freeze(new SolidColorBrush(TrackPlum));
    public static readonly SolidColorBrush PlumBorderBrush = Freeze(new SolidColorBrush(PlumBorder));
    public static readonly SolidColorBrush BerryRoseBrush = Freeze(new SolidColorBrush(BerryRose));
    public static readonly SolidColorBrush PinkMidBrush = Freeze(new SolidColorBrush(PinkMid));
    public static readonly SolidColorBrush SakuraPinkBrush = Freeze(new SolidColorBrush(SakuraPink));
    public static readonly SolidColorBrush BlushLightBrush = Freeze(new SolidColorBrush(BlushLight));
    public static readonly SolidColorBrush BlushGlowBrush = Freeze(new SolidColorBrush(BlushGlow));
    public static readonly SolidColorBrush CreamWhiteBrush = Freeze(new SolidColorBrush(CreamWhite));
    public static readonly SolidColorBrush LavenderBrush = Freeze(new SolidColorBrush(Lavender));
    public static readonly SolidColorBrush GhostCyanBrush = Freeze(new SolidColorBrush(GhostCyan));
    public static readonly SolidColorBrush CyanGlintBrush = Freeze(new SolidColorBrush(CyanGlint));
    public static readonly SolidColorBrush ScanlineBrush = Freeze(new SolidColorBrush(Color.FromArgb(26, 0x12, 0x0A, 0x16)));

    public static FontFamily PretendardFamily { get; } = LoadPretendardFont();

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static FontFamily LoadPretendardFont()
    {
        try
        {
            string fontDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
            if (Directory.Exists(fontDir))
            {
                var uri = new Uri(fontDir + Path.DirectorySeparatorChar, UriKind.Absolute);
                return new FontFamily(uri, "./#Pretendard");
            }
        }
        catch { }

        return new FontFamily("Segoe UI");
    }

    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
