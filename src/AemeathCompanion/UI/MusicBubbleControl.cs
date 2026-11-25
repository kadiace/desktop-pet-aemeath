using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AemeathCompanion.Media;
using AemeathCompanion.Native;

namespace AemeathCompanion.UI;

public enum MediaTransportButton
{
    None,
    Previous,
    PlayPause,
    Next
}

/// <summary>
/// Pixel-art speech bubble rendered on a crisp 2px dot grid using Aemeath's GIF sprite palette.
/// Features stepped pixel corners, dynamic pixel tail, blurred album-tinted scanline backdrop,
/// pixel-framed jacket (or custom pixel ghost-disc fallback), 24-band pixel-block equalizer
/// with peak-hold dots, exact pixel-measured marquee text, a segmented pixel progress bar,
/// and interactive pixel-art Previous / Play-Pause / Next transport buttons.
/// </summary>
public sealed class MusicBubbleControl : FrameworkElement
{
    public const double BubbleWidth = 276.0;
    public const double BodyHeight = 308.0;
    public const double TailHeight = 16.0;
    public const double TotalHeight = BodyHeight + TailHeight;
    private const double P = 2.0; // Base pixel-art dot unit

    private static readonly Rect PrevBtnRect = new(56, 268, 44, 24);
    private static readonly Rect PlayPauseBtnRect = new(110, 268, 56, 24);
    private static readonly Rect NextBtnRect = new(176, 268, 44, 24);

    private readonly float[] _bands = new float[WasapiLoopbackAnalyzer.BandCount];
    private readonly float[] _peaks = new float[WasapiLoopbackAnalyzer.BandCount];

    private MediaSnapshot _snapshot = new();
    private BitmapSource? _sourceArtRef;
    private BitmapSource? _softBgArt;
    private Color _currentTint = PixelPalette.PanelPlum;
    private Color _targetTint = PixelPalette.PanelPlum;

    private double _displayPositionSec;
    private double _displayDurationSec;
    private double _tailCenterX = BubbleWidth * 0.5;
    private double _titleMarqueeClock;
    private double _artistMarqueeClock;
    private double _blinkClock;
    private string _lastTrackKey = string.Empty;

    private MediaTransportButton _hoveredButton = MediaTransportButton.None;
    private MediaTransportButton _pressedButton = MediaTransportButton.None;

    private readonly Typeface _boldTypeface;
    private readonly Typeface _regularTypeface;

    public event Action? PreviousClicked;
    public event Action? PlayPauseClicked;
    public event Action? NextClicked;

