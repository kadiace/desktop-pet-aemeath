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
    public event Action? ExitRequested;

    public TrayIconService(string spriteDirectory)
    {
        _customIcon = CreateIconFromSprite(Path.Combine(spriteDirectory, "idle1.gif"));

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
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit Aemeath", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "Aemeath, Digital Ghost",
            Icon = _customIcon ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenMenuRequested?.Invoke();
    }

    private static Icon? CreateIconFromSprite(string gifPath)
    {
        try
        {
            if (!File.Exists(gifPath)) return null;
            using var img = Image.FromFile(gifPath);
            using var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.Clear(Color.Transparent);
                g.DrawImage(img, 0, 0, 32, 32);
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
