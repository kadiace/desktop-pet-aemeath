using AemeathCompanion.Native;

namespace AemeathCompanion.Engine;

public enum CompanionMode
{
    A,
    B
}

/// <summary>
/// Core desktop companion state machine (Mode A roaming, Mode B gravity + window platform landing,
/// drag/click/double-click interactions, music playback pose & 5-minute pause hold).
/// All coordinates (X, Y) are in physical screen pixels (Y-down).
/// </summary>
public sealed class CompanionController
{
    public static readonly string[] IdleNames = ["idle1", "idle2", "idle3", "idle4"];
    public static readonly string[] ScreenNames = ["screen1", "screen2", "screen3", "screen4", "screen5", "screen6", "screen7", "screen8"];
    public static readonly string[] ManualNames =
    [
        "idle1", "idle2", "idle3", "idle4",
        "screen1", "screen2", "screen3", "screen4", "screen5", "screen6", "screen7", "screen8"
    ];

    public const double BaseSpriteDip = 200.0;
    public const double BubbleHeightDip = 324.0;
    public const double BubbleGapDip = 14.0;
    public const double BaseWalkSpeedDip = 130.0;
    public const double BaseGravityDip = 1550.0;
    public const double MaxFallSpeedDip = 2200.0;
    public const double PauseReturnSeconds = 5 * 60.0;

    public static (
        double WidthDip,
        double HeightDip,
        double BottomPadDip,
        double FootHalfWidthDip,
        double TopPadDip,
        double LeftPadDip,
        double RightPadDip,
        double VisualBottomPadDip) GetSpriteMetrics(string spriteName)
    {
        if (string.Equals(spriteName, "screen8", StringComparison.OrdinalIgnoreCase))
            return (300.0, 200.0, 15.0, 62.0, 30.0, 30.0, 30.0, 0.0);
        if (string.Equals(spriteName, "screen2", StringComparison.OrdinalIgnoreCase))
            return (200.0, 200.0, 0.0, 42.0, 68.0, 38.0, 34.0, 0.0);
        if (string.Equals(spriteName, "screen4", StringComparison.OrdinalIgnoreCase))
            return (200.0, 200.0, 0.0, 42.0, 72.0, 38.0, 34.0, 0.0);
        if (string.Equals(spriteName, "screen5", StringComparison.OrdinalIgnoreCase))
            return (200.0, 200.0, 4.0, 42.0, 90.0, 38.0, 38.0, 0.0);
        if (string.Equals(spriteName, "screen7", StringComparison.OrdinalIgnoreCase))
            return (200.0, 200.0, 4.0, 42.0, 64.0, 38.0, 34.0, 0.0);
        if (spriteName.StartsWith("screen", StringComparison.OrdinalIgnoreCase))
            return (200.0, 200.0, 4.0, 42.0, 68.0, 38.0, 34.0, 0.0);
        if (string.Equals(spriteName, "aemeath", StringComparison.OrdinalIgnoreCase))
            return (200.0, 200.0, 0.0, 42.0, 25.0, 2.0, 2.0, 23.0);
        if (string.Equals(spriteName, "move", StringComparison.OrdinalIgnoreCase))
            return (200.0, 200.0, 0.0, 42.0, 1.0, 15.0, 15.0, 1.0);
        if (string.Equals(spriteName, "drag", StringComparison.OrdinalIgnoreCase))
            return (200.0, 200.0, 0.0, 42.0, 0.0, 0.0, 0.0, 0.0);
        if (string.Equals(spriteName, "idle1", StringComparison.OrdinalIgnoreCase))
            return (200.0, 200.0, 0.0, 42.0, 13.0, 0.0, 0.0, 0.0);
        if (string.Equals(spriteName, "idle2", StringComparison.OrdinalIgnoreCase))
            return (200.0, 200.0, 0.0, 42.0, 10.0, 1.0, 1.0, 10.0);
        if (string.Equals(spriteName, "idle3", StringComparison.OrdinalIgnoreCase))
            return (200.0, 200.0, 0.0, 42.0, 1.0, 1.0, 1.0, 1.0);
        if (string.Equals(spriteName, "idle4", StringComparison.OrdinalIgnoreCase))
            return (200.0, 200.0, 0.0, 42.0, 1.0, 2.0, 2.0, 0.0);
        return (200.0, 200.0, 0.0, 42.0, 16.0, 0.0, 0.0, 0.0);
    }

    private readonly Random _rng;
    private double _targetX, _targetY;
    private bool _hasTarget;
    private double _actionDelay = 3.0;
    private double _pausedElapsed = -1;
    private bool _platformMiniPlayerHeld;
    private double _platformMiniPauseElapsed = -1;
    private bool _roamMiniPlayerActive;
    private bool _stateDirty;
    private double _saveCooldown;
    private bool _smtcIsPlaying;
    private bool _musicDismissedWhilePlaying;
    private CompanionMode? _modeBeforeMusic;