    public MusicBubbleControl()
    {
        Width = BubbleWidth;
        Height = TotalHeight;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        IsHitTestVisible = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        _boldTypeface = new Typeface(PixelPalette.PretendardFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        _regularTypeface = new Typeface(PixelPalette.PretendardFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

        MouseMove += OnBubbleMouseMove;
        MouseLeave += OnBubbleMouseLeave;
        MouseLeftButtonDown += OnBubbleMouseLeftDown;
        MouseLeftButtonUp += OnBubbleMouseLeftUp;
    }

    public MediaTransportButton HitTestTransportButton(Point dipPos)
    {
        if (PrevBtnRect.Contains(dipPos)) return MediaTransportButton.Previous;
        if (PlayPauseBtnRect.Contains(dipPos)) return MediaTransportButton.PlayPause;
        if (NextBtnRect.Contains(dipPos)) return MediaTransportButton.Next;
        return MediaTransportButton.None;
    }

    private bool IsButtonEnabled(MediaTransportButton button) => button switch
    {
        MediaTransportButton.Previous => _snapshot.HasMusicSession && _snapshot.CanSkipPrevious,
        MediaTransportButton.PlayPause => _snapshot.HasMusicSession && _snapshot.CanPlayPause,
        MediaTransportButton.Next => _snapshot.HasMusicSession && _snapshot.CanSkipNext,
        _ => false
    };

    private void OnBubbleMouseMove(object sender, MouseEventArgs e)
    {
        var hit = HitTestTransportButton(e.GetPosition(this));
        if (hit != _hoveredButton)
        {
            _hoveredButton = hit;
            Cursor = hit != MediaTransportButton.None && IsButtonEnabled(hit) ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }
    }

    private void OnBubbleMouseLeave(object sender, MouseEventArgs e)
    {
        if (_hoveredButton != MediaTransportButton.None || _pressedButton != MediaTransportButton.None)
        {
            _hoveredButton = MediaTransportButton.None;
            if (!IsMouseCaptured) _pressedButton = MediaTransportButton.None;
            Cursor = Cursors.Arrow;
            InvalidateVisual();
        }
    }

    private void OnBubbleMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        var hit = HitTestTransportButton(e.GetPosition(this));
        if (hit != MediaTransportButton.None && IsButtonEnabled(hit))
        {
            _pressedButton = hit;
            _hoveredButton = hit;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void OnBubbleMouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressedButton == MediaTransportButton.None) return;
        var pressed = _pressedButton;
        _pressedButton = MediaTransportButton.None;
        if (IsMouseCaptured) ReleaseMouseCapture();

        var hit = HitTestTransportButton(e.GetPosition(this));
        _hoveredButton = hit;
        InvalidateVisual();

        if (hit == pressed && IsButtonEnabled(pressed))
        {
            switch (pressed)
            {
                case MediaTransportButton.Previous:
                    PreviousClicked?.Invoke();
                    break;
                case MediaTransportButton.PlayPause:
                    PlayPauseClicked?.Invoke();
                    break;
                case MediaTransportButton.Next:
                    NextClicked?.Invoke();
                    break;
            }
        }
        e.Handled = true;
    }

    public void ApplySnapshot(MediaSnapshot snapshot)
    {
        _snapshot = snapshot;
        string trackKey = $"{snapshot.Title}|{snapshot.Artist}";
        if (!string.Equals(trackKey, _lastTrackKey, StringComparison.Ordinal))
        {
            _lastTrackKey = trackKey;
            _titleMarqueeClock = 0;
            _artistMarqueeClock = 0;
        }

        _displayPositionSec = Math.Max(0, snapshot.Position.TotalSeconds);
        _displayDurationSec = Math.Max(0, snapshot.Duration.TotalSeconds);
        _targetTint = snapshot.AlbumArt is not null ? snapshot.AlbumTint : PixelPalette.PanelPlum;

        if (!ReferenceEquals(_sourceArtRef, snapshot.AlbumArt))
        {
            _sourceArtRef = snapshot.AlbumArt;
            _softBgArt = snapshot.AlbumArt is not null ? CreateSoftBackdrop(snapshot.AlbumArt) : null;
        }
    }

    public void UpdateFrame(double dt, double tailCenterX, WasapiLoopbackAnalyzer audio)
    {
        _tailCenterX = Math.Clamp(Math.Round(tailCenterX / P) * P, 30.0, BubbleWidth - 30.0);
        _blinkClock += dt;

        // Smooth tint transition
        double blend = 1.0 - Math.Exp(-Math.Max(0, dt) * 4.5);
        _currentTint = PixelPalette.Lerp(_currentTint, _targetTint, blend);

        if (_snapshot.IsMusicPlaying && _displayDurationSec > 0)
        {
            _displayPositionSec = Math.Clamp(_displayPositionSec + dt, 0, _displayDurationSec);
        }

        audio.CopyLevels(_bands, _peaks, (float)dt);

        _titleMarqueeClock += dt;
        _artistMarqueeClock += dt;

        InvalidateVisual();
    }

    public void ApplyPreviewState(double tailCenterX, float[] bands, float[] peaks)
    {
        _tailCenterX = Math.Clamp(Math.Round(tailCenterX / P) * P, 30.0, BubbleWidth - 30.0);
        for (int i = 0; i < _bands.Length && i < bands.Length; i++) _bands[i] = bands[i];
        for (int i = 0; i < _peaks.Length && i < peaks.Length; i++) _peaks[i] = peaks[i];
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // 1. Stepped pixel-art outer bubble body & tail backdrop
        DrawSteppedBubbleBackdrop(dc);

        // 2. Interior clipped artwork glow + dark plum vignette + subtle digital scanlines
        DrawInteriorBackdrop(dc);

        // 3. Stepped pixel borders, blush inner frame, cyan corner glints & merged pixel tail
        DrawSteppedBubbleBordersAndTail(dc);

        // 4. Header strip: pixel music note + NOW PLAYING / PAUSED + source app tag
        DrawHeader(dc, dpi);

        // 5. Center album jacket (112x112) with pixel corner brackets or custom ghost disc
        DrawAlbumJacket(dc);

        // 6. 24-column pixel-block audio spectrum with peak-hold dots
        DrawPixelSpectrum(dc);

        // 7. Pixel-measured marquee title & artist
        DrawMarqueeText(dc, _snapshot.Title, _boldTypeface, 13.0, PixelPalette.CreamWhiteBrush,
            new Rect(22, 188, 232, 20), ref _titleMarqueeClock, dpi, dropShadow: true);
        DrawMarqueeText(dc, _snapshot.Artist, _regularTypeface, 11.0, PixelPalette.BlushLightBrush,
            new Rect(22, 209, 232, 18), ref _artistMarqueeClock, dpi, dropShadow: false);

        // 8. Pixel-segmented progress bar & timestamps
        DrawProgressAndTime(dc, dpi);

        // 9. Interactive pixel-art transport controls (Prev / Play-Pause / Next)
        DrawTransportButtons(dc);
    }

    private void DrawSteppedBubbleBackdrop(DrawingContext dc)
    {
        // Fill interior stepped octagon (matches outer contour minus 2px outline)
        var baseFill = new SolidColorBrush(PixelPalette.Lerp(PixelPalette.BasePlum, _currentTint, 0.16));
        baseFill.Freeze();

        dc.DrawRectangle(baseFill, null, new Rect(6, 2, BubbleWidth - 12, BodyHeight - 4));
        dc.DrawRectangle(baseFill, null, new Rect(2, 6, BubbleWidth - 4, BodyHeight - 12));
    }

    private void DrawInteriorBackdrop(DrawingContext dc)
    {
        var innerClip = new RectangleGeometry(new Rect(6, 6, BubbleWidth - 12, BodyHeight - 12));
        dc.PushClip(innerClip);

        if (_softBgArt is not null)
        {
            // Render dreamy pixelated soft backdrop from album artwork
            dc.PushOpacity(0.36);
            dc.DrawImage(_softBgArt, new Rect(6, 6, BubbleWidth - 12, BodyHeight - 12));
            dc.Pop();
        }

        // Top-to-bottom plum wash tinted with album color for crisp text legibility
        var topTint = PixelPalette.Lerp(PixelPalette.BasePlum, _currentTint, 0.25);
        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(145, topTint.R, topTint.G, topTint.B), 0.0));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(195, PixelPalette.BasePlum.R, PixelPalette.BasePlum.G, PixelPalette.BasePlum.B), 0.56));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(236, PixelPalette.OutlineDark.R, PixelPalette.OutlineDark.G, PixelPalette.OutlineDark.B), 1.0));
        grad.Freeze();
        dc.DrawRectangle(grad, null, new Rect(6, 6, BubbleWidth - 12, BodyHeight - 12));

        // Subtle 2px digital-ghost scanlines every 4px
        for (double y = 8; y < BodyHeight - 6; y += 4)
        {
            dc.DrawRectangle(PixelPalette.ScanlineBrush, null, new Rect(6, y, BubbleWidth - 12, 1));
        }

        dc.Pop();
    }

    private void DrawSteppedBubbleBordersAndTail(DrawingContext dc)
    {
        double w = BubbleWidth;
        double h = BodyHeight;
        double tx = _tailCenterX;

        // --- Outer 2px dark plum-charcoal outline (#241928) ---
        var dark = PixelPalette.OutlineDarkBrush;
        dc.DrawRectangle(dark, null, new Rect(6, 0, w - 12, P));             // Top
        dc.DrawRectangle(dark, null, new Rect(0, 6, P, h - 12));             // Left
        dc.DrawRectangle(dark, null, new Rect(w - P, 6, P, h - 12));         // Right

        // Bottom outline split around tail opening [tx - 10 .. tx + 10]
        double openL = tx - 10;
        double openR = tx + 10;
        if (openL > 6) dc.DrawRectangle(dark, null, new Rect(6, h - P, openL - 6, P));
        if (w - 6 > openR) dc.DrawRectangle(dark, null, new Rect(openR, h - P, (w - 6) - openR, P));

        // Stepped outer corners (2px notches)
        DrawCornerSteps(dc, dark, 0, 0, +1, +1);
        DrawCornerSteps(dc, dark, w - P, 0, -1, +1);
        DrawCornerSteps(dc, dark, 0, h - P, +1, -1);
        DrawCornerSteps(dc, dark, w - P, h - P, -1, -1);

        // --- Primary 2px sakura-blush frame (#FFD0E0) ---
        var blush = PixelPalette.BlushLightBrush;
        dc.DrawRectangle(blush, null, new Rect(6, 2, w - 12, P));            // Top
        dc.DrawRectangle(blush, null, new Rect(2, 6, P, h - 12));            // Left
        dc.DrawRectangle(blush, null, new Rect(w - 4, 6, P, h - 12));        // Right
        if (openL > 6) dc.DrawRectangle(blush, null, new Rect(6, h - 4, openL - 6, P));
        if (w - 6 > openR) dc.DrawRectangle(blush, null, new Rect(openR, h - 4, (w - 6) - openR, P));

        // Blush inner corner pixels
        dc.DrawRectangle(blush, null, new Rect(4, 4, P, P));
        dc.DrawRectangle(blush, null, new Rect(w - 6, 4, P, P));
        dc.DrawRectangle(blush, null, new Rect(4, h - 6, P, P));
        dc.DrawRectangle(blush, null, new Rect(w - 6, h - 6, P, P));

        // --- Inner 2px berry-rose accent trim (#A04068) + cyan corner glints (#D0FFFF) ---
        var rose = PixelPalette.BerryRoseBrush;
        dc.DrawRectangle(rose, null, new Rect(8, 4, w - 16, P));             // Top trim
        dc.DrawRectangle(rose, null, new Rect(4, 8, P, h - 16));             // Left trim
        dc.DrawRectangle(rose, null, new Rect(w - 6, 8, P, h - 16));         // Right trim
        if (openL > 8) dc.DrawRectangle(rose, null, new Rect(8, h - 6, openL - 8, P));
        if (w - 8 > openR) dc.DrawRectangle(rose, null, new Rect(openR, h - 6, (w - 8) - openR, P));

        // Cyan pixel corner glints
        dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(6, 6, P, P));
        dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(w - 8, 6, P, P));
        dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(6, h - 8, P, P));
        dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(w - 8, h - 8, P, P));

        // --- Stepped Pixel Speech Bubble Tail (centered at tx) ---
        var tailOutline = PixelPalette.OutlineDarkBrush;
        var innerFill = PixelPalette.OutlineDarkBrush;

        // Seamless bridge through the bottom border opening using the dark plum interior tone
        dc.DrawRectangle(innerFill, null, new Rect(tx - 8, h - 7, 16, 7));
        dc.DrawRectangle(blush, null, new Rect(tx - 10, h - 4, P, 4));
        dc.DrawRectangle(blush, null, new Rect(tx + 8, h - 4, P, 4));
        dc.DrawRectangle(tailOutline, null, new Rect(tx - 12, h - 2, P, 2));
        dc.DrawRectangle(tailOutline, null, new Rect(tx + 10, h - 2, P, 2));

        // 4 pixel stairs tapering to a crisp 4px pixel tip
        int[] halfWidths = [8, 6, 4, 2];
        for (int row = 0; row < halfWidths.Length; row++)
        {
            int hw = halfWidths[row];
            double ry = h + row * 3;
            if (row < halfWidths.Length - 1)
            {
                dc.DrawRectangle(innerFill, null, new Rect(tx - hw + 2, ry, Math.Max(0, (hw - 2) * 2), 3));
                dc.DrawRectangle(blush, null, new Rect(tx - hw, ry, P, 3));
                dc.DrawRectangle(blush, null, new Rect(tx + hw - P, ry, P, 3));
                dc.DrawRectangle(tailOutline, null, new Rect(tx - hw - P, ry, P, 3));
                dc.DrawRectangle(tailOutline, null, new Rect(tx + hw, ry, P, 3));
            }
            else
            {
                dc.DrawRectangle(blush, null, new Rect(tx - 2, ry, 4, 2));
                dc.DrawRectangle(tailOutline, null, new Rect(tx - 4, ry, P, 2));
                dc.DrawRectangle(tailOutline, null, new Rect(tx + 2, ry, P, 2));
                dc.DrawRectangle(tailOutline, null, new Rect(tx - 2, ry + 2, 4, 2));
            }
        }
    }

    private static void DrawCornerSteps(DrawingContext dc, Brush brush, double cornerX, double cornerY, int dirX, int dirY)
    {
        // Two 2x2 diagonal step pixels forming a crisp pixel-art chamfer
        dc.DrawRectangle(brush, null, new Rect(cornerX + dirX * 4, cornerY + dirY * 2, P, P));
        dc.DrawRectangle(brush, null, new Rect(cornerX + dirX * 2, cornerY + dirY * 4, P, P));
    }

    private void DrawHeader(DrawingContext dc, double dpi)
    {
        bool playing = _snapshot.IsMusicPlaying;
        var accent = playing ? PixelPalette.GhostCyanBrush : PixelPalette.SakuraPinkBrush;

        // Blinking 4x4 pixel LED indicator
        bool ledOn = !playing || ((int)(_blinkClock * 2.4) % 2 == 0);
        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(17, 13, 6, 6));
        dc.DrawRectangle(ledOn ? (playing ? PixelPalette.CyanGlintBrush : PixelPalette.SakuraPinkBrush) : PixelPalette.PlumBorderBrush,
            null, new Rect(18, 14, 4, 4));

        // Tiny pixel music note (8x8 on 2px grid)
        DrawPixelMusicNote(dc, 28, 12, accent);

        string stateLabel = playing ? "NOW PLAYING" : "PAUSED";
        var stateFt = new FormattedText(stateLabel, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            _boldTypeface, 9.5, accent, dpi);
        dc.DrawText(stateFt, new Point(40, 10.5));

        if (!string.IsNullOrWhiteSpace(_snapshot.SourceApp))
        {
            string appTag = _snapshot.SourceApp.ToUpperInvariant();
            if (appTag.Length > 13) appTag = appTag[..13];
            var appFt = new FormattedText(appTag, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                _regularTypeface, 9.0, PixelPalette.LavenderBrush, dpi);
            dc.DrawText(appFt, new Point(BubbleWidth - 18 - appFt.Width, 11));
        }

        // Dotted pixel divider line
        for (double x = 16; x < BubbleWidth - 16; x += 4)
        {
            dc.DrawRectangle(PixelPalette.PlumBorderBrush, null, new Rect(x, 26, 2, 1));
        }
    }

    private static void DrawPixelMusicNote(DrawingContext dc, double x, double y, Brush brush)
    {
        // Classic 8-bit beamed eighth-note icon (2px dots)
        dc.DrawRectangle(brush, null, new Rect(x + 2, y, 6, 2));      // Top beam
        dc.DrawRectangle(brush, null, new Rect(x + 2, y + 2, 2, 5));  // Left stem
        dc.DrawRectangle(brush, null, new Rect(x + 6, y + 2, 2, 4));  // Right stem
        dc.DrawRectangle(brush, null, new Rect(x, y + 6, 4, 2));      // Left head
        dc.DrawRectangle(brush, null, new Rect(x + 4, y + 5, 4, 2));  // Right head
    }

    private void DrawAlbumJacket(DrawingContext dc)
    {
        const double size = 112.0;
        double x = Math.Floor((BubbleWidth - size) * 0.5);
        const double y = 34.0;

        // Pixel drop shadow
        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(x - 2, y - 2, size + 6, size + 6));
        // Berry-rose outer trim & sakura inner frame
        dc.DrawRectangle(PixelPalette.BerryRoseBrush, null, new Rect(x - 2, y - 2, size + 4, size + 4));
        dc.DrawRectangle(PixelPalette.BlushGlowBrush, null, new Rect(x - 1, y - 1, size + 2, size + 2));

        var artRect = new Rect(x, y, size, size);
        if (_snapshot.AlbumArt is not null)
        {
            dc.PushClip(new RectangleGeometry(artRect));
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            dc.DrawImage(_snapshot.AlbumArt, artRect);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            dc.Pop();
        }
        else
        {
            DrawPixelDiscFallback(dc, artRect);
        }

        // Crisp 4-corner digital cyan pixel brackets (#D0FFFF)
        var glint = PixelPalette.CyanGlintBrush;
        dc.DrawRectangle(glint, null, new Rect(x - 4, y - 4, 6, 2));
        dc.DrawRectangle(glint, null, new Rect(x - 4, y - 4, 2, 6));

        dc.DrawRectangle(glint, null, new Rect(x + size - 2, y - 4, 6, 2));
        dc.DrawRectangle(glint, null, new Rect(x + size + 2, y - 4, 2, 6));

        dc.DrawRectangle(glint, null, new Rect(x - 4, y + size + 2, 6, 2));
        dc.DrawRectangle(glint, null, new Rect(x - 4, y + size - 2, 2, 6));

        dc.DrawRectangle(glint, null, new Rect(x + size - 2, y + size + 2, 6, 2));
        dc.DrawRectangle(glint, null, new Rect(x + size + 2, y + size - 2, 2, 6));
    }

    private static void DrawPixelDiscFallback(DrawingContext dc, Rect r)
    {
        dc.DrawRectangle(PixelPalette.PanelPlumBrush, null, r);
        double cx = r.X + r.Width * 0.5;
        double cy = r.Y + r.Height * 0.5;

        // Retro pixel vinyl / CD disc with 3-step chamfered circle silhouette
        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(cx - 34, cy - 18, 68, 36));
        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(cx - 30, cy - 26, 60, 52));
        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(cx - 24, cy - 30, 48, 60));
        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(cx - 18, cy - 34, 36, 68));

        dc.DrawRectangle(PixelPalette.PlumBorderBrush, null, new Rect(cx - 30, cy - 16, 60, 32));
        dc.DrawRectangle(PixelPalette.PlumBorderBrush, null, new Rect(cx - 26, cy - 22, 52, 44));
        dc.DrawRectangle(PixelPalette.PlumBorderBrush, null, new Rect(cx - 22, cy - 26, 44, 52));
        dc.DrawRectangle(PixelPalette.PlumBorderBrush, null, new Rect(cx - 16, cy - 30, 32, 60));

        // Cyan highlight arcs
        dc.DrawRectangle(PixelPalette.GhostCyanBrush, null, new Rect(cx - 22, cy - 14, 4, 10));
        dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(cx - 16, cy - 22, 10, 4));
        dc.DrawRectangle(PixelPalette.LavenderBrush, null, new Rect(cx + 14, cy + 16, 8, 4));

        // Sakura center label
        dc.DrawRectangle(PixelPalette.SakuraPinkBrush, null, new Rect(cx - 14, cy - 10, 28, 20));
        dc.DrawRectangle(PixelPalette.SakuraPinkBrush, null, new Rect(cx - 10, cy - 14, 20, 28));
        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(cx - 4, cy - 4, 8, 8));
        dc.DrawRectangle(PixelPalette.BlushGlowBrush, null, new Rect(cx - 2, cy - 2, 4, 4));

        // Signature Aemeath 4-point pixel sparkles in corners
        DrawPixelSparkle(dc, r.X + 14, r.Y + 14, PixelPalette.CyanGlintBrush);
        DrawPixelSparkle(dc, r.Right - 18, r.Bottom - 18, PixelPalette.BlushGlowBrush);
    }

    private static void DrawPixelSparkle(DrawingContext dc, double x, double y, Brush brush)
    {
        dc.DrawRectangle(brush, null, new Rect(x + 2, y, 2, 6));
        dc.DrawRectangle(brush, null, new Rect(x, y + 2, 6, 2));
    }

    private void DrawPixelSpectrum(DrawingContext dc)
    {
        const int cols = WasapiLoopbackAnalyzer.BandCount; // 24
        const int maxBlocks = 7;
        const double colW = 7.0;
        const double colGap = 2.0;
        const double blockH = 2.0;
        const double blockGap = 2.0;

        double totalW = cols * colW + (cols - 1) * colGap; // 214px
        double startX = Math.Floor((BubbleWidth - totalW) * 0.5);
        const double baselineY = 182.0;

        // Subtle dark rail under the equalizer
        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(startX - 2, baselineY + 1, totalW + 4, 2));

        for (int i = 0; i < cols; i++)
        {
            double bx = startX + i * (colW + colGap);
            float level = _bands[i];
            float peak = _peaks[i];

            int litBlocks = Math.Clamp((int)Math.Round(level * maxBlocks), 0, maxBlocks);
            // Always show a faint resting baseline pixel
            dc.DrawRectangle(PixelPalette.TrackPlumBrush, null, new Rect(bx, baselineY - blockH, colW, blockH));

            for (int b = 0; b < litBlocks; b++)
            {
                double by = baselineY - (b + 1) * blockH - b * blockGap;
                Brush blockBrush = b switch
                {
                    <= 1 => PixelPalette.GhostCyanBrush,
                    <= 3 => PixelPalette.LavenderBrush,
                    <= 5 => PixelPalette.SakuraPinkBrush,
                    _ => PixelPalette.BlushGlowBrush
                };
                dc.DrawRectangle(blockBrush, null, new Rect(bx, by, colW, blockH));
            }

            // Floating cyan-white peak-hold dot cleanly separated on the pixel grid
            int peakBlock = Math.Clamp((int)Math.Round(peak * maxBlocks), 0, maxBlocks);
            if (peakBlock > litBlocks && peak > 0.08f)
            {
                double py = baselineY - (peakBlock + 1) * blockH - peakBlock * blockGap;
                dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(bx, py, colW, blockH));
            }
        }
    }

    private static void DrawMarqueeText(
        DrawingContext dc,
        string rawText,
        Typeface typeface,
        double fontSize,
        Brush foreground,
        Rect viewport,
        ref double clock,
        double dpi,
        bool dropShadow)
    {
        string text = string.IsNullOrWhiteSpace(rawText) ? "-" : rawText;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, fontSize, foreground, dpi);

        double textWidth = ft.WidthIncludingTrailingWhitespace;
        double overflow = textWidth - viewport.Width;
        double offsetX = 0;

        if (overflow > 2.0)
        {
            const double startPause = 1.4;
            const double endPause = 2.6;
            const double speedPxPerSec = 32.0;
            double travelTime = overflow / speedPxPerSec;
            double cycle = startPause + travelTime + endPause;
            clock %= cycle;

            if (clock > startPause)
            {
                double t = Math.Clamp((clock - startPause) / travelTime, 0.0, 1.0);
                offsetX = -Math.Round(t * overflow);
            }
        }
        else
        {
            clock = 0;
            // Center short titles cleanly inside the pixel card
            offsetX = Math.Floor((viewport.Width - textWidth) * 0.5);
        }

        dc.PushClip(new RectangleGeometry(viewport));
        double drawX = viewport.X + offsetX;
        double drawY = viewport.Y + Math.Floor((viewport.Height - ft.Height) * 0.5);

        if (dropShadow)
        {
            var shadowFt = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, fontSize, PixelPalette.OutlineDarkBrush, dpi);
            dc.DrawText(shadowFt, new Point(drawX + 1, drawY + 1));
        }

        dc.DrawText(ft, new Point(drawX, drawY));
        dc.Pop();
    }

    private void DrawProgressAndTime(DrawingContext dc, double dpi)
    {
        const double barX = 24.0;
        const double barY = 238.0;
        const double barW = 228.0;
        const double barH = 8.0;

        // Recessed pixel progress container
        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(barX - 2, barY - 2, barW + 4, barH + 4));
        dc.DrawRectangle(PixelPalette.PlumBorderBrush, null, new Rect(barX - 1, barY - 1, barW + 2, barH + 2));
        dc.DrawRectangle(PixelPalette.TrackPlumBrush, null, new Rect(barX, barY, barW, barH));

        double fraction = _displayDurationSec > 0 ? Math.Clamp(_displayPositionSec / _displayDurationSec, 0.0, 1.0) : 0.0;
        double fillW = Math.Round(barW * fraction);

        if (fillW > 0)
        {
            // Segmented 4px pixel blocks with 1px micro-divider
            const double segW = 4.0;
            const double step = 5.0;
            for (double sx = 0; sx < fillW; sx += step)
            {
                double drawW = Math.Min(segW, fillW - sx);
                if (drawW <= 0) break;
                double t = (sx + drawW * 0.5) / barW;
                Brush b = t < 0.45 ? PixelPalette.PinkMidBrush
                    : t < 0.82 ? PixelPalette.SakuraPinkBrush
                    : PixelPalette.BlushLightBrush;
                dc.DrawRectangle(b, null, new Rect(barX + sx, barY + 1, drawW, barH - 2));
            }

            // Bright digital-cyan cursor tip pixel
            double tipX = Math.Clamp(barX + fillW - 3, barX, barX + barW - 3);
            dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(tipX, barY, 3, barH));
        }

        string curText = _displayDurationSec > 0 ? FormatTime(_displayPositionSec) : "--:--";
        string totText = _displayDurationSec > 0 ? FormatTime(_displayDurationSec) : "--:--";

        var curFt = new FormattedText(curText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            _boldTypeface, 9.5, PixelPalette.BlushGlowBrush, dpi);
        var totFt = new FormattedText(totText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            _boldTypeface, 9.5, PixelPalette.LavenderBrush, dpi);

        dc.DrawText(curFt, new Point(barX, barY + 12));
        dc.DrawText(totFt, new Point(barX + barW - totFt.Width, barY + 12));
    }

    private void DrawTransportButtons(DrawingContext dc)
    {
        // Subtle dotted pixel separator above transport bar
        for (double x = 24; x < BubbleWidth - 24; x += 4)
            dc.DrawRectangle(PixelPalette.TrackPlumBrush, null, new Rect(x, 263, 2, 1));

        DrawPixelTransportButton(dc, PrevBtnRect, MediaTransportButton.Previous, featured: false);
        DrawPixelTransportButton(dc, PlayPauseBtnRect, MediaTransportButton.PlayPause, featured: true);
        DrawPixelTransportButton(dc, NextBtnRect, MediaTransportButton.Next, featured: false);
    }

    private void DrawPixelTransportButton(DrawingContext dc, Rect r, MediaTransportButton kind, bool featured)
    {
        bool enabled = IsButtonEnabled(kind);
        bool hovered = enabled && _hoveredButton == kind;
        bool pressed = enabled && _pressedButton == kind;

        Brush borderBrush = !enabled
            ? PixelPalette.TrackPlumBrush
            : pressed
                ? PixelPalette.SakuraPinkBrush
                : hovered
                    ? PixelPalette.CyanGlintBrush
                    : featured
                        ? PixelPalette.BlushLightBrush
                        : PixelPalette.PlumBorderBrush;

        Brush fillBrush = !enabled
            ? PixelPalette.BasePlumBrush
            : pressed
                ? PixelPalette.BerryRoseBrush
                : hovered
                    ? PixelPalette.BerryRoseBrush
                    : featured
                        ? PixelPalette.PanelPlumBrush
                        : PixelPalette.PanelPlumBrush;

        Brush iconBrush = !enabled
            ? PixelPalette.PlumBorderBrush
            : hovered || pressed
                ? PixelPalette.CreamWhiteBrush
                : featured
                    ? (_snapshot.IsMusicPlaying ? PixelPalette.GhostCyanBrush : PixelPalette.BlushGlowBrush)
                    : PixelPalette.BlushLightBrush;

        // Crisp 2px stepped pixel button frame (chamfered corners)
        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(r.X, r.Y, r.Width, r.Height));
        dc.DrawRectangle(borderBrush, null, new Rect(r.X + 2, r.Y, r.Width - 4, P));               // Top
        dc.DrawRectangle(borderBrush, null, new Rect(r.X + 2, r.Bottom - P, r.Width - 4, P));      // Bottom
        dc.DrawRectangle(borderBrush, null, new Rect(r.X, r.Y + 2, P, r.Height - 4));              // Left
        dc.DrawRectangle(borderBrush, null, new Rect(r.Right - P, r.Y + 2, P, r.Height - 4));      // Right

        // Inner fill + subtle top highlight / bottom bevel
        dc.DrawRectangle(fillBrush, null, new Rect(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4));
        if (enabled && !pressed)
        {
            var topBevel = hovered ? PixelPalette.SakuraPinkBrush : PixelPalette.TrackPlumBrush;
            dc.DrawRectangle(topBevel, null, new Rect(r.X + 2, r.Y + 2, r.Width - 4, 2));
        }

        double cx = Math.Floor(r.X + r.Width * 0.5);
        double cy = Math.Floor(r.Y + r.Height * 0.5) + (pressed ? 1.0 : 0.0);

        switch (kind)
        {
            case MediaTransportButton.Previous:
                DrawPixelPrevGlyph(dc, cx, cy, iconBrush);
                break;
            case MediaTransportButton.PlayPause:
                if (_snapshot.IsMusicPlaying)
                    DrawPixelPauseGlyph(dc, cx, cy, iconBrush);
                else
                    DrawPixelPlayGlyph(dc, cx, cy, iconBrush);
                break;
            case MediaTransportButton.Next:
                DrawPixelNextGlyph(dc, cx, cy, iconBrush);
                break;
        }
    }

    private static void DrawPixelPrevGlyph(DrawingContext dc, double cx, double cy, Brush brush)
    {
        // Vertical bar on left + pixel left-pointing triangle (|<)
        dc.DrawRectangle(brush, null, new Rect(cx - 7, cy - 5, 2, 10));
        dc.DrawRectangle(brush, null, new Rect(cx - 3, cy - 1, 2, 2));
        dc.DrawRectangle(brush, null, new Rect(cx - 1, cy - 3, 2, 6));
        dc.DrawRectangle(brush, null, new Rect(cx + 1, cy - 4, 2, 8));
        dc.DrawRectangle(brush, null, new Rect(cx + 3, cy - 5, 2, 10));
    }

    private static void DrawPixelNextGlyph(DrawingContext dc, double cx, double cy, Brush brush)
    {
        // Pixel right-pointing triangle + vertical bar on right (>|)
        dc.DrawRectangle(brush, null, new Rect(cx - 5, cy - 5, 2, 10));
        dc.DrawRectangle(brush, null, new Rect(cx - 3, cy - 4, 2, 8));
        dc.DrawRectangle(brush, null, new Rect(cx - 1, cy - 3, 2, 6));
        dc.DrawRectangle(brush, null, new Rect(cx + 1, cy - 1, 2, 2));
        dc.DrawRectangle(brush, null, new Rect(cx + 5, cy - 5, 2, 10));
    }

    private static void DrawPixelPlayGlyph(DrawingContext dc, double cx, double cy, Brush brush)
    {
        // Crisp 5-column pixel play triangle (>)
        dc.DrawRectangle(brush, null, new Rect(cx - 4, cy - 6, 2, 12));
        dc.DrawRectangle(brush, null, new Rect(cx - 2, cy - 5, 2, 10));
        dc.DrawRectangle(brush, null, new Rect(cx, cy - 3, 2, 6));
        dc.DrawRectangle(brush, null, new Rect(cx + 2, cy - 2, 2, 4));
        dc.DrawRectangle(brush, null, new Rect(cx + 4, cy - 1, 2, 2));
    }

    private static void DrawPixelPauseGlyph(DrawingContext dc, double cx, double cy, Brush brush)
    {
        // Crisp twin pixel pause bars (||)
        dc.DrawRectangle(brush, null, new Rect(cx - 5, cy - 5, 3, 10));
        dc.DrawRectangle(brush, null, new Rect(cx + 2, cy - 5, 3, 10));
    }

    private static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "--:--";
        int total = (int)Math.Floor(seconds);
        int mins = total / 60;
        int secs = total % 60;
        return $"{mins}:{secs:00}";
    }

    private static BitmapSource? CreateSoftBackdrop(BitmapSource source)
    {
        try
        {
            // Downscale to a dreamy 22x22 low-res mosaic to create a soft pixel-art ambient glow
            var small = new TransformedBitmap(source, new ScaleTransform(22.0 / Math.Max(1, source.PixelWidth), 22.0 / Math.Max(1, source.PixelHeight)));
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
                ctx.DrawImage(small, new Rect(0, 0, 64, 64));
            }
            var rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
        catch
        {
            return null;
        }
    }
}
