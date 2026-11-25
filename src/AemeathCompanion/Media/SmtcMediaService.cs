using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace AemeathCompanion.Media;

public sealed class MediaSnapshot
{
    public bool IsMusicPlaying { get; init; }
    public bool IsMusicPaused { get; init; }
    public bool HasMusicSession { get; init; }
    public bool CanPlayPause { get; init; }
    public bool CanSkipPrevious { get; init; }
    public bool CanSkipNext { get; init; }
    public string Title { get; init; } = "Unknown track";
    public string Artist { get; init; } = "Unknown artist";
    public string AlbumTitle { get; init; } = string.Empty;
    public string SourceApp { get; init; } = string.Empty;
    public string StatusSummary { get; init; } = "No music session";
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTimeOffset TimelineUpdatedAt { get; init; } = DateTimeOffset.MinValue;
    public BitmapSource? AlbumArt { get; init; }
    public Color AlbumTint { get; init; } = Color.FromRgb(0x3B, 0x26, 0x3B);
}

/// <summary>
/// Subscribes directly to Windows Runtime SMTC (GlobalSystemMediaTransportControlsSessionManager),
/// filters out non-music sessions (games, videos, meetings), and decodes album artwork + dominant tint.
/// </summary>
public sealed class SmtcMediaService : IDisposable
{
    private readonly Func<IReadOnlyList<string>> _browserTitleProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _trackedSession;
    private CancellationTokenSource? _cts;
    private string _lastArtKey = string.Empty;
    private BitmapSource? _cachedArt;
    private Color _cachedTint = Color.FromRgb(0x3B, 0x26, 0x3B);
    private bool _disposed;

    public MediaSnapshot Current { get; private set; } = new();
    public event Action<MediaSnapshot>? SnapshotUpdated;

    public SmtcMediaService(Func<IReadOnlyList<string>> browserTitleProvider)
    {
        _browserTitleProvider = browserTitleProvider;
    }