    // Platform tracking for State B
    private nint _landedHwnd;
    private int _landedPlatformLeft;
    private int _landedPlatformTop;

    public double X { get; private set; }
    public double Y { get; private set; }
    public int Facing { get; private set; } = 1;
    public CompanionMode Mode { get; private set; } = CompanionMode.A;
    public string Action { get; private set; } = "idle";
    public string IdleName { get; private set; } = "idle1";
    public string ScreenName { get; private set; } = "screen1";
    public string? ManualVisual { get; private set; }
    public double SizeMultiplier { get; private set; } = 1.0;
    public bool AlwaysOnTop { get; set; } = true;
    public string? UpdateServerUrl { get; private set; }

    public bool MusicPlaying { get; private set; }
    public bool PlaybackPoseHeld { get; private set; }
    public bool PlatformMiniPlayerHeld => _platformMiniPlayerHeld;
    public bool IsDragging { get; private set; }
    public bool IsFalling { get; private set; }
    public double FallSpeed { get; private set; }
    public bool MenuOpen { get; set; }
    public nint LandedWindowHwnd => _landedHwnd;
    public string LandedPlatformTitle { get; private set; } = string.Empty;

    public event Action<string>? SpriteRestartRequested;

    public CompanionController(Random? rng = null)
    {
        _rng = rng ?? Random.Shared;
    }

    public (
        double WidthDip,
        double HeightDip,
        double BottomPadDip,
        double FootHalfWidthDip,
        double TopPadDip,
        double LeftPadDip,
        double RightPadDip,
        double VisualBottomPadDip) ActiveMetrics
        => GetSpriteMetrics(ActiveSpriteName);

    public double HalfWidthPx(double dpiScale) => Math.Max(16.0, (ActiveMetrics.WidthDip * 0.5) * SizeMultiplier * dpiScale);
    public double HalfHeightPx(double dpiScale) => Math.Max(16.0, (ActiveMetrics.HeightDip * 0.5) * SizeMultiplier * dpiScale);
    public double FootOffsetPx(double dpiScale)
    {
        var m = ActiveMetrics;
        return Math.Max(16.0, (m.HeightDip * 0.5 - m.BottomPadDip) * SizeMultiplier * dpiScale);
    }
    public double VisualLeftOffsetPx(double dpiScale)
    {
        var m = ActiveMetrics;
        double pad = ShouldFlipHorizontal ? m.RightPadDip : m.LeftPadDip;
        return Math.Max(8.0, (m.WidthDip * 0.5 - pad) * SizeMultiplier * dpiScale);
    }
    public double VisualRightOffsetPx(double dpiScale)
    {
        var m = ActiveMetrics;
        double pad = ShouldFlipHorizontal ? m.LeftPadDip : m.RightPadDip;
        return Math.Max(8.0, (m.WidthDip * 0.5 - pad) * SizeMultiplier * dpiScale);
    }
    public double VisualTopOffsetPx(double dpiScale)
    {
        var m = ActiveMetrics;
        return Math.Max(8.0, (m.HeightDip * 0.5 - m.TopPadDip) * SizeMultiplier * dpiScale);
    }
    public double VisualBottomOffsetPx(double dpiScale)
    {
        var m = ActiveMetrics;
        return Math.Max(8.0, (m.HeightDip * 0.5 - m.VisualBottomPadDip) * SizeMultiplier * dpiScale);
    }
    public double HalfSizePx(double dpiScale) => HalfHeightPx(dpiScale);
    public double SpriteSizePx(double dpiScale) => HalfHeightPx(dpiScale) * 2.0;

    public string ActiveSpriteName
    {
        get
        {
            if (MusicPlaying || PlaybackPoseHeld) return "aemeath";
            if (!string.IsNullOrEmpty(ManualVisual)) return ManualVisual;
            if (Mode == CompanionMode.B) return ScreenName;
            if (IsDragging) return "drag";
            return Action == "move" ? "move" : IdleName;
        }
    }

    public bool ShouldShowBubble => MusicPlaying || PlaybackPoseHeld;

    /// <summary>
    /// Compact horizontal mini music player:
    /// visible whenever music is actively playing in Platform mode (including during drags),
    /// or in Roam mode when the user explicitly selected Roam while music was playing,
    /// or held up to 5 minutes after pausing until the user clicks/drags.
    /// </summary>
    public bool ShouldShowPlatformMiniPlayer =>
        !ShouldShowBubble &&
        (_smtcIsPlaying || _platformMiniPlayerHeld) &&
        (Mode == CompanionMode.B || (Mode == CompanionMode.A && _roamMiniPlayerActive));

    public bool ShouldFlipHorizontal
    {
        get
        {
            string active = ActiveSpriteName;
            if (active == "aemeath" || active.StartsWith("screen", StringComparison.Ordinal))
                return false;
            return Facing < 0;
        }
    }

