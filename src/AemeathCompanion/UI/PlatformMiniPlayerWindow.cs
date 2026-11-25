using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AemeathCompanion.Media;
using AemeathCompanion.Native;

namespace AemeathCompanion.UI;

/// <summary>
/// Topmost, non-activating floating host window for Platform mode's compact horizontal mini player.
/// Default placement is directly above Aemeath's head; when monitor top space is insufficient,
/// automatically clamps to her left or right side with a matching side pixel tail.
/// Uses selective WM_NCHITTEST so only the 3 transport buttons capture clicks while everything else passes through.
/// </summary>
public sealed class PlatformMiniPlayerWindow : Window
{
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int WM_NCHITTEST = 0x0084;
    private const int MA_NOACTIVATE = 3;
    private const int HTTRANSPARENT = -1;
    private const int HTCLIENT = 1;

    private readonly PlatformMiniPlayerControl _control;
    private nint _hwnd;

    public event Action? PreviousClicked;
    public event Action? PlayPauseClicked;
    public event Action? NextClicked;

    public PlatformMiniPlayerWindow()
    {
        Title = "Aemeath Platform Mini Player";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = PlatformMiniPlayerControl.CanvasWidth;
        Height = PlatformMiniPlayerControl.CanvasHeight;

        _control = new PlatformMiniPlayerControl();
        _control.PreviousClicked += () => PreviousClicked?.Invoke();
        _control.PlayPauseClicked += () => PlayPauseClicked?.Invoke();
        _control.NextClicked += () => NextClicked?.Invoke();
        Content = _control;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
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
                if (_control.HitTestTransportButton(localDip) != MediaTransportButton.None)
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

    public void ApplySnapshot(MediaSnapshot snapshot) => _control.ApplySnapshot(snapshot);

    /// <summary>
    /// Pure placement computation: defaults directly above Aemeath's visual head;
    /// if monitor top clearance is insufficient, falls back to right or left side cleanly clamped inside monitor work area.
    /// </summary>
    public static (double LeftPx, double TopPx, MiniPlayerTailEdge TailEdge, double TailCenterDip) ComputePlacement(
        double companionCenterX,
        double companionCenterY,
        double companionHalfWpx,
        double companionHalfHpx,
        double spriteTopPadPx,
        NativeRect workRect,
        double dpiScale)
    {
        double canvasWpx = PlatformMiniPlayerControl.CanvasWidth * dpiScale;
        double bodyWpx = PlatformMiniPlayerControl.BodyWidth * dpiScale;
        double bodyHpx = PlatformMiniPlayerControl.BodyHeight * dpiScale;
        double sideMarginPx = PlatformMiniPlayerControl.SideMargin * dpiScale;
        double totalHpx = PlatformMiniPlayerControl.CanvasHeight * dpiScale;
        double marginPx = 6.0 * dpiScale;

        double visualHeadTopY = companionCenterY - companionHalfHpx + spriteTopPadPx;
        double preferredTopPx = visualHeadTopY - totalHpx - 4.0 * dpiScale;

        // 1. Preferred placement: directly above Aemeath's head
        if (preferredTopPx >= workRect.Top + marginPx)
        {
            double minBodyLeft = workRect.Left + marginPx;
            double maxBodyLeft = Math.Max(minBodyLeft, workRect.Right - bodyWpx - marginPx);
            double bodyLeftPx = Math.Clamp(companionCenterX - bodyWpx * 0.5, minBodyLeft, maxBodyLeft);
            double winLeftPx = bodyLeftPx - sideMarginPx;
            double tailXdip = (companionCenterX - winLeftPx) / Math.Max(0.5, dpiScale);
            return (winLeftPx, preferredTopPx, MiniPlayerTailEdge.Bottom, tailXdip);
        }

        // 2. Top clearance insufficient -> place on right or left side of Aemeath (whichever has room)
        double spriteVisualHalfW = Math.Min(companionHalfWpx, 85.0 * dpiScale);
        double sideGap = 6.0 * dpiScale;
        double rightBodyLeft = companionCenterX + spriteVisualHalfW + sideGap;
        double leftBodyLeft = companionCenterX - spriteVisualHalfW - sideGap - bodyWpx;

        bool fitsRight = rightBodyLeft + bodyWpx <= workRect.Right - marginPx;
        bool fitsLeft = leftBodyLeft >= workRect.Left + marginPx;

        MiniPlayerTailEdge tailEdge;
        double chosenBodyLeft;
        if (fitsRight || (!fitsLeft && (workRect.Right - companionCenterX) >= (companionCenterX - workRect.Left)))
        {
            tailEdge = MiniPlayerTailEdge.Left;
            chosenBodyLeft = Math.Clamp(rightBodyLeft, workRect.Left + marginPx,
                Math.Max(workRect.Left + marginPx, workRect.Right - bodyWpx - marginPx));
        }
        else
        {
            tailEdge = MiniPlayerTailEdge.Right;
            chosenBodyLeft = Math.Clamp(leftBodyLeft, workRect.Left + marginPx,
                Math.Max(workRect.Left + marginPx, workRect.Right - bodyWpx - marginPx));
        }

        double targetCenterY = visualHeadTopY + 34.0 * dpiScale;
        double sideTopPx = Math.Clamp(
            targetCenterY - bodyHpx * 0.5,
            workRect.Top + marginPx,
            Math.Max(workRect.Top + marginPx, workRect.Bottom - bodyHpx - marginPx));
        double tailYdip = (targetCenterY - sideTopPx) / Math.Max(0.5, dpiScale);

        return (chosenBodyLeft - sideMarginPx, sideTopPx, tailEdge, tailYdip);
    }

    public void UpdateLayoutAndRender(
        bool visible,
        double companionCenterX,
        double companionCenterY,
        double companionHalfWpx,
        double companionHalfHpx,
        double spriteTopPadPx,
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
        var mon = Win32Interop.FindMonitorForPoint(monitors, companionCenterX, companionCenterY);

        var (leftPx, topPx, tailEdge, tailCenterDip) = ComputePlacement(
            companionCenterX,
            companionCenterY,
            companionHalfWpx,
            companionHalfHpx,
            spriteTopPadPx,
            mon.WorkRect,
            dpiScale);

        if (_hwnd != 0)
        {
            Win32Interop.MoveWindowPhysical(_hwnd, (int)Math.Round(leftPx), (int)Math.Round(topPx), enforceTopmost);
        }

        _control.UpdateFrame(dt, tailEdge, tailCenterDip, audio);
    }

    public void ReinforceTopmost()
    {
        if (_hwnd != 0 && IsVisible)
            Win32Interop.ForceTopmost(_hwnd);
    }
}
