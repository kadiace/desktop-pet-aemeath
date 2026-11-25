using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AemeathCompanion.Engine;
using AemeathCompanion.Media;
using AemeathCompanion.Native;
using AemeathCompanion.Update;

namespace AemeathCompanion.UI;

public partial class CompanionWindow : Window
{
    private readonly GifSpriteBank _sprites = new();
    private readonly CompanionController _controller = new();
    private readonly WindowPlatformScanner _platformScanner = new();
    private readonly WasapiLoopbackAnalyzer _audioAnalyzer = new();
    private readonly SmtcMediaService _smtcService;
    private readonly AppUpdateService _updateService;
    private readonly MusicBubbleWindow _bubbleWindow;
    private readonly ActionMenuWindow _menuWindow;
    private readonly TrayIconService _trayService;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTickSec;
    private double _platformScanCooldown;
    private double _monitorScanCooldown;
    private double _topmostWatchdogCooldown;

    private List<MonitorBounds> _monitors = [];
    private List<RawWindowInfo> _rawWindows = [];
    private List<PlatformSurface> _platforms = [];
    private volatile IReadOnlyList<string> _browserTitlesSnapshot = Array.Empty<string>();

    // Sprite frame state
    private string _currentSpriteName = string.Empty;
    private int _currentFrameIndex;
    private double _frameElapsed;

    // Input state
    private bool _leftArmed;
    private bool _dragStarted;
    private bool _holdMenuOpened;
    private NativePoint _pressCursorPx;
    private double _pressTimeSec;
    private double _grabOffsetX;
    private double _grabOffsetY;
    private double _lastClickTimeSec = -10;
    private NativePoint _lastClickPosPx;

    // Win32 handles & foreground hook
    private nint _hwnd;
    private nint _foregroundHook;
    private Win32Interop.WinEventDelegate? _winEventCallback;
    private bool _closing;

