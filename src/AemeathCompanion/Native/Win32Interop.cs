using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AemeathCompanion.Native;

[StructLayout(LayoutKind.Sequential)]
public struct NativePoint
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

public sealed record MonitorBounds(NativeRect MonitorRect, NativeRect WorkRect, bool IsPrimary);

public static class Win32Interop
{
    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE = -16;

    public const long WS_EX_TRANSPARENT = 0x00000020L;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_TOPMOST = 0x00000008L;
    public const long WS_EX_LAYERED = 0x00080000L;
    public const long WS_EX_NOACTIVATE = 0x08000000L;

    public const long WS_VISIBLE = 0x10000000L;
    public const long WS_CHILD = 0x40000000L;
    public const long WS_MINIMIZE = 0x20000000L;

    public static readonly nint HWND_TOPMOST = new(-1);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint MONITORINFOF_PRIMARY = 0x00000001;
    public const int VK_LBUTTON = 0x01;
    public const int SM_CXDOUBLECLK = 36;
    public const int SM_CYDOUBLECLK = 37;

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    public delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref NativeRect lprcMonitor, nint dwData);
    public delegate void WinEventDelegate(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    public static long GetWindowLongPtr(nint hWnd, int nIndex)
        => Environment.Is64BitProcess ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    public static long SetWindowLongPtr(nint hWnd, int nIndex, long newValue)
        => Environment.Is64BitProcess
            ? SetWindowLongPtr64(hWnd, nIndex, new nint(newValue))
            : SetWindowLong32(hWnd, nIndex, (int)newValue);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public static bool IsLeftMouseButtonDown() => (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLengthW(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out NativeRect pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    public static void ConfigureOverlayWindow(nint hwnd, bool clickThrough, bool noActivate)
    {
        if (hwnd == 0) return;
        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW;
        if (noActivate) exStyle |= WS_EX_NOACTIVATE;
        if (clickThrough) exStyle |= WS_EX_TRANSPARENT;
        else exStyle &= ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle);
        ForceTopmost(hwnd);
    }

    public static void ForceTopmost(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd)) return;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static void MoveWindowPhysical(nint hwnd, int leftPx, int topPx, bool enforceTopmost = false)
    {
        if (hwnd == 0) return;
        uint flags = SWP_NOSIZE | SWP_NOACTIVATE;
        if (!enforceTopmost) flags |= SWP_NOZORDER;
        SetWindowPos(hwnd, HWND_TOPMOST, leftPx, topPx, 0, 0, flags);
    }

    public static void SetWindowBoundsPhysical(nint hwnd, int leftPx, int topPx, int widthPx, int heightPx, bool enforceTopmost = false)
    {
        if (hwnd == 0) return;
        uint flags = SWP_NOACTIVATE;
        if (!enforceTopmost) flags |= SWP_NOZORDER;
        SetWindowPos(hwnd, HWND_TOPMOST, leftPx, topPx, Math.Max(1, widthPx), Math.Max(1, heightPx), flags);
    }

    public static double GetWindowDpiScale(nint hwnd)
    {
        if (hwnd == 0) return 1.0;
        uint dpi = GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    public static bool TryGetVisibleWindowBounds(nint hwnd, out NativeRect bounds)
    {
        int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out bounds, Marshal.SizeOf<NativeRect>());
        if (hr == 0 && bounds.Width > 0 && bounds.Height > 0)
            return true;
        return GetWindowRect(hwnd, out bounds) && bounds.Width > 0 && bounds.Height > 0;
    }

    public static bool IsWindowCloaked(nint hwnd)
    {
        int hr = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    public static string GetWindowTitle(nint hwnd)
    {
        int len = GetWindowTextLengthW(hwnd);
        if (len <= 0) return string.Empty;
        var sb = new StringBuilder(len + 1);
        _ = GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetWindowClassName(nint hwnd)
    {
        var sb = new StringBuilder(256);
        int len = GetClassNameW(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString(0, len) : string.Empty;
    }

    public static string GetProcessNameForWindow(nint hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return string.Empty;
        nint hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc == 0) return string.Empty;
        try
        {
            var sb = new StringBuilder(512);
            uint size = (uint)sb.Capacity;
            if (!QueryFullProcessImageNameW(hProc, 0, sb, ref size)) return string.Empty;
            return Path.GetFileNameWithoutExtension(sb.ToString());
        }
        finally
        {
            CloseHandle(hProc);
        }
    }

    public static List<MonitorBounds> GetAllMonitors()
    {
        var list = new List<MonitorBounds>();
        EnumDisplayMonitors(0, 0, (nint hMon, nint _, ref NativeRect _, nint _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfoW(hMon, ref info))
            {
                list.Add(new MonitorBounds(info.rcMonitor, info.rcWork, (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        }, 0);

        if (list.Count == 0)
        {
            var fallback = new NativeRect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
            list.Add(new MonitorBounds(fallback, fallback, true));
        }

        return list;
    }

    public static NativeRect GetVirtualWorkBounds(IReadOnlyList<MonitorBounds> monitors)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var m in monitors)
        {
            if (m.WorkRect.Left < minX) minX = m.WorkRect.Left;
            if (m.WorkRect.Top < minY) minY = m.WorkRect.Top;
            if (m.WorkRect.Right > maxX) maxX = m.WorkRect.Right;
            if (m.WorkRect.Bottom > maxY) maxY = m.WorkRect.Bottom;
        }
        return new NativeRect { Left = minX, Top = minY, Right = maxX, Bottom = maxY };
    }

    public static MonitorBounds FindMonitorForPoint(IReadOnlyList<MonitorBounds> monitors, double x, double y)
    {
        foreach (var m in monitors)
        {
            if (m.MonitorRect.Contains(x, y))
                return m;
        }

        // Closest monitor center fallback
        MonitorBounds best = monitors[0];
        double bestDist = double.MaxValue;
        foreach (var m in monitors)
        {
            double cx = (m.WorkRect.Left + m.WorkRect.Right) * 0.5;
            double cy = (m.WorkRect.Top + m.WorkRect.Bottom) * 0.5;
            double d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
            if (d < bestDist)
            {
                bestDist = d;
                best = m;
            }
        }
        return best;
    }
}