    public void InitializeFromState(CompanionPersistedState saved, IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        Mode = saved.Mode == "B" ? CompanionMode.B : CompanionMode.A;
        SizeMultiplier = Math.Clamp(saved.SizeMultiplier, 0.25, 2.0);
        AlwaysOnTop = saved.AlwaysOnTop;
        UpdateServerUrl = string.IsNullOrWhiteSpace(saved.UpdateServerUrl) ? null : saved.UpdateServerUrl.Trim();
        if (saved.ScreenName is not null && Array.IndexOf(ScreenNames, saved.ScreenName) >= 0)
            ScreenName = saved.ScreenName;

        ManualVisual = saved.ManualVisual is not null && Array.IndexOf(ManualNames, saved.ManualVisual) >= 0
            ? saved.ManualVisual
            : null;

        // Heal any stale state.json where ManualVisual disagrees with Mode or ScreenName
        if (!string.IsNullOrEmpty(ManualVisual))
        {
            if (ManualVisual.StartsWith("screen", StringComparison.Ordinal))
            {
                Mode = CompanionMode.B;
                ScreenName = ManualVisual;
            }
            else if (ManualVisual.StartsWith("idle", StringComparison.Ordinal))
            {
                Mode = CompanionMode.A;
                IdleName = ManualVisual;
            }
        }

        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        bool validPos = double.IsFinite(saved.X) && double.IsFinite(saved.Y) &&
                        monitors.Any(m => m.MonitorRect.Contains(saved.X, saved.Y));

        if (validPos)
        {
            X = saved.X;
            Y = saved.Y;
        }
        else
        {
            X = (primary.WorkRect.Left + primary.WorkRect.Right) * 0.5;
            Y = (primary.WorkRect.Top + primary.WorkRect.Bottom) * 0.5;
        }

        ClampToMonitors(monitors, dpiScale);

        if (Mode == CompanionMode.B)
        {
            IsFalling = true;
            FallSpeed = 0;
            _landedHwnd = 0;
        }
    }

    public CompanionPersistedState ExportState() => new()
    {
        Mode = Mode == CompanionMode.B ? "B" : "A",
        X = Math.Round(X, 1),
        Y = Math.Round(Y, 1),
        SizeMultiplier = Math.Round(SizeMultiplier, 2),
        ManualVisual = ManualVisual,
        ScreenName = ScreenName,
        AlwaysOnTop = AlwaysOnTop,
        UpdateServerUrl = UpdateServerUrl
    };

    public bool ConsumeDirtyFlag(double dt)
    {
        if (!_stateDirty) return false;
        _saveCooldown -= dt;
        if (_saveCooldown > 0) return false;
        _stateDirty = false;
        _saveCooldown = 1.5;
        return true;
    }

    private void MarkDirty()
    {
        _stateDirty = true;
        if (_saveCooldown <= 0) _saveCooldown = 0.6;
    }

    public void SetPosition(double x, double y, IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        X = x;
        Y = y;
        ClampToMonitors(monitors, dpiScale);
    }

    public void SetSizeMultiplier(double value, IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        double clamped = Math.Clamp(Math.Round(value * 100.0) / 100.0, 0.25, 2.0);
        if (Math.Abs(clamped - SizeMultiplier) < 0.001) return;

        double oldFootY = Y + FootOffsetPx(dpiScale);
        SizeMultiplier = clamped;

        // Keep feet planted on the current platform/floor in Platform mode when scaling
        if (Mode == CompanionMode.B && !IsFalling && !IsDragging)
        {
            Y = oldFootY - FootOffsetPx(dpiScale);
        }

        ClampToMonitors(monitors, dpiScale);
        MarkDirty();
    }

    public void DismissMusicMode(IReadOnlyList<MonitorBounds> monitors, double dpiScale, CompanionMode? explicitTargetMode = null)
    {
        bool wasInMusicUi = MusicPlaying || PlaybackPoseHeld;
        if (!wasInMusicUi && explicitTargetMode is null) return;

        if (_smtcIsPlaying)
            _musicDismissedWhilePlaying = true;

        MusicPlaying = false;
        PlaybackPoseHeld = false;
        _pausedElapsed = -1;

        var targetMode = explicitTargetMode ?? _modeBeforeMusic ?? Mode;
        _modeBeforeMusic = null;

        if (targetMode != Mode)
        {
            SetModeInternal(targetMode, monitors, dpiScale);
        }
        else if (wasInMusicUi)
        {
            if (Mode == CompanionMode.B)
            {
                IsFalling = true;
                FallSpeed = 0;
                _landedHwnd = 0;
            }
            else
            {
                SetIdle(change: true);
            }
            ClampToMonitors(monitors, dpiScale);
            MarkDirty();
        }
    }

