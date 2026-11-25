using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AemeathCompanion.Media;
using AemeathCompanion.Native;

namespace AemeathCompanion.UI;

public enum MiniPlayerTailEdge
{
    Bottom,
    Left,
    Right
}

/// <summary>
/// Compact horizontal pixel-art mini music player shown above (or beside when near screen top)
/// Aemeath during Platform mode. Features a translucent bottom-up background audio spectrum,
/// 36x36 album art, pixel-measured title/artist marquee, slim segmented progress bar,
/// and interactive Prev / Play-Pause / Next pixel buttons.
/// </summary>
public sealed class PlatformMiniPlayerControl : FrameworkElement
{
    public const double BodyWidth = 304.0;
    public const double BodyHeight = 56.0;
    public const double SideMargin = 10.0;
    public const double BottomMargin = 10.0;
    public const double CanvasWidth = BodyWidth + SideMargin * 2.0;   // 324
    public const double CanvasHeight = BodyHeight + BottomMargin;     // 66
    public const double BodyLeft = SideMargin;
    public const double BodyTop = 0.0;
    private const double P = 2.0;
    private const double InnerInset = 6.0;
    private const double JacketSize = BodyHeight - InnerInset * 2.0; // 44x44 exact 1:1 flush height
    private const double JacketDividerW = 2.0;
    private const double RightAreaLeft = BodyLeft + InnerInset + JacketSize + JacketDividerW; // BodyLeft + 52
    private const double RightAreaWidth = BodyWidth - InnerInset * 2.0 - JacketSize - JacketDividerW; // 246

    private static readonly Rect PrevBtnRect = new(BodyLeft + 220, BodyTop + 17, 22, 22);
    private static readonly Rect PlayPauseBtnRect = new(BodyLeft + 244, BodyTop + 17, 26, 22);
    private static readonly Rect NextBtnRect = new(BodyLeft + 272, BodyTop + 17, 22, 22);

    private static readonly SolidColorBrush BgSpecCyan = Freeze(Color.FromArgb(72, 0x88, 0xE8, 0xE8));
    private static readonly SolidColorBrush BgSpecLavender = Freeze(Color.FromArgb(66, 0xB0, 0xB0, 0xD8));
    private static readonly SolidColorBrush BgSpecPink = Freeze(Color.FromArgb(70, 0xF0, 0xA0, 0xB8));
    private static readonly SolidColorBrush BgSpecPeak = Freeze(Color.FromArgb(92, 0xD0, 0xFF, 0xFF));

    private readonly float[] _bands = new float[WasapiLoopbackAnalyzer.BandCount];
    private readonly float[] _peaks = new float[WasapiLoopbackAnalyzer.BandCount];

    private MediaSnapshot _snapshot = new();
    private BitmapSource? _sourceArtRef;
    private BitmapSource? _softBgArt;
    private Color _currentTint = PixelPalette.PanelPlum;
    private Color _targetTint = PixelPalette.PanelPlum;

    private double _displayPositionSec;
    private double _displayDurationSec;
    private MiniPlayerTailEdge _tailEdge = MiniPlayerTailEdge.Bottom;
    private double _tailCenterLocal = BodyLeft + BodyWidth * 0.5;
    private double _titleMarqueeClock;
    private double _artistMarqueeClock;
    private string _lastTrackKey = string.Empty;

    private MediaTransportButton _hoveredButton = MediaTransportButton.None;
    private MediaTransportButton _pressedButton = MediaTransportButton.None;

    private readonly Typeface _boldTypeface;
    private readonly Typeface _regularTypeface;

    public event Action? PreviousClicked;
    public event Action? PlayPauseClicked;
    public event Action? NextClicked;

