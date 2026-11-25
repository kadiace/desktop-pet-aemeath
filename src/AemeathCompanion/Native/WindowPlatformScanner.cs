using System.Diagnostics;

namespace AemeathCompanion.Native;

public sealed record RawWindowInfo(
    nint Hwnd,
    NativeRect Rect,
    string Title,
    string ClassName,
    string ProcessName);

public sealed record PlatformSurface(
    nint Hwnd,
    int Left,
    int Right,
    int Top,
    int WindowBottom,
    string Title,
    bool IsWorkAreaFloor = false);

/// <summary>
/// Discovers visible application windows (including Task Manager, browsers, editors, etc.)
/// in Z-order and computes unoccluded horizontal top-edge platforms for State B gravity landing.
/// </summary>
public sealed class WindowPlatformScanner
{
    private static readonly HashSet<string> ExcludedClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "DV2ControlHost",
        "Windows.UI.Core.CoreWindow",
        "MultitaskingViewFrame",
        "ForegroundStaging",
        "ApplicationManager_DesktopShellWindow",
        "EdgeUiInputTopWndClass",
        "NativeHWNDHost",
        "ThumbnailDeviceHelperWnd",
        "NarratorHelperWindow"
    };

    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "whale", "vivaldi", "arc", "zen", "waterfox"
    };

    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    public List<RawWindowInfo> CaptureVisibleWindows(IReadOnlyList<MonitorBounds> monitors)
    {
        var results = new List<RawWindowInfo>(64);

        Win32Interop.EnumWindows((hwnd, lParam) =>
        {
            if (!Win32Interop.IsWindowVisible(hwnd) || Win32Interop.IsIconic(hwnd))
                return true;

            Win32Interop.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == _ownProcessId)
                return true;

            if (Win32Interop.IsWindowCloaked(hwnd))
                return true;

            string className = Win32Interop.GetWindowClassName(hwnd);
            if (string.IsNullOrEmpty(className) || ExcludedClassNames.Contains(className))
                return true;

            long exStyle = Win32Interop.GetWindowLongPtr(hwnd, Win32Interop.GWL_EXSTYLE);
            // Skip pure click-through overlays and small floating tool palettes (except Task Manager / normal apps)
            if ((exStyle & Win32Interop.WS_EX_TRANSPARENT) != 0)
                return true;

            string title = Win32Interop.GetWindowTitle(hwnd);
            bool isTaskMgr = className.Equals("TaskManagerWindow", StringComparison.OrdinalIgnoreCase);

            // Most real windows have a title; toolwindows without title are system helpers.
            if (string.IsNullOrWhiteSpace(title) && !isTaskMgr)
                return true;

            if ((exStyle & Win32Interop.WS_EX_TOOLWINDOW) != 0 && !isTaskMgr)
                return true;

            if (!Win32Interop.TryGetVisibleWindowBounds(hwnd, out var rect))
                return true;

            if (rect.Width < 120 || rect.Height < 60)
                return true;

            // Skip maximized/fullscreen windows whose top edge is already at/above the monitor top,
            // because Aemeath cannot stand above the top edge of the screen; still record them as occluders!
            string procName = Win32Interop.GetProcessNameForWindow(hwnd);
            results.Add(new RawWindowInfo(hwnd, rect, title, className, procName));
            return true;
        }, 0);

        return results;
    }

    public List<string> GetOpenBrowserTitles(IReadOnlyList<RawWindowInfo> windows)
    {
        var titles = new List<string>();
        foreach (var w in windows)
        {
            if (!string.IsNullOrEmpty(w.Title) &&
                (BrowserProcesses.Contains(w.ProcessName) ||
                 w.ClassName.Equals("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase) ||
                 w.ClassName.Equals("MozillaWindowClass", StringComparison.OrdinalIgnoreCase)))
            {
                titles.Add(w.Title);
            }
        }
        return titles;
    }

    /// <summary>
    /// Computes unoccluded horizontal segments along the top border of each visible window
    /// from front-to-back Z-order. Pure logic so it can be thoroughly unit-tested.
    /// </summary>
    public static List<PlatformSurface> ComputeVisiblePlatforms(
        IReadOnlyList<RawWindowInfo> frontToBackWindows,
        IReadOnlyList<MonitorBounds> monitors,
        int minPlatformWidth = 44,
        int minTopClearancePx = 28)
    {
        var platforms = new List<PlatformSurface>(frontToBackWindows.Count + monitors.Count);
        var occluders = new List<NativeRect>(frontToBackWindows.Count);

        foreach (var w in frontToBackWindows)
        {
            var r = w.Rect;
            // Ensure the top edge has enough clearance below the monitor's top so Aemeath isn't clipped off-screen
            var mon = Win32Interop.FindMonitorForPoint(monitors, (r.Left + r.Right) * 0.5, r.Top);
            bool topUsable = r.Top >= mon.WorkRect.Top + minTopClearancePx && r.Top < mon.WorkRect.Bottom - 8;

            if (topUsable)
            {
                // Start with the window's top edge clipped to its monitor's work area horizontally
                int segStart = Math.Max(r.Left, mon.WorkRect.Left);
                int segEnd = Math.Min(r.Right, mon.WorkRect.Right);

                if (segEnd - segStart >= minPlatformWidth)
                {
                    var segments = new List<(int Start, int End)> { (segStart, segEnd) };

                    foreach (var occ in occluders)
                    {
                        // An occluder blocks this top edge if it covers vertically across y = r.Top
                        if (occ.Top <= r.Top + 4 && occ.Bottom > r.Top + 4)
                        {
                            SubtractInterval(segments, occ.Left, occ.Right);
                            if (segments.Count == 0) break;
                        }
                    }

                    foreach (var (s, e) in segments)
                    {
                        if (e - s >= minPlatformWidth)
                        {
                            platforms.Add(new PlatformSurface(w.Hwnd, s, e, r.Top, r.Bottom, w.Title, false));
                        }
                    }
                }
            }

            occluders.Add(r);
        }

        // Add each monitor's taskbar-adjusted work-area floor as the guaranteed fallback floor
        foreach (var mon in monitors)
        {
            platforms.Add(new PlatformSurface(
                0,
                mon.WorkRect.Left,
                mon.WorkRect.Right,
                mon.WorkRect.Bottom,
                mon.MonitorRect.Bottom,
                "Taskbar Floor",
                IsWorkAreaFloor: true));
        }

        return platforms;
    }

    private static void SubtractInterval(List<(int Start, int End)> segments, int cutLeft, int cutRight)
    {
        if (cutRight <= cutLeft) return;

        for (int i = segments.Count - 1; i >= 0; i--)
        {
            var (s, e) = segments[i];
            if (cutRight <= s || cutLeft >= e) continue;

            segments.RemoveAt(i);
            if (cutLeft > s)
                segments.Insert(i, (s, cutLeft));
            if (cutRight < e)
                segments.Insert(cutLeft > s ? i + 1 : i, (cutRight, e));
        }
    }
}