    public async Task StartAsync()
    {
        if (_disposed) return;
        _cts = new CancellationTokenSource();

        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += OnSessionsChanged;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            await RefreshAsync();

            _ = Task.Run(() => PollLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Current = new MediaSnapshot { StatusSummary = $"SMTC unavailable: {ex.Message}" };
            SnapshotUpdated?.Invoke(Current);
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1200));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct)) break;
                await RefreshAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore transient WinRT COM errors when a media app exits mid-query
            }
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        => _ = RefreshAsync();

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        => _ = RefreshAsync();

    private void OnSessionEvent(GlobalSystemMediaTransportControlsSession sender, object args)
        => _ = RefreshAsync();

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_trackedSession, session)) return;

        if (_trackedSession is not null)
        {
            try
            {
                _trackedSession.PlaybackInfoChanged -= OnSessionEvent;
                _trackedSession.MediaPropertiesChanged -= OnSessionEvent;
                _trackedSession.TimelinePropertiesChanged -= OnSessionEvent;
            }
            catch { }
        }

        _trackedSession = session;

        if (_trackedSession is not null)
        {
            try
            {
                _trackedSession.PlaybackInfoChanged += OnSessionEvent;
                _trackedSession.MediaPropertiesChanged += OnSessionEvent;
                _trackedSession.TimelinePropertiesChanged += OnSessionEvent;
            }
            catch { }
        }
    }

    public async Task RefreshAsync()
    {
        if (_disposed || _manager is null) return;
        if (!await _refreshLock.WaitAsync(0)) return;

        try
        {
            var browserTitles = _browserTitleProvider();
            var allSessions = _manager.GetSessions();
            var currentSession = _manager.GetCurrentSession();

            // Put current session first, followed by all other sessions
            var ordered = new List<GlobalSystemMediaTransportControlsSession>(allSessions.Count);
            if (currentSession is not null) ordered.Add(currentSession);
            foreach (var s in allSessions)
            {
                if (currentSession is null || !string.Equals(s.SourceAppUserModelId, currentSession.SourceAppUserModelId, StringComparison.OrdinalIgnoreCase))
                    ordered.Add(s);
            }

            (GlobalSystemMediaTransportControlsSession Session, GlobalSystemMediaTransportControlsSessionMediaProperties Props, GlobalSystemMediaTransportControlsSessionPlaybackInfo Info, GlobalSystemMediaTransportControlsSessionTimelineProperties Timeline, MusicFilterDecision Decision)? bestPlaying = null;
            (GlobalSystemMediaTransportControlsSession Session, GlobalSystemMediaTransportControlsSessionMediaProperties Props, GlobalSystemMediaTransportControlsSessionPlaybackInfo Info, GlobalSystemMediaTransportControlsSessionTimelineProperties Timeline, MusicFilterDecision Decision)? bestPaused = null;
            string? lastIgnoredReason = null;

            foreach (var session in ordered)
            {
                try
                {
                    var info = session.GetPlaybackInfo();
                    var status = info.PlaybackStatus;
                    if (status is not (GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                        or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused))
                        continue;

                    var props = await session.TryGetMediaPropertiesAsync();
                    if (props is null) continue;

                    var timeline = session.GetTimelineProperties();
                    var duration = timeline.EndTime > timeline.StartTime
                        ? timeline.EndTime - timeline.StartTime
                        : TimeSpan.Zero;

                    var candidate = new MediaSessionCandidate(
                        SourceAppId: session.SourceAppUserModelId ?? string.Empty,
                        PlaybackType: MapMediaKind(info.PlaybackType),
                        IsPlaying: status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                        IsPaused: status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused,
                        Title: props.Title ?? string.Empty,
                        Artist: props.Artist ?? string.Empty,
                        AlbumTitle: props.AlbumTitle ?? string.Empty,
                        Duration: duration);

                    var decision = MusicSessionFilter.Evaluate(candidate, browserTitles);
                    if (decision.IsMusic)
                    {
                        if (candidate.IsPlaying && bestPlaying is null)
                            bestPlaying = (session, props, info, timeline, decision);
                        else if (candidate.IsPaused && bestPaused is null)
                            bestPaused = (session, props, info, timeline, decision);
                    }
                    else if (candidate.IsPlaying && lastIgnoredReason is null)
                    {
                        lastIgnoredReason = decision.Reason;
                    }
                }
                catch
                {
                    // Session may have just closed
                }
            }

            var chosen = bestPlaying ?? bestPaused;
            if (chosen is null)
            {
                AttachSession(null);
                var empty = new MediaSnapshot
                {
                    HasMusicSession = false,
                    IsMusicPlaying = false,
                    IsMusicPaused = false,
                    StatusSummary = lastIgnoredReason ?? "No music playing"
                };
                Current = empty;
                SnapshotUpdated?.Invoke(empty);
                return;
            }

            var (winSession, winProps, winInfo, winTimeline, winDecision) = chosen.Value;
            AttachSession(winSession);

            string title = string.IsNullOrWhiteSpace(winProps.Title) ? "Unknown track" : winProps.Title.Trim();
            string artist = string.IsNullOrWhiteSpace(winProps.Artist) ? "Unknown artist" : winProps.Artist.Trim();
            string album = (winProps.AlbumTitle ?? string.Empty).Trim();
            string appShort = MusicSessionFilter.ShortAppName(winSession.SourceAppUserModelId ?? string.Empty);

            string artKey = $"{appShort}|{title}|{artist}|{album}|{winProps.Thumbnail is not null}";
            if (!string.Equals(artKey, _lastArtKey, StringComparison.Ordinal) || (_cachedArt is null && winProps.Thumbnail is not null))
            {
                _lastArtKey = artKey;
                (_cachedArt, _cachedTint) = await DecodeThumbnailAsync(winProps.Thumbnail);
            }

            var dur = winTimeline.EndTime > winTimeline.StartTime
                ? winTimeline.EndTime - winTimeline.StartTime
                : TimeSpan.Zero;
            var pos = winTimeline.Position >= winTimeline.StartTime
                ? winTimeline.Position - winTimeline.StartTime
                : TimeSpan.Zero;

            bool isPlaying = winInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // Advance position using LastUpdatedTime when playing
            if (isPlaying && dur > TimeSpan.Zero && winTimeline.LastUpdatedTime != DateTimeOffset.MinValue)
            {
                var delta = DateTimeOffset.UtcNow - winTimeline.LastUpdatedTime;
                if (delta > TimeSpan.Zero && delta < TimeSpan.FromHours(1))
                    pos += delta;
            }
            if (dur > TimeSpan.Zero && pos > dur) pos = dur;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            var updatedAt = DateTimeOffset.UtcNow;

            var controls = winInfo.Controls;
            bool canPlayPause = controls is null ||
                                controls.IsPlayPauseToggleEnabled ||
                                controls.IsPlayEnabled ||
                                controls.IsPauseEnabled;
            bool canPrev = controls is null || controls.IsPreviousEnabled;
            bool canNext = controls is null || controls.IsNextEnabled;

            var snap = new MediaSnapshot
            {
                HasMusicSession = true,
                IsMusicPlaying = isPlaying,
                IsMusicPaused = !isPlaying,
                CanPlayPause = canPlayPause,
                CanSkipPrevious = canPrev,
                CanSkipNext = canNext,
                Title = title,
                Artist = artist,
                AlbumTitle = album,
                SourceApp = appShort,
                StatusSummary = isPlaying ? $"♪ {appShort}" : $"❚❚ {appShort} ({winDecision.Reason})",
                Position = pos,
                Duration = dur,
                TimelineUpdatedAt = updatedAt,
                AlbumArt = _cachedArt,
                AlbumTint = _cachedTint
            };

            Current = snap;
            SnapshotUpdated?.Invoke(snap);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static CandidateMediaKind MapMediaKind(MediaPlaybackType? type) => type switch
    {
        MediaPlaybackType.Music => CandidateMediaKind.Music,
        MediaPlaybackType.Video => CandidateMediaKind.Video,
        MediaPlaybackType.Image => CandidateMediaKind.Image,
        _ => CandidateMediaKind.Unknown
    };

    public async Task<bool> TogglePlayPauseAsync()
    {
        var session = _trackedSession;
        if (_disposed || session is null) return false;
        try
        {
            bool ok = await session.TryTogglePlayPauseAsync();
            if (!ok)
            {
                var status = session.GetPlaybackInfo().PlaybackStatus;
                ok = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    ? await session.TryPauseAsync()
                    : await session.TryPlayAsync();
            }
            _ = RefreshSoonAsync();
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SkipPreviousAsync()
    {
        var session = _trackedSession;
        if (_disposed || session is null) return false;
        try
        {
            bool ok = await session.TrySkipPreviousAsync();
            _ = RefreshSoonAsync();
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SkipNextAsync()
    {
        var session = _trackedSession;
        if (_disposed || session is null) return false;
        try
        {
            bool ok = await session.TrySkipNextAsync();
            _ = RefreshSoonAsync();
            return ok;
        }
        catch
        {
            return false;
        }
    }

    private async Task RefreshSoonAsync()
    {
        try
        {
            await Task.Delay(120);
            await RefreshAsync();
        }
        catch { }
    }

    private static async Task<(BitmapSource? Bitmap, Color Tint)> DecodeThumbnailAsync(IRandomAccessStreamReference? thumbRef)
    {
        var fallbackTint = Color.FromRgb(0x3B, 0x26, 0x3B);
        if (thumbRef is null) return (null, fallbackTint);

        try
        {
            using var stream = await thumbRef.OpenReadAsync();
            if (stream.Size == 0) return (null, fallbackTint);

            using var netStream = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            await netStream.CopyToAsync(ms);
            byte[] bytes = ms.ToArray();

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.DecodePixelWidth = 256;
            bmp.EndInit();
            bmp.Freeze();

            var tint = ExtractDominantTint(bmp, fallbackTint);
            return (bmp, tint);
        }
        catch
        {
            return (null, fallbackTint);
        }
    }

    private static Color ExtractDominantTint(BitmapSource bmp, Color fallback)
    {
        try
        {
            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth;
            int h = converted.PixelHeight;
            if (w <= 0 || h <= 0) return fallback;

            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            converted.CopyPixels(pixels, stride, 0);

            double rSum = 0, gSum = 0, bSum = 0, weightSum = 0;
            int step = Math.Max(1, Math.Min(w, h) / 24);

            for (int y = 0; y < h; y += step)
            {
                for (int x = 0; x < w; x += step)
                {
                    int idx = y * stride + x * 4;
                    byte b = pixels[idx];
                    byte g = pixels[idx + 1];
                    byte r = pixels[idx + 2];
                    byte a = pixels[idx + 3];
                    if (a < 128) continue;

                    int max = Math.Max(r, Math.Max(g, b));
                    int min = Math.Min(r, Math.Min(g, b));
                    double sat = max > 0 ? (max - min) / (double)max : 0;
                    double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;

                    // Favor colorful mid-tones over pure black/white borders
                    double wgt = (0.25 + sat * 1.6) * (1.0 - Math.Abs(lum - 0.52) * 1.2);
                    if (wgt < 0.05) wgt = 0.05;

                    rSum += r * wgt;
                    gSum += g * wgt;
                    bSum += b * wgt;
                    weightSum += wgt;
                }
            }

            if (weightSum <= 0.01) return fallback;
            byte rr = (byte)Math.Clamp(rSum / weightSum, 0, 255);
            byte gg = (byte)Math.Clamp(gSum / weightSum, 0, 255);
            byte bb = (byte)Math.Clamp(bSum / weightSum, 0, 255);
            return Color.FromRgb(rr, gg, bb);
        }
        catch
        {
            return fallback;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        AttachSession(null);
        if (_manager is not null)
        {
            try
            {
                _manager.SessionsChanged -= OnSessionsChanged;
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            }
            catch { }
        }
        _refreshLock.Dispose();
        _cts?.Dispose();
    }
}