    public void SetMode(CompanionMode next, IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        if (MusicPlaying || PlaybackPoseHeld)
        {
            bool showMiniInRoam = next == CompanionMode.A && _smtcIsPlaying;
            DismissMusicMode(monitors, dpiScale, explicitTargetMode: next);
            if (showMiniInRoam)
            {
                _roamMiniPlayerActive = true;
                _platformMiniPlayerHeld = false;
                _platformMiniPauseElapsed = -1;
            }
            return;
        }

        // If user explicitly chooses Roam while music is already playing (e.g. Platform -> Roam),
        // respect the user's explicit Roam selection and show the compact horizontal mini player.
        bool keepRoamMini = next == CompanionMode.A && _smtcIsPlaying;
        if (keepRoamMini)
        {
            _musicDismissedWhilePlaying = true;
            _roamMiniPlayerActive = true;
            _platformMiniPlayerHeld = false;
            _platformMiniPauseElapsed = -1;
        }

        if (next == Mode) return;
        SetModeInternal(next, monitors, dpiScale);
        if (keepRoamMini)
            _roamMiniPlayerActive = true;
    }

    private void SetModeInternal(CompanionMode next, IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        Mode = next;
        ManualVisual = null;
        MenuOpen = false;
        IsDragging = false;
        _hasTarget = false;
        _landedHwnd = 0;
        LandedPlatformTitle = string.Empty;
        _roamMiniPlayerActive = false;

        if (Mode == CompanionMode.B)
        {
            IsFalling = true;
            FallSpeed = 0;
            Action = "idle";
            ChangeScreen();
            if (_smtcIsPlaying)
            {
                _platformMiniPlayerHeld = false;
                _platformMiniPauseElapsed = -1;
            }
        }
        else
        {
            IsFalling = false;
            FallSpeed = 0;
            _platformMiniPlayerHeld = false;
            _platformMiniPauseElapsed = -1;
            SetIdle(change: true);
        }

        ClampToMonitors(monitors, dpiScale);
        MarkDirty();
    }

    public void SelectManualVisual(string? visualName, IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        if (MusicPlaying || PlaybackPoseHeld)
        {
            var desiredMode = !string.IsNullOrEmpty(visualName) && visualName.StartsWith("screen", StringComparison.Ordinal)
                ? CompanionMode.B
                : CompanionMode.A;
            DismissMusicMode(monitors, dpiScale, explicitTargetMode: desiredMode);
        }

        if (string.IsNullOrEmpty(visualName))
        {
            ManualVisual = null;
            MarkDirty();
            return;
        }

        if (Array.IndexOf(ManualNames, visualName) < 0) return;

        if (visualName.StartsWith("screen", StringComparison.Ordinal))
        {
            bool wasRestingPlatform = Mode == CompanionMode.B && !IsFalling && !IsDragging;
            double oldFootY = Y + FootOffsetPx(dpiScale);
            if (Mode == CompanionMode.A)
                SetModeInternal(CompanionMode.B, monitors, dpiScale);
            ScreenName = visualName;
            ManualVisual = visualName;
            if (wasRestingPlatform)
            {
                Y = oldFootY - FootOffsetPx(dpiScale);
            }
            else
            {
                IsFalling = true;
                FallSpeed = 0;
                _landedHwnd = 0;
            }
            SpriteRestartRequested?.Invoke(ScreenName);
        }
        else
        {
            if (_smtcIsPlaying)
                _musicDismissedWhilePlaying = true;
            if (Mode == CompanionMode.B)
                SetModeInternal(CompanionMode.A, monitors, dpiScale);
            IdleName = visualName;
            SetIdle(change: false);
            ManualVisual = visualName;
        }

        ClampToMonitors(monitors, dpiScale);
        MarkDirty();
    }

    public void BeginDrag(IReadOnlyList<MonitorBounds>? monitors = null, double dpiScale = 1.0)
    {
        // While music is actively playing in Music mode, dragging moves Aemeath directly in Music mode
        // (keeping the speech bubble and aemeath sprite intact without switching to Platform or Roam).
        // When music is paused in Music mode (5-minute hold), starting a drag immediately dismisses the hold to Roam.
        if (!MusicPlaying && PlaybackPoseHeld)
        {
            PlaybackPoseHeld = false;
            _pausedElapsed = -1;
            _modeBeforeMusic = null;
            Mode = CompanionMode.A;
            if (monitors is not null) ClampToMonitors(monitors, dpiScale);
            MarkDirty();
        }

        // If music is paused while the mini player is held (Platform or Roam), starting a drag immediately hides it.
        // While music is actively playing, the mini player stays visible during the drag.
        if (!_smtcIsPlaying && _platformMiniPlayerHeld)
        {
            _platformMiniPlayerHeld = false;
            _platformMiniPauseElapsed = -1;
            if (Mode == CompanionMode.A)
                _roamMiniPlayerActive = false;
        }

        IsDragging = true;
        ManualVisual = null;
        if (!MusicPlaying)
            Action = "drag";
        _hasTarget = false;
        IsFalling = false;
        FallSpeed = 0;
        _landedHwnd = 0;
        LandedPlatformTitle = string.Empty;
    }

    public void UpdateDrag(double newCenterX, double newCenterY, IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        if (!IsDragging) return;
        double oldX = X;
        X = newCenterX;
        Y = newCenterY;
        ClampToMonitors(monitors, dpiScale);
        if (Math.Abs(X - oldX) > 0.5)
            Facing = X > oldX ? 1 : -1;
    }