    public CompanionWindow()
    {
        InitializeComponent();

        string spriteDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Sprites");
        _sprites.LoadFromDirectory(spriteDir);

        _monitors = Win32Interop.GetAllMonitors();
        _rawWindows = _platformScanner.CaptureVisibleWindows(_monitors);
        _platforms = WindowPlatformScanner.ComputeVisiblePlatforms(_rawWindows, _monitors);
        _browserTitlesSnapshot = _platformScanner.GetOpenBrowserTitles(_rawWindows);

        var saved = CompanionStateStorage.Load();
        _controller.InitializeFromState(saved, _monitors, 1.0);
        _controller.SpriteRestartRequested += OnSpriteRestartRequested;
        _updateService = new AppUpdateService(saved.UpdateServerUrl);

        _bubbleWindow = new MusicBubbleWindow();
        _menuWindow = new ActionMenuWindow();
        WireActionMenuEvents();

        _updateService.UpdateStatusChanged += result =>
        {
            if (_closing) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (!_closing && _menuWindow.IsVisible)
                    _menuWindow.SyncUpdateUi(result, _updateService.IsChecking, _updateService.IsUpdating);
            });
        };

        _trayService = new TrayIconService(spriteDir);
        WireTrayEvents();

        _smtcService = new SmtcMediaService(() => _browserTitlesSnapshot);
        _smtcService.SnapshotUpdated += OnSmtcSnapshotUpdated;

        _bubbleWindow.PreviousClicked += () => _ = _smtcService.SkipPreviousAsync();
        _bubbleWindow.PlayPauseClicked += () => _ = _smtcService.TogglePlayPauseAsync();
        _bubbleWindow.NextClicked += () => _ = _smtcService.SkipNextAsync();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnWindowClosing;

        MouseLeftButtonDown += OnMouseLeftDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftUp;
        MouseRightButtonUp += OnMouseRightUp;

        ApplyWindowScale();
        ApplyCurrentSpriteFrame(forceRedraw: true);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Win32Interop.ConfigureOverlayWindow(_hwnd, clickThrough: false, noActivate: true);

        // Re-assert topmost whenever another window takes foreground (Condition 2)
        _winEventCallback = OnWinEvent;
        _foregroundHook = Win32Interop.SetWinEventHook(
            Win32Interop.EVENT_SYSTEM_FOREGROUND,
            Win32Interop.EVENT_SYSTEM_FOREGROUND,
            0,
            _winEventCallback,
            0,
            0,
            Win32Interop.WINEVENT_OUTOFCONTEXT | Win32Interop.WINEVENT_SKIPOWNPROCESS);

        MoveSelfPhysical(enforceTopmost: _controller.AlwaysOnTop);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _lastTickSec = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnCompositionRendering;
        _ = _updateService.CheckForUpdatesAsync();
        await _smtcService.StartAsync();
    }

    private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (_closing || !_controller.AlwaysOnTop) return;
        Dispatcher.BeginInvoke(ReinforceTopmostAll);
    }

    private void ReinforceTopmostAll()
    {
        if (_closing || !_controller.AlwaysOnTop) return;
        if (_hwnd != 0) Win32Interop.ForceTopmost(_hwnd);
        _bubbleWindow.ReinforceTopmost();
    }

    private void OnSpriteRestartRequested(string spriteName)
    {
        if (string.Equals(_currentSpriteName, spriteName, StringComparison.OrdinalIgnoreCase))
        {
            _currentFrameIndex = 0;
            _frameElapsed = 0;
        }
    }

    private void OnSmtcSnapshotUpdated(MediaSnapshot snapshot)
    {
        if (_closing) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            double dpiScale = CurrentDpiScale();
            _controller.UpdateMusicState(snapshot.IsMusicPlaying, _monitors, dpiScale);
            _bubbleWindow.ApplySnapshot(snapshot);
            _audioAnalyzer.SetActive(_controller.MusicPlaying);

            if (_menuWindow.IsVisible)
            {
                _menuWindow.SyncUi(_controller, snapshot);
            }
        });
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (_closing) return;

        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTickSec, 0.0, 0.05);
        _lastTickSec = now;

        double dpiScale = CurrentDpiScale();

        // Periodically refresh monitors
        _monitorScanCooldown -= dt;
        if (_monitorScanCooldown <= 0)
        {
            _monitors = Win32Interop.GetAllMonitors();
            _monitorScanCooldown = 2.0;
        }

        // Refresh window platforms rapidly in Mode B (~30Hz) so window landing & window riding feel locked in;
        // slower in Mode A (~1.7Hz) just to keep browser tab titles fresh for Condition 4.
        _platformScanCooldown -= dt;
        if (_platformScanCooldown <= 0)
        {
            _rawWindows = _platformScanner.CaptureVisibleWindows(_monitors);
            _browserTitlesSnapshot = _platformScanner.GetOpenBrowserTitles(_rawWindows);
            if (_controller.Mode == CompanionMode.B)
            {
                _platforms = WindowPlatformScanner.ComputeVisiblePlatforms(_rawWindows, _monitors);
                _platformScanCooldown = 0.033;
            }
            else
            {
                _platformScanCooldown = 0.6;
            }
        }

        // Poll physical left button & cursor while armed/dragging so WS_EX_NOACTIVATE
        // or sprite-size changes can never drop drag tracking or leave drag stuck.
        if (_leftArmed)
        {
            if (!Win32Interop.IsLeftMouseButtonDown())
            {
                FinishLeftPointer();
            }
            else if (Win32Interop.GetCursorPos(out var dragCur))
            {
                StepLeftPointerDrag(dragCur, dpiScale);
            }
        }

        // Long-press fallback (650ms stationary left press opens menu, matching Wallpaper Engine muscle memory)
        if (_leftArmed && !_dragStarted && !_holdMenuOpened && !_menuWindow.IsVisible)
        {
            if (now - _pressTimeSec >= 0.65 && Win32Interop.GetCursorPos(out var cur))
            {
                double dx = cur.X - _pressCursorPx.X;
                double dy = cur.Y - _pressCursorPx.Y;
                double thresh = 7.0 * dpiScale;
                if (dx * dx + dy * dy <= thresh * thresh)
                {
                    _holdMenuOpened = true;
                    _leftArmed = false;
                    if (IsMouseCaptured) ReleaseMouseCapture();
                    OpenActionMenu();
                }
            }
        }

        _controller.MenuOpen = _menuWindow.IsVisible;
        if (_menuWindow.IsVisible)
        {
            _menuWindow.UpdatePlaybackTime(_smtcService.Current);
        }

        NativePoint? cursor = Win32Interop.GetCursorPos(out var pt) ? pt : null;
        _controller.Tick(dt, _monitors, _platforms, cursor, dpiScale);
        _audioAnalyzer.SetActive(_controller.MusicPlaying);

        // Periodic topmost watchdog (Condition 2)
        _topmostWatchdogCooldown -= dt;
        bool enforceTop = false;
        if (_controller.AlwaysOnTop && _topmostWatchdogCooldown <= 0)
        {
            enforceTop = true;
            _topmostWatchdogCooldown = 1.25;
        }

        MoveSelfPhysical(enforceTopmost: enforceTop);
        AdvanceSpriteAnimation(dt);

        _bubbleWindow.UpdateLayoutAndRender(
            visible: _controller.ShouldShowBubble,
            companionCenterX: _controller.X,
            companionCenterY: _controller.Y,
            companionHalfPx: _controller.HalfSizePx(dpiScale),
            monitors: _monitors,
            dt: dt,
            audio: _audioAnalyzer,
            enforceTopmost: enforceTop);

        if (_controller.ConsumeDirtyFlag(dt))
        {
            CompanionStateStorage.Save(_controller.ExportState());
        }
    }

    private void AdvanceSpriteAnimation(double dt)
    {
        string targetName = _controller.ActiveSpriteName;
        bool switched = !string.Equals(targetName, _currentSpriteName, StringComparison.OrdinalIgnoreCase);

        if (switched)
        {
            _currentSpriteName = targetName;
            _currentFrameIndex = 0;
            _frameElapsed = 0;
            ApplyWindowScale();
            ApplyCurrentSpriteFrame(forceRedraw: true);
            MoveSelfPhysical(enforceTopmost: false);
        }
        else
        {
            var anim = _sprites.Get(_currentSpriteName);
            if (anim.Frames.Count > 1)
            {
                _frameElapsed += dt;
                double delay = anim.Frames[_currentFrameIndex].DelaySeconds;
                if (_frameElapsed >= delay)
                {
                    _frameElapsed %= delay;
                    _currentFrameIndex = (_currentFrameIndex + 1) % anim.Frames.Count;
                    ApplyCurrentSpriteFrame(forceRedraw: false);
                }
            }
        }

        double desiredScaleX = _controller.ShouldFlipHorizontal ? -1.0 : 1.0;
        if (Math.Abs(SpriteFlipTransform.ScaleX - desiredScaleX) > 0.01)
            SpriteFlipTransform.ScaleX = desiredScaleX;
    }

    private void ApplyCurrentSpriteFrame(bool forceRedraw)
    {
        if (string.IsNullOrEmpty(_currentSpriteName))
            _currentSpriteName = _controller.ActiveSpriteName;

        var anim = _sprites.Get(_currentSpriteName);
        if (_currentFrameIndex >= anim.Frames.Count) _currentFrameIndex = 0;
        SpriteImage.Source = anim.Frames[_currentFrameIndex].Bitmap;
    }

    private void ApplyWindowScale()
    {
        var metrics = _controller.ActiveMetrics;
        double widthDip = Math.Max(48.0, metrics.WidthDip * _controller.SizeMultiplier);
        double heightDip = Math.Max(48.0, metrics.HeightDip * _controller.SizeMultiplier);
        if (Math.Abs(Width - widthDip) > 0.25 || Math.Abs(Height - heightDip) > 0.25)
        {
            Width = widthDip;
            Height = heightDip;
        }
    }

    private void MoveSelfPhysical(bool enforceTopmost)
    {
        if (_hwnd == 0) return;
        double dpiScale = CurrentDpiScale();
        double halfW = _controller.HalfWidthPx(dpiScale);
        double halfH = _controller.HalfHeightPx(dpiScale);
        int leftPx = (int)Math.Round(_controller.X - halfW);
        int topPx = (int)Math.Round(_controller.Y - halfH);
        Win32Interop.MoveWindowPhysical(_hwnd, leftPx, topPx, enforceTopmost);
    }

    private double CurrentDpiScale() => _hwnd != 0 ? Win32Interop.GetWindowDpiScale(_hwnd) : 1.0;

    // --- Native Mouse Interactions ---

    private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (_menuWindow.IsVisible) _menuWindow.Hide();

        if (!Win32Interop.GetCursorPos(out var cur)) return;
        _leftArmed = true;
        _dragStarted = false;
        _holdMenuOpened = false;
        _pressCursorPx = cur;
        _pressTimeSec = _clock.Elapsed.TotalSeconds;
        _grabOffsetX = _controller.X - cur.X;
        _grabOffsetY = _controller.Y - cur.Y;
        CaptureMouse();
        e.Handled = true;
    }

    private void StepLeftPointerDrag(NativePoint cur, double dpiScale)
    {
        if (!_dragStarted)
        {
            double dx = cur.X - _pressCursorPx.X;
            double dy = cur.Y - _pressCursorPx.Y;
            double thresh = 7.0 * dpiScale;
            if (dx * dx + dy * dy > thresh * thresh)
            {
                _dragStarted = true;
                _lastClickTimeSec = -10; // Cancel any pending double-click sequence
                _controller.BeginDrag(_monitors, dpiScale);
                _audioAnalyzer.SetActive(_controller.MusicPlaying);
            }
        }

        if (_dragStarted)
        {
            _controller.UpdateDrag(cur.X + _grabOffsetX, cur.Y + _grabOffsetY, _monitors, dpiScale);
            MoveSelfPhysical(enforceTopmost: false);
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_leftArmed) return;
        if (e.LeftButton != MouseButtonState.Pressed && !Win32Interop.IsLeftMouseButtonDown())
        {
            FinishLeftPointer();
            return;
        }

        if (!Win32Interop.GetCursorPos(out var cur)) return;
        StepLeftPointerDrag(cur, CurrentDpiScale());
    }

    private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        FinishLeftPointer();
        e.Handled = true;
    }

    private void FinishLeftPointer()
    {
        if (!_leftArmed) return;
        _leftArmed = false;
        if (IsMouseCaptured) ReleaseMouseCapture();

        if (_holdMenuOpened)
        {
            _holdMenuOpened = false;
            return;
        }

        double dpiScale = CurrentDpiScale();
        if (_dragStarted)
        {
            _dragStarted = false;
            _lastClickTimeSec = -10;
            // Refresh platforms right before drop so landing is exact
            if (_controller.Mode == CompanionMode.B)
            {
                _rawWindows = _platformScanner.CaptureVisibleWindows(_monitors);
                _platforms = WindowPlatformScanner.ComputeVisiblePlatforms(_rawWindows, _monitors);
            }
            _controller.EndDrag(_monitors, dpiScale);
            return;
        }

        // Only evaluate double-click on clean non-drag release so a quick click + drag never hijacks into mode toggle
        double now = _clock.Elapsed.TotalSeconds;
        double dblIntervalSec = Math.Clamp(Win32Interop.GetDoubleClickTime() / 1000.0, 0.2, 0.7);
        double maxDist = 24.0 * dpiScale;
        double cdx = _pressCursorPx.X - _lastClickPosPx.X;
        double cdy = _pressCursorPx.Y - _lastClickPosPx.Y;
        bool isDoubleClick = (now - _lastClickTimeSec) <= dblIntervalSec &&
                             (cdx * cdx + cdy * cdy) <= maxDist * maxDist;

        if (isDoubleClick)
        {
            _lastClickTimeSec = -10;
            _controller.HandleDoubleClick(_monitors, dpiScale);
        }
        else
        {
            _lastClickTimeSec = now;
            _lastClickPosPx = _pressCursorPx;
            _controller.HandleSingleClick(_monitors, dpiScale);
        }

        _audioAnalyzer.SetActive(_controller.MusicPlaying);
        if (_controller.Mode == CompanionMode.B)
        {
            _rawWindows = _platformScanner.CaptureVisibleWindows(_monitors);
            _platforms = WindowPlatformScanner.ComputeVisiblePlatforms(_rawWindows, _monitors);
        }
    }

    private void OnMouseRightUp(object sender, MouseButtonEventArgs e)
    {
        if (_leftArmed)
        {
            _leftArmed = false;
            _dragStarted = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }
        OpenActionMenu();
        e.Handled = true;
    }

    private void OpenActionMenu()
    {
        double dpiScale = CurrentDpiScale();
        _menuWindow.ShowAt(
            _controller.X,
            _controller.Y,
            _controller.HalfWidthPx(dpiScale),
            _monitors,
            _controller,
            _smtcService.Current,
            _updateService.LatestResult,
            _updateService.IsChecking,
            _updateService.IsUpdating);
    }

    private void WireActionMenuEvents()
    {
        _menuWindow.ModeSelected += mode =>
        {
            double dpiScale = CurrentDpiScale();
            _controller.SetMode(mode, _monitors, dpiScale);
            _audioAnalyzer.SetActive(_controller.MusicPlaying);
            if (mode == CompanionMode.B)
            {
                _rawWindows = _platformScanner.CaptureVisibleWindows(_monitors);
                _platforms = WindowPlatformScanner.ComputeVisiblePlatforms(_rawWindows, _monitors);
            }
        };

        _menuWindow.MusicModeSelected += () =>
        {
            double dpiScale = CurrentDpiScale();
            _controller.EnterMusicMode(_monitors, dpiScale);
            _bubbleWindow.ApplySnapshot(_smtcService.Current);
            _audioAnalyzer.SetActive(_controller.MusicPlaying);
        };

        _menuWindow.VisualSelected += visual =>
        {
            double dpiScale = CurrentDpiScale();
            _controller.SelectManualVisual(visual, _monitors, dpiScale);
            if (_controller.Mode == CompanionMode.B)
            {
                _rawWindows = _platformScanner.CaptureVisibleWindows(_monitors);
                _platforms = WindowPlatformScanner.ComputeVisiblePlatforms(_rawWindows, _monitors);
            }
        };

        _menuWindow.ScaleChanged += scale =>
        {
            double dpiScale = CurrentDpiScale();
            _controller.SetSizeMultiplier(scale, _monitors, dpiScale);
            ApplyWindowScale();
            MoveSelfPhysical(enforceTopmost: false);
        };

        _menuWindow.AlwaysOnTopToggled += () =>
        {
            _controller.AlwaysOnTop = !_controller.AlwaysOnTop;
            Topmost = _controller.AlwaysOnTop;
            _bubbleWindow.Topmost = _controller.AlwaysOnTop;
            if (_controller.AlwaysOnTop) ReinforceTopmostAll();
            CompanionStateStorage.Save(_controller.ExportState());
            _menuWindow.SyncUi(_controller, _smtcService.Current);
        };

        _menuWindow.MediaPreviousRequested += () => _ = _smtcService.SkipPreviousAsync();
        _menuWindow.MediaPlayPauseRequested += () => _ = _smtcService.TogglePlayPauseAsync();
        _menuWindow.MediaNextRequested += () => _ = _smtcService.SkipNextAsync();
        _menuWindow.CheckUpdateRequested += OnCheckUpdateRequested;
        _menuWindow.ApplyUpdateRequested += OnApplyUpdateRequested;
        _menuWindow.ResetPositionRequested += ResetPositionToPrimaryCenter;
        _menuWindow.ExitRequested += ShutdownApp;
    }

    private async void OnCheckUpdateRequested()
    {
        _menuWindow.SyncUpdateUi(_updateService.LatestResult, isChecking: true, isUpdating: _updateService.IsUpdating);
        var result = await _updateService.CheckForUpdatesAsync();
        if (!_closing && _menuWindow.IsVisible)
            _menuWindow.SyncUpdateUi(result, isChecking: false, isUpdating: _updateService.IsUpdating);
    }

    private async void OnApplyUpdateRequested()
    {
        var latest = _updateService.LatestResult;
        if (!latest.IsUpdateAvailable) return;

        _menuWindow.SyncUpdateUi(latest, isChecking: false, isUpdating: true, progressText: "다운로드 준비 중...");
        var (shouldShutdown, message) = await _updateService.DownloadAndApplyUpdateAsync(
            latest,
            pct => Dispatcher.BeginInvoke(() =>
            {
                if (!_closing && _menuWindow.IsVisible)
                    _menuWindow.SyncUpdateUi(latest, isChecking: false, isUpdating: true, progressText: $"다운로드 {pct}%");
            }));

        if (shouldShutdown)
        {
            ShutdownApp();
            return;
        }

        if (!_closing && _menuWindow.IsVisible)
        {
            var updated = latest with { StatusText = message };
            _menuWindow.SyncUpdateUi(updated, isChecking: false, isUpdating: false);
        }
    }

    private void WireTrayEvents()
    {
        _trayService.OpenMenuRequested += () => Dispatcher.BeginInvoke(OpenActionMenu);
        _trayService.SwitchModeARequested += () => Dispatcher.BeginInvoke(() =>
            _controller.SetMode(CompanionMode.A, _monitors, CurrentDpiScale()));
        _trayService.SwitchModeBRequested += () => Dispatcher.BeginInvoke(() =>
        {
            _controller.SetMode(CompanionMode.B, _monitors, CurrentDpiScale());
            _audioAnalyzer.SetActive(_controller.MusicPlaying);
            _rawWindows = _platformScanner.CaptureVisibleWindows(_monitors);
            _platforms = WindowPlatformScanner.ComputeVisiblePlatforms(_rawWindows, _monitors);
        });
        _trayService.SwitchMusicModeRequested += () => Dispatcher.BeginInvoke(() =>
        {
            _controller.EnterMusicMode(_monitors, CurrentDpiScale());
            _bubbleWindow.ApplySnapshot(_smtcService.Current);
            _audioAnalyzer.SetActive(_controller.MusicPlaying);
        });
        _trayService.ResetPositionRequested += () => Dispatcher.BeginInvoke(ResetPositionToPrimaryCenter);
        _trayService.ResetScaleRequested += () => Dispatcher.BeginInvoke(() =>
        {
            _controller.SetSizeMultiplier(1.0, _monitors, CurrentDpiScale());
            ApplyWindowScale();
            MoveSelfPhysical(enforceTopmost: false);
        });
        _trayService.ToggleTopmostRequested += () => Dispatcher.BeginInvoke(() =>
        {
            _controller.AlwaysOnTop = !_controller.AlwaysOnTop;
            Topmost = _controller.AlwaysOnTop;
            _bubbleWindow.Topmost = _controller.AlwaysOnTop;
            if (_controller.AlwaysOnTop) ReinforceTopmostAll();
            CompanionStateStorage.Save(_controller.ExportState());
        });
        _trayService.ExitRequested += () => Dispatcher.BeginInvoke(ShutdownApp);
    }

    private void ResetPositionToPrimaryCenter()
    {
        _monitors = Win32Interop.GetAllMonitors();
        var primary = _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors[0];
        double cx = (primary.WorkRect.Left + primary.WorkRect.Right) * 0.5;
        double cy = (primary.WorkRect.Top + primary.WorkRect.Bottom) * 0.5;
        _controller.SetPosition(cx, cy, _monitors, CurrentDpiScale());
        MoveSelfPhysical(enforceTopmost: _controller.AlwaysOnTop);
        CompanionStateStorage.Save(_controller.ExportState());
    }

    private void ShutdownApp()
    {
        if (_closing) return;
        Close();
        Application.Current.Shutdown();
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closing) return;
        _closing = true;
        CompositionTarget.Rendering -= OnCompositionRendering;

        if (_foregroundHook != 0)
        {
            Win32Interop.UnhookWinEvent(_foregroundHook);
            _foregroundHook = 0;
        }

        CompanionStateStorage.Save(_controller.ExportState());
        _updateService.Dispose();
        _smtcService.Dispose();
        _audioAnalyzer.Dispose();
        _trayService.Dispose();

        try { _menuWindow.Close(); } catch { }
        try { _bubbleWindow.Close(); } catch { }
    }
}
