using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AemeathCompanion.Media;
using AemeathCompanion.Native;

namespace AemeathCompanion.UI;

/// <summary>
/// Topmost, non-activating floating host window for Aemeath's pixel-art speech bubble.
/// Uses selective WM_NCHITTEST so ONLY the Prev / Play-Pause / Next pixel buttons receive clicks
/// (without stealing focus via WM_MOUSEACTIVATE=MA_NOACTIVATE), while the rest of the bubble
/// remains 100% click-through to underlying windows.
/// </summary>
public sealed class MusicBubbleWindow : Window
{
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int WM_NCHITTEST = 0x0084;
    private const int MA_NOACTIVATE = 3;
    private const int HTTRANSPARENT = -1;
    private const int HTCLIENT = 1;

    private readonly MusicBubbleControl _bubble;
    private nint _hwnd;

    public event Action? PreviousClicked;
    public event Action? PlayPauseClicked;
    public event Action? NextClicked;

    public MusicBubbleWindow()
    {
        Title = "Aemeath Music Bubble";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = MusicBubbleControl.BubbleWidth;
        Height = MusicBubbleControl.TotalHeight;

        _bubble = new MusicBubbleControl();
        _bubble.PreviousClicked += () => PreviousClicked?.Invoke();
        _bubble.PlayPauseClicked += () => PlayPauseClicked?.Invoke();
        _bubble.NextClicked += () => NextClicked?.Invoke();
        Content = _bubble;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Not globally WS_EX_TRANSPARENT: selective click-through is handled in WndProc WM_NCHITTEST
            Win32Interop.ConfigureOverlayWindow(_hwnd, clickThrough: false, noActivate: true);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        };
    }

    public nint Hwnd => _hwnd;

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new nint(MA_NOACTIVATE);
        }

        if (msg == WM_NCHITTEST)
        {
            int screenX = unchecked((short)((long)lParam & 0xFFFF));
            int screenY = unchecked((short)(((long)lParam >> 16) & 0xFFFF));

            if (Win32Interop.GetWindowRect(hwnd, out var rect))
            {
                double dpi = Win32Interop.GetWindowDpiScale(hwnd);
                var localDip = new Point((screenX - rect.Left) / dpi, (screenY - rect.Top) / dpi);
                if (_bubble.HitTestTransportButton(localDip) != MediaTransportButton.None)
                {
                    handled = true;
                    return new nint(HTCLIENT);
                }
            }

            handled = true;
            return new nint(HTTRANSPARENT);
        }

        return 0;
    }

    public void ApplySnapshot(MediaSnapshot snapshot) => _bubble.ApplySnapshot(snapshot);

    public void UpdateLayoutAndRender(
        bool visible,
        double companionCenterX,
        double companionCenterY,
        double companionHalfPx,
        IReadOnlyList<MonitorBounds> monitors,
        double dt,
        WasapiLoopbackAnalyzer audio,
        bool enforceTopmost)
    {
        if (!visible)
        {
            if (IsVisible) Hide();
            return;
        }

        if (!IsVisible)
        {
            Show();
            if (_hwnd != 0)
                Win32Interop.ConfigureOverlayWindow(_hwnd, clickThrough: false, noActivate: true);
        }

        double dpiScale = _hwnd != 0 ? Win32Interop.GetWindowDpiScale(_hwnd) : 1.0;
        double bubbleWpx = MusicBubbleControl.BubbleWidth * dpiScale;
        double bubbleHpx = MusicBubbleControl.TotalHeight * dpiScale;

        var mon = Win32Interop.FindMonitorForPoint(monitors, companionCenterX, companionCenterY);
        double leftPx = Math.Clamp(
            companionCenterX - bubbleWpx * 0.5,
            mon.WorkRect.Left + 8 * dpiScale,
            Math.Max(mon.WorkRect.Left + 8 * dpiScale, mon.WorkRect.Right - bubbleWpx - 8 * dpiScale));

        // Position directly above Aemeath's head
        double topPx = companionCenterY - companionHalfPx - bubbleHpx + 12 * dpiScale;
        topPx = Math.Clamp(
            topPx,
            mon.WorkRect.Top + 8 * dpiScale,
            Math.Max(mon.WorkRect.Top + 8 * dpiScale, mon.WorkRect.Bottom - bubbleHpx - 8 * dpiScale));

        if (_hwnd != 0)
        {
            Win32Interop.MoveWindowPhysical(_hwnd, (int)Math.Round(leftPx), (int)Math.Round(topPx), enforceTopmost);
        }

        double tailXdip = (companionCenterX - leftPx) / Math.Max(0.5, dpiScale);
        _bubble.UpdateFrame(dt, tailXdip, audio);
    }

    public void ReinforceTopmost()
    {
        if (_hwnd != 0 && IsVisible)
            Win32Interop.ForceTopmost(_hwnd);
    }
}
