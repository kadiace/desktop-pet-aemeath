using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AemeathCompanion.Engine;

public sealed record SpriteFrame(BitmapSource Bitmap, double DelaySeconds);

public sealed class SpriteAnimation
{
    public string Name { get; }
    public IReadOnlyList<SpriteFrame> Frames { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public SpriteAnimation(string name, List<SpriteFrame> frames, int width, int height)
    {
        Name = name;
        Frames = frames;
        PixelWidth = width;
        PixelHeight = height;
    }
}

/// <summary>
/// Loads and composites all 14 Aemeath GIF sprites with exact original frame delays
/// and NearestNeighbor pixel preservation.
/// </summary>
public sealed class GifSpriteBank
{
    public static readonly string[] AllNames =
    [
        "aemeath", "drag",
        "idle1", "idle2", "idle3", "idle4", "move",
        "screen1", "screen2", "screen3", "screen4", "screen5", "screen6", "screen7", "screen8"
    ];

    private const int PropertyTagFrameDelay = 0x5100;
    private readonly Dictionary<string, SpriteAnimation> _sprites = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, SpriteAnimation> Sprites => _sprites;

    public void LoadFromDirectory(string spriteDirectory)
    {
        foreach (string name in AllNames)
        {
            string path = Path.Combine(spriteDirectory, name + ".gif");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Missing required sprite GIF: {path}", path);

            // Preserve native resolution up to 400px (keeps 300x250 screen8 lossless while downscaling 1000x1000 aemeath)
            _sprites[name] = DecodeGif(name, path, maxDim: 400);
        }
    }

    public SpriteAnimation Get(string name)
        => _sprites.TryGetValue(name, out var anim) ? anim : _sprites["idle1"];

    private static SpriteAnimation DecodeGif(string name, string filePath, int maxDim)
    {
        using var img = Image.FromFile(filePath);
        var dim = new FrameDimension(img.FrameDimensionsList[0]);
        int frameCount = Math.Max(1, img.GetFrameCount(dim));

        byte[]? delayBytes = null;
        try
        {
            if (Array.IndexOf(img.PropertyIdList, PropertyTagFrameDelay) >= 0)
                delayBytes = img.GetPropertyItem(PropertyTagFrameDelay)?.Value;
        }
        catch { }

        int targetW = img.Width;
        int targetH = img.Height;
        if (targetW > maxDim || targetH > maxDim)
        {
            double ratio = Math.Min(maxDim / (double)targetW, maxDim / (double)targetH);
            targetW = Math.Max(1, (int)Math.Round(targetW * ratio));
            targetH = Math.Max(1, (int)Math.Round(targetH * ratio));
        }

        var frames = new List<SpriteFrame>(frameCount);

        for (int i = 0; i < frameCount; i++)
        {
            img.SelectActiveFrame(dim, i);

            double delaySec = 0.1;
            if (delayBytes is not null && (i * 4 + 3) < delayBytes.Length)
            {
                int centiseconds = BitConverter.ToInt32(delayBytes, i * 4);
                delaySec = centiseconds > 0 ? centiseconds / 100.0 : 0.1;
            }
            if (delaySec < 0.02) delaySec = 0.04;

            using var bmp = new Bitmap(targetW, targetH, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.Clear(System.Drawing.Color.Transparent);
                g.DrawImage(img, 0, 0, targetW, targetH);
            }

            var wpfBmp = ConvertToFrozenBitmapSource(bmp);
            frames.Add(new SpriteFrame(wpfBmp, delaySec));
        }

        return new SpriteAnimation(name, frames, targetW, targetH);
    }

    private static BitmapSource ConvertToFrozenBitmapSource(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            var wb = BitmapSource.Create(
                bmp.Width,
                bmp.Height,
                96,
                96,
                PixelFormats.Pbgra32,
                null,
                data.Scan0,
                data.Stride * bmp.Height,
                data.Stride);
            wb.Freeze();
            return wb;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