    public void EndDrag(IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        if (!IsDragging) return;
        IsDragging = false;
        ManualVisual = null;
        ClampToMonitors(monitors, dpiScale);

        if (MusicPlaying)
        {
            MarkDirty();
            return;
        }

        if (Mode == CompanionMode.B)
        {
            IsFalling = true;
            FallSpeed = 0;
            _landedHwnd = 0;
        }
        else
        {
            SetIdle(change: true);
            _actionDelay = RandomRange(2.5, 4.0);
        }
        MarkDirty();
    }

    public void HandleSingleClick(IReadOnlyList<MonitorBounds>? monitors = null, double dpiScale = 1.0)
    {
        // While music is actively playing in Music mode, single-click only plays click feedback (Boing)
        // and keeps Music mode — double-click is required to switch to Platform mode.
        if (MusicPlaying)
            return;

        // While paused in Music mode (5-minute hold), single-click dismisses the pause hold back to Roam.
        if (PlaybackPoseHeld)
        {
            if (monitors is not null)
                DismissMusicMode(monitors, dpiScale, explicitTargetMode: CompanionMode.A);
            else
            {
                PlaybackPoseHeld = false;
                _pausedElapsed = -1;
                _modeBeforeMusic = null;
                Mode = CompanionMode.A;
                SetIdle(change: true);
                MarkDirty();
            }
            return;
        }

        ManualVisual = null;
        // When paused while mini player is held (Platform or Roam), clicking hides the mini player immediately;
        // when actively playing, clicking only switches the sprite and keeps the mini player visible.
        if (!_smtcIsPlaying && _platformMiniPlayerHeld)
        {
            _platformMiniPlayerHeld = false;
            _platformMiniPauseElapsed = -1;
            if (Mode == CompanionMode.A)
                _roamMiniPlayerActive = false;
        }

        if (Mode == CompanionMode.B)
        {
            bool wasResting = !IsFalling && !IsDragging;
            double oldFootY = Y + FootOffsetPx(dpiScale);
            ChangeScreen();
            if (wasResting)
            {
                Y = oldFootY - FootOffsetPx(dpiScale);
                if (monitors is not null) ClampHorizontalToMonitors(monitors, dpiScale);
            }
        }
        else
        {
            SetIdle(change: true);
        }
    }

    public void HandleDoubleClick(IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        // From Music mode, double-click switches cleanly to Platform mode (showing mini player if playing).
        if (MusicPlaying || PlaybackPoseHeld)
        {
            DismissMusicMode(monitors, dpiScale, explicitTargetMode: CompanionMode.B);
            return;
        }

        // In Platform mode while music is actively playing, double-click expands into full Music mode.
        if (Mode == CompanionMode.B && _smtcIsPlaying)
        {
            EnterMusicMode(monitors, dpiScale);
            return;
        }

        SetMode(Mode == CompanionMode.A ? CompanionMode.B : CompanionMode.A, monitors, dpiScale);
    }

    public void EnterMusicMode(IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        _musicDismissedWhilePlaying = false;
        _roamMiniPlayerActive = false;
        _platformMiniPlayerHeld = false;
        _platformMiniPauseElapsed = -1;
        _pausedElapsed = _smtcIsPlaying ? -1 : 0;
        MenuOpen = false;
        ManualVisual = null;
        IsDragging = false;
        _hasTarget = false;
        _landedHwnd = 0;
        LandedPlatformTitle = string.Empty;

        if (!MusicPlaying && !PlaybackPoseHeld)
            _modeBeforeMusic = Mode;

        if (Mode == CompanionMode.B)
            SetModeInternal(CompanionMode.A, monitors, dpiScale);

        MusicPlaying = _smtcIsPlaying;
        PlaybackPoseHeld = !_smtcIsPlaying;
        SetIdle(change: false);
        ClampToMonitors(monitors, dpiScale);
        MarkDirty();
    }

