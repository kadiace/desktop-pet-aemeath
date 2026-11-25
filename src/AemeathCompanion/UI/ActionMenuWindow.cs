using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AemeathCompanion.Engine;
using AemeathCompanion.Native;

namespace AemeathCompanion.UI;

/// <summary>
/// Pixel-styled floating context menu in Aemeath's signature palette.
/// Lets the user switch Mode A/B, preview poses, adjust 0.25x-2.00x scale,
/// inspect SMTC music filter status, toggle Always-On-Top, or exit.
/// </summary>
public sealed class ActionMenuWindow : Window
{
    private readonly TextBlock _modeBadge;
    private readonly TextBlock _mediaTitleText;
    private readonly TextBlock _mediaArtistText;
    private readonly TextBlock _mediaTimeText;
    private readonly TextBlock _scaleLabel;
    private readonly Slider _scaleSlider;
    private readonly Button _btnModeA;
    private readonly Button _btnModeB;
    private readonly Button _btnMusicMode;
    private readonly Button _btnTopmost;
    private readonly Button _btnMediaPrev;
    private readonly Button _btnMediaPlayPause;
    private readonly Button _btnMediaNext;
    private readonly Dictionary<string, Button> _poseButtons = new(StringComparer.OrdinalIgnoreCase);

    private bool _suppressSlider;
    private nint _hwnd;

    public event Action<CompanionMode>? ModeSelected;
    public event Action? MusicModeSelected;
    public event Action<string?>? VisualSelected;
    public event Action<double>? ScaleChanged;
    public event Action? AlwaysOnTopToggled;
    public event Action? ResetPositionRequested;
    public event Action? MediaPreviousRequested;
    public event Action? MediaPlayPauseRequested;
    public event Action? MediaNextRequested;
    public event Action? ExitRequested;