    public PlatformMiniPlayerControl()
    {
        Width = CanvasWidth;
        Height = CanvasHeight;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        IsHitTestVisible = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        _boldTypeface = new Typeface(PixelPalette.PretendardFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        _regularTypeface = new Typeface(PixelPalette.PretendardFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

        MouseMove += OnMiniMouseMove;
        MouseLeave += OnMiniMouseLeave;
        MouseLeftButtonDown += OnMiniMouseLeftDown;
        MouseLeftButtonUp += OnMiniMouseLeftUp;
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

    private void OnMiniMouseMove(object sender, MouseEventArgs e)
    {
        var hit = HitTestTransportButton(e.GetPosition(this));
        if (hit != _hoveredButton)
        {
            _hoveredButton = hit;
            Cursor = hit != MediaTransportButton.None && IsButtonEnabled(hit) ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }
    }

    private void OnMiniMouseLeave(object sender, MouseEventArgs e)
    {
        if (_hoveredButton != MediaTransportButton.None || _pressedButton != MediaTransportButton.None)
        {
            _hoveredButton = MediaTransportButton.None;
            if (!IsMouseCaptured) _pressedButton = MediaTransportButton.None;
            Cursor = Cursors.Arrow;
            InvalidateVisual();
        }
    }

    private void OnMiniMouseLeftDown(object sender, MouseButtonEventArgs e)
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

    private void OnMiniMouseLeftUp(object sender, MouseButtonEventArgs e)
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

    public void UpdateFrame(double dt, MiniPlayerTailEdge tailEdge, double tailCenterLocal, WasapiLoopbackAnalyzer audio)
    {
        _tailEdge = tailEdge;
        _tailCenterLocal = tailEdge == MiniPlayerTailEdge.Bottom
            ? Math.Clamp(Math.Round(tailCenterLocal / P) * P, BodyLeft + 26.0, BodyLeft + BodyWidth - 26.0)
            : Math.Clamp(Math.Round(tailCenterLocal / P) * P, BodyTop + 16.0, BodyTop + BodyHeight - 16.0);

        double blend = 1.0 - Math.Exp(-Math.Max(0, dt) * 4.5);
        _currentTint = PixelPalette.Lerp(_currentTint, _targetTint, blend);

        if (_snapshot.IsMusicPlaying && _displayDurationSec > 0)
            _displayPositionSec = Math.Clamp(_displayPositionSec + dt, 0, _displayDurationSec);

        audio.CopyLevels(_bands, _peaks, (float)dt);

        _titleMarqueeClock += dt;
        _artistMarqueeClock += dt;
        InvalidateVisual();
    }

    public void ApplyPreviewState(MiniPlayerTailEdge tailEdge, double tailCenterLocal, float[] bands, float[] peaks)
    {
        _tailEdge = tailEdge;
        _tailCenterLocal = tailCenterLocal;
        for (int i = 0; i < _bands.Length && i < bands.Length; i++) _bands[i] = bands[i];
        for (int i = 0; i < _peaks.Length && i < peaks.Length; i++) _peaks[i] = peaks[i];
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // 1. Stepped pixel outer fill
        DrawBackdrop(dc);

        // 2. Clipped interior: flush 1:1 left album jacket + right-side backdrop & translucent bottom-up audio spectrum
        DrawInteriorAndBackgroundSpectrum(dc);

        // 3. Stepped 2px pixel borders, corner glints, and 3-way dynamic pixel tail
        DrawBordersAndTail(dc);

        // 4. Center title & artist marquee + slim segmented pixel progress bar
        DrawMarqueeText(dc, _snapshot.Title, _boldTypeface, 11.5, PixelPalette.CreamWhiteBrush,
            new Rect(RightAreaLeft + 6, BodyTop + 7, 156, 16), ref _titleMarqueeClock, dpi);
        DrawMarqueeText(dc, _snapshot.Artist, _regularTypeface, 9.5, PixelPalette.BlushLightBrush,
            new Rect(RightAreaLeft + 6, BodyTop + 23, 156, 14), ref _artistMarqueeClock, dpi);
        DrawSlimProgressBar(dc);

        // 5. Right compact transport buttons (Prev / Play-Pause / Next)
        DrawTransportButtons(dc);
    }

    private void DrawBackdrop(DrawingContext dc)
    {
        var baseFill = new SolidColorBrush(PixelPalette.Lerp(PixelPalette.BasePlum, _currentTint, 0.16));
        baseFill.Freeze();
        dc.DrawRectangle(baseFill, null, new Rect(BodyLeft + 6, BodyTop + 2, BodyWidth - 12, BodyHeight - 4));
        dc.DrawRectangle(baseFill, null, new Rect(BodyLeft + 2, BodyTop + 6, BodyWidth - 4, BodyHeight - 12));
    }

    private void DrawInteriorAndBackgroundSpectrum(DrawingContext dc)
    {
        var inner = new Rect(BodyLeft + InnerInset, BodyTop + InnerInset, BodyWidth - InnerInset * 2.0, JacketSize);
        var jacketRect = new Rect(inner.Left, inner.Top, JacketSize, JacketSize);
        var rightArea = new Rect(RightAreaLeft, inner.Top, RightAreaWidth, JacketSize);

        dc.PushClip(new RectangleGeometry(inner));

        // Right-side backdrop (soft album tint glow + dark plum wash + translucent bottom-up spectrum)
        if (_softBgArt is not null)
        {
            dc.PushOpacity(0.28);
            dc.DrawImage(_softBgArt, rightArea);
            dc.Pop();
        }

        var topTint = PixelPalette.Lerp(PixelPalette.BasePlum, _currentTint, 0.22);
        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(150, topTint.R, topTint.G, topTint.B), 0.0));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(220, PixelPalette.OutlineDark.R, PixelPalette.OutlineDark.G, PixelPalette.OutlineDark.B), 1.0));
        grad.Freeze();
        dc.DrawRectangle(grad, null, rightArea);