    public void UpdateMusicState(bool isMusicPlaying, IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        bool wasSmtcPlaying = _smtcIsPlaying;
        _smtcIsPlaying = isMusicPlaying;

        // User actions always take priority: never hijack Platform mode or an active drag into full Music mode
        if ((Mode == CompanionMode.B || IsDragging) && !MusicPlaying && !PlaybackPoseHeld)
        {
            if (Mode == CompanionMode.B || _roamMiniPlayerActive)
            {
                if (isMusicPlaying)
                {
                    _platformMiniPlayerHeld = false;
                    _platformMiniPauseElapsed = -1;
                }
                else if (wasSmtcPlaying)
                {
                    // Music paused while mini player is visible:
                    // if currently dragging, hide immediately; otherwise hold for 5 minutes until click/drag.
                    if (IsDragging)
                    {
                        _platformMiniPlayerHeld = false;
                        _platformMiniPauseElapsed = -1;
                        if (Mode == CompanionMode.A)
                            _roamMiniPlayerActive = false;
                    }
                    else
                    {
                        _platformMiniPlayerHeld = true;
                        _platformMiniPauseElapsed = 0.0;
                    }
                }
            }

            if (!isMusicPlaying) _musicDismissedWhilePlaying = false;
            return;
        }

        // Music paused while Roam mini player is active (not dragging): hold mini player for 5 minutes
        if (Mode == CompanionMode.A && _roamMiniPlayerActive && !MusicPlaying && !PlaybackPoseHeld && !isMusicPlaying)
        {
            if (wasSmtcPlaying)
            {
                _platformMiniPlayerHeld = true;
                _platformMiniPauseElapsed = 0.0;
            }
            _musicDismissedWhilePlaying = false;
            return;
        }

        // If user manually dismissed music mode while the track kept playing,
        // keep staying in normal mode until playback stops/pauses and starts again.
        if (!isMusicPlaying)
        {
            _musicDismissedWhilePlaying = false;
        }
        else if (_musicDismissedWhilePlaying)
        {
            if (!wasSmtcPlaying)
                _musicDismissedWhilePlaying = false;
            else
                return;
        }

        if (isMusicPlaying == MusicPlaying) return;
        MusicPlaying = isMusicPlaying;

        if (isMusicPlaying)
        {
            _pausedElapsed = -1;
            PlaybackPoseHeld = false;
            _roamMiniPlayerActive = false;
            _platformMiniPlayerHeld = false;
            _platformMiniPauseElapsed = -1;
            MenuOpen = false;
            ManualVisual = null;
            _modeBeforeMusic ??= Mode;
            if (Mode == CompanionMode.B)
                SetModeInternal(CompanionMode.A, monitors, dpiScale);
            IsDragging = false;
            SetIdle(change: false);
            ClampToMonitors(monitors, dpiScale);
            MarkDirty();
        }
        else
        {
            if (IsDragging)
            {
                _pausedElapsed = -1;
                PlaybackPoseHeld = false;
                _modeBeforeMusic = null;
                Mode = CompanionMode.A;
                Action = "drag";
            }
            else
            {
                _pausedElapsed = 0;
                PlaybackPoseHeld = true;
            }
            MarkDirty();
        }
    }

    public void Tick(
        double dt,
        IReadOnlyList<MonitorBounds> monitors,
        IReadOnlyList<PlatformSurface> platforms,
        NativePoint? cursorPx,
        double dpiScale)
    {
        dt = Math.Clamp(dt, 0.0, 0.05);

        // 5-minute pause hold timeout (matches companion.js PAUSE_RETURN_MS)
        if (_pausedElapsed >= 0 && !MusicPlaying)
        {
            _pausedElapsed += dt;
            if (_pausedElapsed >= PauseReturnSeconds)
            {
                _pausedElapsed = -1;
                PlaybackPoseHeld = false;
                _modeBeforeMusic = null;
                if (Mode == CompanionMode.B)
                    SetModeInternal(CompanionMode.A, monitors, dpiScale);
                else
                {
                    ManualVisual = null;
                    SetIdle(change: true);
                    MarkDirty();
                }
            }
        }

        // 5-minute pause hold timeout for horizontal mini music player (Platform or Roam)
        if (_platformMiniPlayerHeld && !_smtcIsPlaying && _platformMiniPauseElapsed >= 0)
        {
            _platformMiniPauseElapsed += dt;
            if (_platformMiniPauseElapsed >= PauseReturnSeconds)
            {
                _platformMiniPlayerHeld = false;
                _platformMiniPauseElapsed = -1;
                _roamMiniPlayerActive = false;
            }
        }

        if (Mode == CompanionMode.A)
            StepA(dt, monitors, cursorPx, dpiScale);
        else
            StepB(dt, monitors, platforms, dpiScale);
    }

    private void StepA(double dt, IReadOnlyList<MonitorBounds> monitors, NativePoint? cursorPx, double dpiScale)
    {
        if (IsDragging || MusicPlaying || PlaybackPoseHeld || MenuOpen || !string.IsNullOrEmpty(ManualVisual))
            return;

        if (Action == "move" && _hasTarget)
        {
            double dx = _targetX - X;
            double dy = _targetY - Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double step = BaseWalkSpeedDip * dpiScale * dt;

            if (dist <= step || dist < 1.0)
            {
                X = _targetX;
                Y = _targetY;
                SetIdle(change: true);
            }
            else
            {
                X += dx / dist * step;
                Y += dy / dist * step;
                if (Math.Abs(dx) > 0.2) Facing = dx > 0 ? 1 : -1;
            }
        }
        else
        {
            _actionDelay -= dt;
            if (_actionDelay <= 0)
            {
                if (_rng.NextDouble() < 0.70)
                    BeginWalk(monitors, cursorPx, dpiScale);
                else
                    SetIdle(change: true);
            }
        }

        ClampToMonitors(monitors, dpiScale);
    }

