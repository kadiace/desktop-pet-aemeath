using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace AemeathCompanion.UI;

/// <summary>
/// System tray icon generated from Aemeath's sprite with quick access to mode, scale, and exit.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _autoStartMenuItem;
    private readonly Icon? _customIcon;
    private bool _disposed;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    public event Action? OpenMenuRequested;
    public event Action? SwitchModeARequested;
    public event Action? SwitchModeBRequested;
    public event Action? SwitchMusicModeRequested;
    public event Action? ResetPositionRequested;
    public event Action? ResetScaleRequested;
    public event Action? ToggleTopmostRequested;
    public event Action? ToggleAutoStartRequested;
    public event Action? ExitRequested;

    public TrayIconService(string spriteDirectory)
    {
        _customIcon = CreateIconFromSprite(Path.Combine(spriteDirectory, "screen1.gif"));

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open Control Menu", null, (_, _) => OpenMenuRequested?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Roam", null, (_, _) => SwitchModeARequested?.Invoke());
        menu.Items.Add("Platform", null, (_, _) => SwitchModeBRequested?.Invoke());
        menu.Items.Add("Music", null, (_, _) => SwitchMusicModeRequested?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Center on Primary Monitor", null, (_, _) => ResetPositionRequested?.Invoke());
        menu.Items.Add("Reset Scale (1.00x)", null, (_, _) => ResetScaleRequested?.Invoke());
        menu.Items.Add("Toggle Always On Top", null, (_, _) => ToggleTopmostRequested?.Invoke());
        _autoStartMenuItem = new WinForms.ToolStripMenuItem("PC 시작 시 자동 켜기", null, (_, _) => ToggleAutoStartRequested?.Invoke())
        {
            Checked = Native.AutoStartService.IsEnabled()
        };
        menu.Items.Add(_autoStartMenuItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit Aemeath", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "Aemeath, the Digital Ghost",
            Icon = _customIcon ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenMenuRequested?.Invoke();
    }

    public void SetAutoStartChecked(bool enabled)
    {
        if (!_disposed) _autoStartMenuItem.Checked = enabled;
    }

    private static Icon? CreateIconFromSprite(string gifPath)
    {
        try
        {
            if (!File.Exists(gifPath)) return null;
            using var img = Image.FromFile(gifPath);
            using var srcBmp = new Bitmap(img);

            // Auto-crop transparent margins so screen1 fills the 16x16/32x32 system tray icon crisply
            int minX = srcBmp.Width, minY = srcBmp.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < srcBmp.Height; y++)
            {
                for (int x = 0; x < srcBmp.Width; x++)
                {
                    if (srcBmp.GetPixel(x, y).A > 16)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            Rectangle srcRect = maxX >= minX && maxY >= minY
                ? Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1)
                : new Rectangle(0, 0, srcBmp.Width, srcBmp.Height);

            using var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.Clear(Color.Transparent);

                float scale = Math.Min(30f / Math.Max(1, srcRect.Width), 30f / Math.Max(1, srcRect.Height));
                int drawW = Math.Max(1, (int)Math.Round(srcRect.Width * scale));
                int drawH = Math.Max(1, (int)Math.Round(srcRect.Height * scale));
                int drawX = (32 - drawW) / 2;
                int drawY = (32 - drawH) / 2;
                g.DrawImage(srcBmp, new Rectangle(drawX, drawY, drawW, drawH), srcRect, GraphicsUnit.Pixel);
            }
            nint hIcon = bmp.GetHicon();
            try
            {
                return (Icon)Icon.FromHandle(hIcon).Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _customIcon?.Dispose();
    }
}