        // Semi-transparent pixel audio spectrum fitted cleanly to the remaining right-side width
        DrawBackgroundBottomSpectrum(dc, rightArea);

        // Subtle horizontal digital scanlines on right content area
        for (double y = rightArea.Top + 2; y < rightArea.Bottom; y += 4)
            dc.DrawRectangle(PixelPalette.ScanlineBrush, null, new Rect(rightArea.Left, y, rightArea.Width, 1));

        // Flush 1:1 album jacket filling the entire left height with zero padding
        DrawMiniAlbumJacket(dc, jacketRect);

        // Crisp 2px vertical pixel divider between flush album jacket and spectrum/content area
        dc.DrawRectangle(PixelPalette.BerryRoseBrush, null, new Rect(jacketRect.Right, inner.Top, 1.0, inner.Height));
        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(jacketRect.Right + 1.0, inner.Top, 1.0, inner.Height));

        dc.Pop();
    }

    private void DrawBackgroundBottomSpectrum(DrawingContext dc, Rect area)
    {
        const int cols = WasapiLoopbackAnalyzer.BandCount; // 24
        const int maxBlocks = 9;
        const double colW = 8.0;
        const double colGap = 2.0;
        const double blockH = 3.0;
        const double blockGap = 1.0;

        double totalW = cols * colW + (cols - 1) * colGap; // 238px inside 246px right area
        double startX = Math.Floor(area.Left + (area.Width - totalW) * 0.5);
        double baselineY = area.Bottom;

        for (int i = 0; i < cols; i++)
        {
            double bx = startX + i * (colW + colGap);
            float level = _bands[i];
            float peak = _peaks[i];

            int litBlocks = Math.Clamp((int)Math.Round(level * maxBlocks), 0, maxBlocks);
            for (int b = 0; b < litBlocks; b++)
            {
                double by = baselineY - (b + 1) * blockH - b * blockGap;
                Brush brush = b switch
                {
                    <= 2 => BgSpecCyan,
                    <= 5 => BgSpecLavender,
                    _ => BgSpecPink
                };
                dc.DrawRectangle(brush, null, new Rect(bx, by, colW, blockH));
            }

            int peakBlock = Math.Clamp((int)Math.Round(peak * maxBlocks), 0, maxBlocks);
            if (peakBlock > litBlocks && peak > 0.08f)
            {
                double py = baselineY - (peakBlock + 1) * blockH - peakBlock * blockGap;
                if (py >= area.Top)
                    dc.DrawRectangle(BgSpecPeak, null, new Rect(bx, py, colW, 2.0));
            }
        }
    }

    private void DrawBordersAndTail(DrawingContext dc)
    {
        double l = BodyLeft;
        double t = BodyTop;
        double r = BodyLeft + BodyWidth;
        double b = BodyTop + BodyHeight;

        var dark = PixelPalette.OutlineDarkBrush;
        var blush = PixelPalette.BlushLightBrush;
        var rose = PixelPalette.BerryRoseBrush;

        // Top border (always closed)
        dc.DrawRectangle(dark, null, new Rect(l + 6, t, BodyWidth - 12, P));
        dc.DrawRectangle(blush, null, new Rect(l + 6, t + 2, BodyWidth - 12, P));
        dc.DrawRectangle(rose, null, new Rect(l + 8, t + 4, BodyWidth - 16, P));

        // Left border (split when Left tail is active)
        if (_tailEdge == MiniPlayerTailEdge.Left)
        {
            double openT = _tailCenterLocal - 6;
            double openB = _tailCenterLocal + 6;
            if (openT > t + 6)
            {
                dc.DrawRectangle(dark, null, new Rect(l, t + 6, P, openT - (t + 6)));
                dc.DrawRectangle(blush, null, new Rect(l + 2, t + 6, P, openT - (t + 6)));
                if (openT > t + 8) dc.DrawRectangle(rose, null, new Rect(l + 4, t + 8, P, openT - (t + 8)));
            }
            if (b - 6 > openB)
            {
                dc.DrawRectangle(dark, null, new Rect(l, openB, P, (b - 6) - openB));
                dc.DrawRectangle(blush, null, new Rect(l + 2, openB, P, (b - 6) - openB));
                if (b - 8 > openB) dc.DrawRectangle(rose, null, new Rect(l + 4, openB, P, (b - 8) - openB));
            }
        }
        else
        {
            dc.DrawRectangle(dark, null, new Rect(l, t + 6, P, BodyHeight - 12));
            dc.DrawRectangle(blush, null, new Rect(l + 2, t + 6, P, BodyHeight - 12));
            dc.DrawRectangle(rose, null, new Rect(l + 4, t + 8, P, BodyHeight - 16));
        }

        // Right border (split when Right tail is active)
        if (_tailEdge == MiniPlayerTailEdge.Right)
        {
            double openT = _tailCenterLocal - 6;
            double openB = _tailCenterLocal + 6;
            if (openT > t + 6)
            {
                dc.DrawRectangle(dark, null, new Rect(r - P, t + 6, P, openT - (t + 6)));
                dc.DrawRectangle(blush, null, new Rect(r - 4, t + 6, P, openT - (t + 6)));
                if (openT > t + 8) dc.DrawRectangle(rose, null, new Rect(r - 6, t + 8, P, openT - (t + 8)));
            }
            if (b - 6 > openB)
            {
                dc.DrawRectangle(dark, null, new Rect(r - P, openB, P, (b - 6) - openB));
                dc.DrawRectangle(blush, null, new Rect(r - 4, openB, P, (b - 6) - openB));
                if (b - 8 > openB) dc.DrawRectangle(rose, null, new Rect(r - 6, openB, P, (b - 8) - openB));
            }
        }
        else
        {
            dc.DrawRectangle(dark, null, new Rect(r - P, t + 6, P, BodyHeight - 12));
            dc.DrawRectangle(blush, null, new Rect(r - 4, t + 6, P, BodyHeight - 12));
            dc.DrawRectangle(rose, null, new Rect(r - 6, t + 8, P, BodyHeight - 16));
        }

        // Bottom border (split when Bottom tail is active)
        if (_tailEdge == MiniPlayerTailEdge.Bottom)
        {
            double openL = _tailCenterLocal - 8;
            double openR = _tailCenterLocal + 8;
            if (openL > l + 6)
            {
                dc.DrawRectangle(dark, null, new Rect(l + 6, b - P, openL - (l + 6), P));
                dc.DrawRectangle(blush, null, new Rect(l + 6, b - 4, openL - (l + 6), P));
                if (openL > l + 8) dc.DrawRectangle(rose, null, new Rect(l + 8, b - 6, openL - (l + 8), P));
            }
            if (r - 6 > openR)
            {
                dc.DrawRectangle(dark, null, new Rect(openR, b - P, (r - 6) - openR, P));
                dc.DrawRectangle(blush, null, new Rect(openR, b - 4, (r - 6) - openR, P));
                if (r - 8 > openR) dc.DrawRectangle(rose, null, new Rect(openR, b - 6, (r - 8) - openR, P));
            }
        }
        else
        {
            dc.DrawRectangle(dark, null, new Rect(l + 6, b - P, BodyWidth - 12, P));
            dc.DrawRectangle(blush, null, new Rect(l + 6, b - 4, BodyWidth - 12, P));
            dc.DrawRectangle(rose, null, new Rect(l + 8, b - 6, BodyWidth - 16, P));
        }

        // Stepped outer corners
        DrawCornerSteps(dc, dark, l, t, +1, +1);
        DrawCornerSteps(dc, dark, r - P, t, -1, +1);
        DrawCornerSteps(dc, dark, l, b - P, +1, -1);
        DrawCornerSteps(dc, dark, r - P, b - P, -1, -1);

        // Blush inner corner pixels
        dc.DrawRectangle(blush, null, new Rect(l + 4, t + 4, P, P));
        dc.DrawRectangle(blush, null, new Rect(r - 6, t + 4, P, P));
        dc.DrawRectangle(blush, null, new Rect(l + 4, b - 6, P, P));
        dc.DrawRectangle(blush, null, new Rect(r - 6, b - 6, P, P));

        // Cyan corner glints
        dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(l + 6, t + 6, P, P));
        dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(r - 8, t + 6, P, P));
        dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(l + 6, b - 8, P, P));
        dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(r - 8, b - 8, P, P));

        // Dynamic 3-way pixel speech tail
        if (_tailEdge == MiniPlayerTailEdge.Bottom)
            DrawBottomTail(dc, _tailCenterLocal, b, dark, blush);
        else if (_tailEdge == MiniPlayerTailEdge.Left)
            DrawSideTail(dc, l, _tailCenterLocal, dirX: -1, dark, blush);
        else
            DrawSideTail(dc, r, _tailCenterLocal, dirX: +1, dark, blush);
    }

    private static void DrawBottomTail(DrawingContext dc, double tx, double b, Brush dark, Brush blush)
    {
        dc.DrawRectangle(dark, null, new Rect(tx - 6, b - 6, 12, 6));
        dc.DrawRectangle(blush, null, new Rect(tx - 8, b - 4, P, 4));
        dc.DrawRectangle(blush, null, new Rect(tx + 6, b - 4, P, 4));
        dc.DrawRectangle(dark, null, new Rect(tx - 10, b - 2, P, 2));
        dc.DrawRectangle(dark, null, new Rect(tx + 8, b - 2, P, 2));

        int[] halfWidths = [6, 4, 2];
        for (int row = 0; row < halfWidths.Length; row++)
        {
            int hw = halfWidths[row];
            double ry = b + row * 3;
            if (row < halfWidths.Length - 1)
            {
                dc.DrawRectangle(dark, null, new Rect(tx - hw + 2, ry, Math.Max(0, (hw - 2) * 2), 3));
                dc.DrawRectangle(blush, null, new Rect(tx - hw, ry, P, 3));
                dc.DrawRectangle(blush, null, new Rect(tx + hw - P, ry, P, 3));
                dc.DrawRectangle(dark, null, new Rect(tx - hw - P, ry, P, 3));
                dc.DrawRectangle(dark, null, new Rect(tx + hw, ry, P, 3));
            }
            else
            {
                dc.DrawRectangle(blush, null, new Rect(tx - 2, ry, 4, 2));
                dc.DrawRectangle(dark, null, new Rect(tx - 4, ry, P, 2));
                dc.DrawRectangle(dark, null, new Rect(tx + 2, ry, P, 2));
                dc.DrawRectangle(dark, null, new Rect(tx - 2, ry + 2, 4, 2));
            }
        }
    }

    private static void DrawSideTail(DrawingContext dc, double edgeX, double ty, int dirX, Brush dark, Brush blush)
    {
        // Seamless interior bridge into the side opening
        double bridgeX = dirX < 0 ? edgeX : edgeX - 6;
        dc.DrawRectangle(dark, null, new Rect(bridgeX, ty - 4, 6, 8));

        double blushBridgeX = dirX < 0 ? edgeX : edgeX - 4;
        dc.DrawRectangle(blush, null, new Rect(blushBridgeX, ty - 6, 4, P));
        dc.DrawRectangle(blush, null, new Rect(blushBridgeX, ty + 4, 4, P));

        int[] halfHeights = [4, 2];
        for (int col = 0; col < halfHeights.Length; col++)
        {
            int hh = halfHeights[col];
            double rx = dirX < 0 ? edgeX - (col + 1) * 3 : edgeX + col * 3;
            if (col == 0)
            {
                dc.DrawRectangle(dark, null, new Rect(rx, ty - hh + 2, 3, Math.Max(0, (hh - 2) * 2)));
                dc.DrawRectangle(blush, null, new Rect(rx, ty - hh, 3, P));
                dc.DrawRectangle(blush, null, new Rect(rx, ty + hh - P, 3, P));
                dc.DrawRectangle(dark, null, new Rect(rx, ty - hh - P, 3, P));
                dc.DrawRectangle(dark, null, new Rect(rx, ty + hh, 3, P));
            }
            else
            {
                double tipX = dirX < 0 ? rx + 1 : rx;
                dc.DrawRectangle(blush, null, new Rect(tipX, ty - 2, 2, 4));
                dc.DrawRectangle(dark, null, new Rect(tipX, ty - 4, 2, P));
                dc.DrawRectangle(dark, null, new Rect(tipX, ty + 2, 2, P));
                double outerTipX = dirX < 0 ? tipX - 2 : tipX + 2;
                dc.DrawRectangle(dark, null, new Rect(outerTipX, ty - 2, 2, 4));
            }
        }
    }

    private static void DrawCornerSteps(DrawingContext dc, Brush brush, double cornerX, double cornerY, int dirX, int dirY)
    {
        dc.DrawRectangle(brush, null, new Rect(cornerX + dirX * 4, cornerY + dirY * 2, P, P));
        dc.DrawRectangle(brush, null, new Rect(cornerX + dirX * 2, cornerY + dirY * 4, P, P));
    }

    private void DrawMiniAlbumJacket(DrawingContext dc, Rect artRect)
    {
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
            // Flush 44x44 mini pixel vinyl disc fallback
            dc.DrawRectangle(PixelPalette.PanelPlumBrush, null, artRect);
            double cx = artRect.X + artRect.Width * 0.5;
            double cy = artRect.Y + artRect.Height * 0.5;
            dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(cx - 14, cy - 12, 28, 24));
            dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(cx - 12, cy - 14, 24, 28));
            dc.DrawRectangle(PixelPalette.PlumBorderBrush, null, new Rect(cx - 12, cy - 10, 24, 20));
            dc.DrawRectangle(PixelPalette.PlumBorderBrush, null, new Rect(cx - 10, cy - 12, 20, 24));
            dc.DrawRectangle(PixelPalette.GhostCyanBrush, null, new Rect(cx - 10, cy - 6, 2, 6));
            dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(cx - 6, cy - 10, 6, 2));
            dc.DrawRectangle(PixelPalette.SakuraPinkBrush, null, new Rect(cx - 5, cy - 5, 10, 10));
            dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(cx - 2, cy - 2, 4, 4));
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
        double dpi)
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
            const double endPause = 2.4;
            const double speedPxPerSec = 30.0;
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
        }

        dc.PushClip(new RectangleGeometry(viewport));
        double drawX = viewport.X + offsetX;
        double drawY = viewport.Y + Math.Floor((viewport.Height - ft.Height) * 0.5);

        var shadowFt = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, fontSize, PixelPalette.OutlineDarkBrush, dpi);
        dc.DrawText(shadowFt, new Point(drawX + 1, drawY + 1));
        dc.DrawText(ft, new Point(drawX, drawY));
        dc.Pop();
    }

    private void DrawSlimProgressBar(DrawingContext dc)
    {
        double barX = RightAreaLeft + 6.0;
        double barY = BodyTop + 41.0;
        const double barW = 156.0;
        const double barH = 5.0;

        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, new Rect(barX - 1, barY - 1, barW + 2, barH + 2));
        dc.DrawRectangle(PixelPalette.TrackPlumBrush, null, new Rect(barX, barY, barW, barH));

        double fraction = _displayDurationSec > 0 ? Math.Clamp(_displayPositionSec / _displayDurationSec, 0.0, 1.0) : 0.0;
        double fillW = Math.Round(barW * fraction);

        if (fillW > 0)
        {
            const double segW = 3.0;
            const double step = 4.0;
            for (double sx = 0; sx < fillW; sx += step)
            {
                double drawW = Math.Min(segW, fillW - sx);
                if (drawW <= 0) break;
                double t = (sx + drawW * 0.5) / barW;
                Brush b = t < 0.5 ? PixelPalette.PinkMidBrush
                    : t < 0.85 ? PixelPalette.SakuraPinkBrush
                    : PixelPalette.BlushLightBrush;
                dc.DrawRectangle(b, null, new Rect(barX + sx, barY + 1, drawW, barH - 2));
            }

            double tipX = Math.Clamp(barX + fillW - 2, barX, barX + barW - 2);
            dc.DrawRectangle(PixelPalette.CyanGlintBrush, null, new Rect(tipX, barY, 2, barH));
        }
    }

    private void DrawTransportButtons(DrawingContext dc)
    {
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
            : (hovered || pressed)
                ? PixelPalette.BerryRoseBrush
                : PixelPalette.PanelPlumBrush;

        Brush iconBrush = !enabled
            ? PixelPalette.PlumBorderBrush
            : hovered || pressed
                ? PixelPalette.CreamWhiteBrush
                : featured
                    ? (_snapshot.IsMusicPlaying ? PixelPalette.GhostCyanBrush : PixelPalette.BlushGlowBrush)
                    : PixelPalette.BlushLightBrush;

        dc.DrawRectangle(PixelPalette.OutlineDarkBrush, null, r);
        dc.DrawRectangle(borderBrush, null, new Rect(r.X + 2, r.Y, r.Width - 4, P));
        dc.DrawRectangle(borderBrush, null, new Rect(r.X + 2, r.Bottom - P, r.Width - 4, P));
        dc.DrawRectangle(borderBrush, null, new Rect(r.X, r.Y + 2, P, r.Height - 4));
        dc.DrawRectangle(borderBrush, null, new Rect(r.Right - P, r.Y + 2, P, r.Height - 4));
        dc.DrawRectangle(fillBrush, null, new Rect(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4));

        double cx = Math.Floor(r.X + r.Width * 0.5);
        double cy = Math.Floor(r.Y + r.Height * 0.5) + (pressed ? 1.0 : 0.0);

        switch (kind)
        {
            case MediaTransportButton.Previous:
                dc.DrawRectangle(iconBrush, null, new Rect(cx - 5, cy - 4, 2, 8));
                dc.DrawRectangle(iconBrush, null, new Rect(cx - 2, cy - 1, 2, 2));
                dc.DrawRectangle(iconBrush, null, new Rect(cx, cy - 3, 2, 6));
                dc.DrawRectangle(iconBrush, null, new Rect(cx + 2, cy - 4, 2, 8));
                break;
            case MediaTransportButton.PlayPause:
                if (_snapshot.IsMusicPlaying)
                {
                    dc.DrawRectangle(iconBrush, null, new Rect(cx - 4, cy - 4, 3, 8));
                    dc.DrawRectangle(iconBrush, null, new Rect(cx + 1, cy - 4, 3, 8));
                }
                else
                {
                    dc.DrawRectangle(iconBrush, null, new Rect(cx - 3, cy - 5, 2, 10));
                    dc.DrawRectangle(iconBrush, null, new Rect(cx - 1, cy - 3, 2, 6));
                    dc.DrawRectangle(iconBrush, null, new Rect(cx + 1, cy - 2, 2, 4));
                    dc.DrawRectangle(iconBrush, null, new Rect(cx + 3, cy - 1, 2, 2));
                }
                break;
            case MediaTransportButton.Next:
                dc.DrawRectangle(iconBrush, null, new Rect(cx - 4, cy - 4, 2, 8));
                dc.DrawRectangle(iconBrush, null, new Rect(cx - 2, cy - 3, 2, 6));
                dc.DrawRectangle(iconBrush, null, new Rect(cx, cy - 1, 2, 2));
                dc.DrawRectangle(iconBrush, null, new Rect(cx + 3, cy - 4, 2, 8));
                break;
        }
    }

    private static BitmapSource? CreateSoftBackdrop(BitmapSource source)
    {
        try
        {
            var small = new TransformedBitmap(source, new ScaleTransform(22.0 / Math.Max(1, source.PixelWidth), 12.0 / Math.Max(1, source.PixelHeight)));
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
                ctx.DrawImage(small, new Rect(0, 0, 64, 32));
            }
            var rtb = new RenderTargetBitmap(64, 32, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
        catch
        {
            return null;
        }
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