    private void StepB(double dt, IReadOnlyList<MonitorBounds> monitors, IReadOnlyList<PlatformSurface> platforms, double dpiScale)
    {
        if (IsDragging)
        {
            IsFalling = false;
            FallSpeed = 0;
            return;
        }

        double footOffset = FootOffsetPx(dpiScale);
        double footHalfW = ActiveMetrics.FootHalfWidthDip * SizeMultiplier * dpiScale;
        double minOverlap = Math.Min(26.0 * SizeMultiplier * dpiScale, footHalfW);

        if (IsFalling)
        {
            double prevBottom = Y + footOffset;
            FallSpeed = Math.Min(MaxFallSpeedDip * dpiScale, FallSpeed + BaseGravityDip * dpiScale * dt);
            double nextBottom = prevBottom + FallSpeed * dt;

            double footLeft = X - footHalfW;
            double footRight = X + footHalfW;

            PlatformSurface? landed = null;
            int bestTop = int.MaxValue;

            foreach (var p in platforms)
            {
                // Platform must be vertically crossed (or just touched) during this step
                if (p.Top < prevBottom - 10 || p.Top > nextBottom + 2)
                    continue;

                double overlap = Math.Min(footRight, p.Right) - Math.Max(footLeft, p.Left);
                double required = p.IsWorkAreaFloor ? 1.0 : Math.Min(minOverlap, (p.Right - p.Left) * 0.5);
                if (overlap < required)
                    continue;

                if (p.Top < bestTop)
                {
                    bestTop = p.Top;
                    landed = p;
                }
            }

            if (landed is not null)
            {
                Y = landed.Top - footOffset;
                IsFalling = false;
                FallSpeed = 0;
                _landedHwnd = landed.IsWorkAreaFloor ? 0 : landed.Hwnd;
                _landedPlatformLeft = landed.Left;
                _landedPlatformTop = landed.Top;
                LandedPlatformTitle = landed.Title;
                MarkDirty();
            }
            else
            {
                Y = nextBottom - footOffset;
                // Safety catch against current monitor work floor
                var mon = Win32Interop.FindMonitorForPoint(monitors, X, Y);
                if (Y + footOffset >= mon.WorkRect.Bottom)
                {
                    Y = mon.WorkRect.Bottom - footOffset;
                    IsFalling = false;
                    FallSpeed = 0;
                    _landedHwnd = 0;
                    LandedPlatformTitle = "Taskbar Floor";
                    MarkDirty();
                }
            }
        }
        else
        {
            // Validate current resting platform!
            if (_landedHwnd != 0)
            {
                // Find any visible segment of the window we are standing on
                PlatformSurface? currentSeg = null;
                double bestOverlap = -1;
                double footLeft = X - footHalfW;
                double footRight = X + footHalfW;

                foreach (var p in platforms)
                {
                    if (p.Hwnd != _landedHwnd) continue;
                    // Account for window being dragged horizontally: estimate shifted X first
                    int dxWin = p.Left - _landedPlatformLeft;
                    double shiftedFootLeft = footLeft + dxWin;
                    double shiftedFootRight = footRight + dxWin;
                    double ov = Math.Min(shiftedFootRight, p.Right) - Math.Max(shiftedFootLeft, p.Left);
                    if (ov > bestOverlap)
                    {
                        bestOverlap = ov;
                        currentSeg = p;
                    }
                }

                double req = Math.Min(minOverlap, 24.0 * dpiScale);
                if (currentSeg is not null && bestOverlap >= req && Math.Abs(currentSeg.Top - _landedPlatformTop) <= 450)
                {
                    // Ride along smoothly if the user moves the window!
                    int dxWin = currentSeg.Left - _landedPlatformLeft;
                    X += dxWin;
                    Y = currentSeg.Top - footOffset;
                    _landedPlatformLeft = currentSeg.Left;
                    _landedPlatformTop = currentSeg.Top;
                    LandedPlatformTitle = currentSeg.Title;
                }
                else
                {
                    // Window closed, minimized, moved away, or occluded -> resume falling!
                    _landedHwnd = 0;
                    LandedPlatformTitle = string.Empty;
                    IsFalling = true;
                    FallSpeed = 0;
                }
            }
            else
            {
                // Resting on taskbar floor — check if monitor/taskbar changed
                var mon = Win32Interop.FindMonitorForPoint(monitors, X, Y);
                double floorY = mon.WorkRect.Bottom - footOffset;
                if (Math.Abs(Y - floorY) > 4)
                {
                    IsFalling = true;
                    FallSpeed = 0;
                }
                else
                {
                    Y = floorY;
                }
            }
        }

        ClampHorizontalToMonitors(monitors, dpiScale);
        // Platform mode intentionally keeps the current screen sprite without time-based random switching.
    }