    public ActionMenuWindow()
    {
        Title = "Aemeath Action Menu";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        var outerBorder = new Border
        {
            Background = PixelPalette.OutlineDarkBrush,
            Padding = new Thickness(2),
            Width = 266
        };

        var blushBorder = new Border
        {
            Background = PixelPalette.BlushLightBrush,
            Padding = new Thickness(2)
        };

        var roseBorder = new Border
        {
            Background = PixelPalette.BerryRoseBrush,
            Padding = new Thickness(2)
        };

        var innerPanel = new Border
        {
            Background = PixelPalette.BasePlumBrush,
            Padding = new Thickness(10, 9, 10, 10)
        };

        var rootStack = new StackPanel();

        // Header bar
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = "✦ AEMEATH // CONTROL",
            FontFamily = PixelPalette.PretendardFamily,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = PixelPalette.GhostCyanBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerGrid.Children.Add(titleText);

        _modeBadge = new TextBlock
        {
            Text = "ROAM",
            FontFamily = PixelPalette.PretendardFamily,
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = PixelPalette.BlushGlowBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_modeBadge, 1);
        headerGrid.Children.Add(_modeBadge);
        rootStack.Children.Add(headerGrid);

        // Divider
        rootStack.Children.Add(MakeDivider());

        // Top mode selector row: ROAM | PLATFORM | ♪ MUSIC
        var modeGrid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });

        _btnModeA = CreatePixelButton("ROAM", compact: true);
        _btnModeA.Click += (_, _) => { ModeSelected?.Invoke(CompanionMode.A); Hide(); };
        modeGrid.Children.Add(_btnModeA);

        _btnModeB = CreatePixelButton("PLATFORM", compact: true);
        _btnModeB.Click += (_, _) => { ModeSelected?.Invoke(CompanionMode.B); Hide(); };
        Grid.SetColumn(_btnModeB, 2);
        modeGrid.Children.Add(_btnModeB);

        _btnMusicMode = CreatePixelButton("♪ MUSIC", compact: true);
        _btnMusicMode.Click += (_, _) => { MusicModeSelected?.Invoke(); Hide(); };
        Grid.SetColumn(_btnMusicMode, 4);
        modeGrid.Children.Add(_btnMusicMode);
        rootStack.Children.Add(modeGrid);

        // Clean media playback panel (title, artist, elapsed / duration, transport controls)
        var statusBox = new Border
        {
            Background = PixelPalette.PanelPlumBrush,
            BorderBrush = PixelPalette.PlumBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 6, 7, 6),
            Margin = new Thickness(0, 0, 0, 7)
        };
        var statusStack = new StackPanel();
        _mediaTitleText = new TextBlock
        {
            Text = "재생 중인 음악 없음",
            FontFamily = PixelPalette.PretendardFamily,
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = PixelPalette.CreamWhiteBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _mediaArtistText = new TextBlock
        {
            Text = "",
            FontFamily = PixelPalette.PretendardFamily,
            FontSize = 10,
            Foreground = PixelPalette.BlushLightBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0)
        };
        _mediaTimeText = new TextBlock
        {
            Text = "--:-- / --:--",
            FontFamily = PixelPalette.PretendardFamily,
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = PixelPalette.GhostCyanBrush,
            Margin = new Thickness(0, 3, 0, 0)
        };
        statusStack.Children.Add(_mediaTitleText);
        statusStack.Children.Add(_mediaArtistText);
        statusStack.Children.Add(_mediaTimeText);

        var mediaCtrlGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        mediaCtrlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mediaCtrlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        mediaCtrlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        mediaCtrlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        mediaCtrlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _btnMediaPrev = CreatePixelButton("⏮ PREV", compact: true);
        _btnMediaPrev.Click += (_, _) => MediaPreviousRequested?.Invoke();
        mediaCtrlGrid.Children.Add(_btnMediaPrev);

        _btnMediaPlayPause = CreatePixelButton("▶ PLAY", compact: true);
        _btnMediaPlayPause.Click += (_, _) => MediaPlayPauseRequested?.Invoke();
        Grid.SetColumn(_btnMediaPlayPause, 2);
        mediaCtrlGrid.Children.Add(_btnMediaPlayPause);

        _btnMediaNext = CreatePixelButton("NEXT ⏭", compact: true);
        _btnMediaNext.Click += (_, _) => MediaNextRequested?.Invoke();
        Grid.SetColumn(_btnMediaNext, 4);
        mediaCtrlGrid.Children.Add(_btnMediaNext);

        statusStack.Children.Add(mediaCtrlGrid);
        statusBox.Child = statusStack;
        rootStack.Children.Add(statusBox);

        // Manual visual poses section
        var poseHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        poseHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        poseHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        poseHeader.Children.Add(new TextBlock
        {
            Text = "ANIMATION / POSE",
            FontFamily = PixelPalette.PretendardFamily,
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = PixelPalette.SakuraPinkBrush,
            VerticalAlignment = VerticalAlignment.Center
        });

        var autoBtn = CreatePixelButton("AUTO", compact: true);
        autoBtn.Click += (_, _) => { VisualSelected?.Invoke(null); Hide(); };
        Grid.SetColumn(autoBtn, 1);
        poseHeader.Children.Add(autoBtn);
        rootStack.Children.Add(poseHeader);

        var poseGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
        foreach (string name in CompanionController.ManualNames)
        {
            var btn = CreatePixelButton(name, compact: true);
            btn.Margin = new Thickness(1.5);
            btn.Click += (_, _) => { VisualSelected?.Invoke(name); Hide(); };
            _poseButtons[name] = btn;
            poseGrid.Children.Add(btn);
        }
        rootStack.Children.Add(poseGrid);

        rootStack.Children.Add(MakeDivider());

        // Scale slider section
        var scaleHeader = new Grid { Margin = new Thickness(0, 6, 0, 2) };
        scaleHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scaleHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _scaleLabel = new TextBlock
        {
            Text = "SCALE 1.00x",
            FontFamily = PixelPalette.PretendardFamily,
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = PixelPalette.CreamWhiteBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        scaleHeader.Children.Add(_scaleLabel);

        var resetScaleBtn = CreatePixelButton("1.00x", compact: true);
        resetScaleBtn.Click += (_, _) =>
        {
            if (_scaleSlider is not null) _scaleSlider.Value = 1.0;
        };
        Grid.SetColumn(resetScaleBtn, 1);
        scaleHeader.Children.Add(resetScaleBtn);
        rootStack.Children.Add(scaleHeader);

        _scaleSlider = new Slider
        {
            Minimum = 0.25,
            Maximum = 2.0,
            Value = 1.0,
            SmallChange = 0.05,
            LargeChange = 0.25,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 2, 0, 8),
            Foreground = PixelPalette.GhostCyanBrush
        };
        _scaleSlider.ValueChanged += (_, e) =>
        {
            if (_suppressSlider) return;
            _scaleLabel.Text = $"SCALE {e.NewValue:0.00}x";
            ScaleChanged?.Invoke(e.NewValue);
        };
        rootStack.Children.Add(_scaleSlider);

        rootStack.Children.Add(MakeDivider());

        // Footer actions
        var footerGrid = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _btnTopmost = CreatePixelButton("TOP: ON", compact: true);
        _btnTopmost.Click += (_, _) => AlwaysOnTopToggled?.Invoke();
        footerGrid.Children.Add(_btnTopmost);

        var centerBtn = CreatePixelButton("CENTER", compact: true);
        centerBtn.Click += (_, _) => { ResetPositionRequested?.Invoke(); Hide(); };
        Grid.SetColumn(centerBtn, 2);
        footerGrid.Children.Add(centerBtn);

        var exitBtn = CreatePixelButton("EXIT", compact: true, danger: true);
        exitBtn.Click += (_, _) => ExitRequested?.Invoke();
        Grid.SetColumn(exitBtn, 4);
        footerGrid.Children.Add(exitBtn);

        rootStack.Children.Add(footerGrid);

        innerPanel.Child = rootStack;
        roseBorder.Child = innerPanel;
        blushBorder.Child = roseBorder;
        outerBorder.Child = blushBorder;
        Content = outerBorder;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32Interop.ConfigureOverlayWindow(_hwnd, clickThrough: false, noActivate: false);
        };

        Deactivated += (_, _) =>
        {
            if (IsVisible) Hide();
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Hide();
        };
    }

    public void ShowAt(
        double companionCenterX,
        double companionCenterY,
        double companionHalfPx,
        IReadOnlyList<MonitorBounds> monitors,
        CompanionController controller,
        AemeathCompanion.Media.MediaSnapshot media)
    {
        SyncUi(controller, media);

        if (!IsVisible) Show();
        UpdateLayout();

        double dpiScale = _hwnd != 0 ? Win32Interop.GetWindowDpiScale(_hwnd) : 1.0;
        double menuWpx = ActualWidth * dpiScale;
        double menuHpx = ActualHeight * dpiScale;

        var mon = Win32Interop.FindMonitorForPoint(monitors, companionCenterX, companionCenterY);
        double preferredLeft = companionCenterX + companionHalfPx + 14 * dpiScale;
        if (preferredLeft + menuWpx > mon.WorkRect.Right - 8 * dpiScale)
        {
            preferredLeft = companionCenterX - companionHalfPx - menuWpx - 14 * dpiScale;
        }

        double leftPx = Math.Clamp(preferredLeft, mon.WorkRect.Left + 8 * dpiScale,
            Math.Max(mon.WorkRect.Left + 8 * dpiScale, mon.WorkRect.Right - menuWpx - 8 * dpiScale));
        double topPx = Math.Clamp(companionCenterY - menuHpx * 0.5, mon.WorkRect.Top + 8 * dpiScale,
            Math.Max(mon.WorkRect.Top + 8 * dpiScale, mon.WorkRect.Bottom - menuHpx - 8 * dpiScale));

        if (_hwnd != 0)
        {
            Win32Interop.MoveWindowPhysical(_hwnd, (int)Math.Round(leftPx), (int)Math.Round(topPx), enforceTopmost: true);
        }

        Activate();
    }

    public void SyncUi(CompanionController controller, AemeathCompanion.Media.MediaSnapshot media)
    {
        bool inMusicMode = controller.MusicPlaying || controller.PlaybackPoseHeld;
        _modeBadge.Text = inMusicMode
            ? "♪ MUSIC"
            : controller.Mode == CompanionMode.B ? "PLATFORM" : "ROAM";

        if (media.HasMusicSession)
        {
            _mediaTitleText.Text = string.IsNullOrWhiteSpace(media.Title) ? "Unknown track" : media.Title;
            _mediaArtistText.Text = string.IsNullOrWhiteSpace(media.Artist) ? "" : media.Artist;
            _mediaArtistText.Visibility = string.IsNullOrWhiteSpace(media.Artist) ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            _mediaTitleText.Text = "재생 중인 음악 없음";
            _mediaArtistText.Text = "";
            _mediaArtistText.Visibility = Visibility.Collapsed;
        }

        UpdatePlaybackTime(media);

        _btnMediaPrev.IsEnabled = media.HasMusicSession && media.CanSkipPrevious;
        _btnMediaPlayPause.IsEnabled = media.HasMusicSession && media.CanPlayPause;
        _btnMediaNext.IsEnabled = media.HasMusicSession && media.CanSkipNext;
        _btnMediaPlayPause.Content = media.IsMusicPlaying ? "❚❚ PAUSE" : "▶ PLAY";
        StyleToggleButton(_btnMediaPlayPause, media.IsMusicPlaying);

        StyleToggleButton(_btnModeA, !inMusicMode && controller.Mode == CompanionMode.A);
        StyleToggleButton(_btnModeB, !inMusicMode && controller.Mode == CompanionMode.B);
        StyleToggleButton(_btnMusicMode, inMusicMode);
        _btnModeA.IsEnabled = true;
        _btnModeB.IsEnabled = true;
        _btnMusicMode.IsEnabled = true;

        foreach (var (name, btn) in _poseButtons)
        {
            StyleToggleButton(btn, string.Equals(controller.ManualVisual, name, StringComparison.OrdinalIgnoreCase));
        }

        _suppressSlider = true;
        _scaleSlider.Value = controller.SizeMultiplier;
        _scaleLabel.Text = $"SCALE {controller.SizeMultiplier:0.00}x";
        _suppressSlider = false;

        _btnTopmost.Content = controller.AlwaysOnTop ? "TOP: ON" : "TOP: OFF";
        StyleToggleButton(_btnTopmost, controller.AlwaysOnTop);
    }

    public void UpdatePlaybackTime(AemeathCompanion.Media.MediaSnapshot media)
    {
        if (!media.HasMusicSession || media.Duration <= TimeSpan.Zero)
        {
            _mediaTimeText.Text = "--:-- / --:--";
            return;
        }

        var pos = media.Position;
        if (media.IsMusicPlaying && media.TimelineUpdatedAt != DateTimeOffset.MinValue)
        {
            var elapsed = DateTimeOffset.UtcNow - media.TimelineUpdatedAt;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(1))
                pos += elapsed;
        }
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        if (pos > media.Duration) pos = media.Duration;

        _mediaTimeText.Text = $"{FormatClock(pos)} / {FormatClock(media.Duration)}";
    }

    private static string FormatClock(TimeSpan ts)
    {
        int totalSec = Math.Max(0, (int)Math.Floor(ts.TotalSeconds));
        int mins = totalSec / 60;
        int secs = totalSec % 60;
        return $"{mins}:{secs:00}";
    }

    private static Border MakeDivider() => new()
    {
        Height = 1,
        Background = PixelPalette.PlumBorderBrush,
        SnapsToDevicePixels = true
    };

    private static Button CreatePixelButton(string label, bool compact = false, bool danger = false)
    {
        var btn = new Button
        {
            Content = label,
            FontFamily = PixelPalette.PretendardFamily,
            FontWeight = FontWeights.Bold,
            FontSize = compact ? 10 : 10.5,
            Padding = compact ? new Thickness(6, 3, 6, 3) : new Thickness(8, 5, 8, 5),
            Foreground = danger ? PixelPalette.BlushGlowBrush : PixelPalette.CreamWhiteBrush,
            Background = danger ? PixelPalette.BerryRoseBrush : PixelPalette.PanelPlumBrush,
            BorderBrush = danger ? PixelPalette.SakuraPinkBrush : PixelPalette.PlumBorderBrush,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true
        };

        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
        borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(presenter);
        template.VisualTree = borderFactory;

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Button.BorderBrushProperty, PixelPalette.CyanGlintBrush));
        template.Triggers.Add(hoverTrigger);

        var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        template.Triggers.Add(disabledTrigger);

        btn.Template = template;
        return btn;
    }

    private static void StyleToggleButton(Button btn, bool active)
    {
        btn.Background = active ? PixelPalette.BerryRoseBrush : PixelPalette.PanelPlumBrush;
        btn.BorderBrush = active ? PixelPalette.BlushLightBrush : PixelPalette.PlumBorderBrush;
        btn.Foreground = active ? PixelPalette.CyanGlintBrush : PixelPalette.CreamWhiteBrush;
    }
}