    private void BeginWalk(IReadOnlyList<MonitorBounds> monitors, NativePoint? cursorPx, double dpiScale)
    {
        var mon = Win32Interop.FindMonitorForPoint(monitors, X, Y);
        var (minX, maxX, minY, maxY) = GetMovementBounds(mon, dpiScale);

        double tx = RandomRange(minX, maxX);
        double ty = RandomRange(minY, maxY);

        if (cursorPx.HasValue && _rng.NextDouble() < 0.25)
        {
            double dx = cursorPx.Value.X - X;
            double dy = cursorPx.Value.Y - Y;
            double radius = 260.0 * dpiScale;
            if (dx * dx + dy * dy < radius * radius)
            {
                tx = Math.Clamp(cursorPx.Value.X, minX, maxX);
                ty = Math.Clamp(cursorPx.Value.Y, minY, maxY);
            }
        }

        if (Math.Abs(tx - X) < 16 * dpiScale && Math.Abs(ty - Y) < 16 * dpiScale)
        {
            SetIdle(change: true);
            return;
        }

        _targetX = tx;
        _targetY = ty;
        _hasTarget = true;
        Action = "move";
    }

    private void SetIdle(bool change)
    {
        Action = "idle";
        _hasTarget = false;
        if (change) IdleName = ChooseOther(IdleNames, IdleName);
        _actionDelay = RandomRange(2.5, 6.0);
    }

    private void ChangeScreen()
    {
        ScreenName = ChooseOther(ScreenNames, ScreenName);
        SpriteRestartRequested?.Invoke(ScreenName);
    }

    private string ChooseOther(string[] pool, string current)
    {
        int idx = _rng.Next(pool.Length - 1);
        int seen = 0;
        foreach (string item in pool)
        {
            if (item == current) continue;
            if (seen == idx) return item;
            seen++;
        }
        return pool[0];
    }

    private (double MinX, double MaxX, double MinY, double MaxY) GetMovementBounds(MonitorBounds mon, double dpiScale, bool useFullMonitor = false)
    {
        double leftOff = VisualLeftOffsetPx(dpiScale);
        double rightOff = VisualRightOffsetPx(dpiScale);
        double topOff = VisualTopOffsetPx(dpiScale);
        double bottomOff = Mode == CompanionMode.B ? FootOffsetPx(dpiScale) : VisualBottomOffsetPx(dpiScale);
        double extraTop = ShouldShowBubble ? (BubbleHeightDip + BubbleGapDip + 16.0) * dpiScale : 0.0;

        var rect = useFullMonitor ? mon.MonitorRect : mon.WorkRect;
        double minX = rect.Left + leftOff;
        double maxX = Math.Max(minX, rect.Right - rightOff);
        double minY = rect.Top + topOff + extraTop;
        double bottomEdge = Mode == CompanionMode.B ? mon.WorkRect.Bottom : rect.Bottom;
        double maxY = Math.Max(minY, bottomEdge - bottomOff);
        return (minX, maxX, minY, maxY);
    }

    private static (int Left, int Right) GetHorizontalSpanAtY(IReadOnlyList<MonitorBounds> monitors, double y, MonitorBounds fallback)
    {
        double checkY = Math.Clamp(y, fallback.MonitorRect.Top, fallback.MonitorRect.Bottom);
        int minLeft = fallback.MonitorRect.Left;
        int maxRight = fallback.MonitorRect.Right;
        foreach (var m in monitors)
        {
            if (checkY >= m.MonitorRect.Top && checkY <= m.MonitorRect.Bottom)
            {
                if (m.MonitorRect.Left < minLeft) minLeft = m.MonitorRect.Left;
                if (m.MonitorRect.Right > maxRight) maxRight = m.MonitorRect.Right;
            }
        }
        return (minLeft, maxRight);
    }

    private void ClampToMonitors(IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        var mon = Win32Interop.FindMonitorForPoint(monitors, X, Y);
        var (spanLeft, spanRight) = GetHorizontalSpanAtY(monitors, Y, mon);
        double leftOff = VisualLeftOffsetPx(dpiScale);
        double rightOff = VisualRightOffsetPx(dpiScale);
        X = Math.Clamp(X, spanLeft + leftOff, Math.Max(spanLeft + leftOff, spanRight - rightOff));

        // Re-resolve monitor after horizontal clamp (in case X crossed to an adjacent monitor)
        mon = Win32Interop.FindMonitorForPoint(monitors, X, Y);
        var (_, _, minY, maxY) = GetMovementBounds(mon, dpiScale, useFullMonitor: true);
        Y = Math.Clamp(Y, minY, maxY);
    }

    private void ClampHorizontalToMonitors(IReadOnlyList<MonitorBounds> monitors, double dpiScale)
    {
        var mon = Win32Interop.FindMonitorForPoint(monitors, X, Y);
        var (spanLeft, spanRight) = GetHorizontalSpanAtY(monitors, Y, mon);
        double leftOff = VisualLeftOffsetPx(dpiScale);
        double rightOff = VisualRightOffsetPx(dpiScale);
        X = Math.Clamp(X, spanLeft + leftOff, Math.Max(spanLeft + leftOff, spanRight - rightOff));
    }

    private double RandomRange(double min, double max) => min + _rng.NextDouble() * (max - min);
}
