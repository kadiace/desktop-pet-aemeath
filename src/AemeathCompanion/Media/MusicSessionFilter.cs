namespace AemeathCompanion.Media;

public enum CandidateMediaKind
{
    Unknown,
    Music,
    Video,
    Image
}

public sealed record MediaSessionCandidate(
    string SourceAppId,
    CandidateMediaKind PlaybackType,
    bool IsPlaying,
    bool IsPaused,
    string Title,
    string Artist,
    string AlbumTitle,
    TimeSpan Duration);

public sealed record MusicFilterDecision(bool IsMusic, string Reason);

/// <summary>
/// Filters Windows SMTC sessions so only genuine music playback triggers companion music mode.
/// Rejects games, voice/meeting apps, video players playing movies, and regular video tabs.
/// </summary>
public static class MusicSessionFilter
{
    private static readonly string[] DedicatedMusicAppTokens =
    [
        "spotify",
        "applemusic",
        "apple.music",
        "itunes",
        "melon",
        "bugs",
        "genie",
        "flo",
        "navervibe",
        "vibe",
        "tidal",
        "qobuz",
        "deezer",
        "amazonmusic",
        "amazon.music",
        "foobar2000",
        "aimp",
        "musicbee",
        "winamp",
        "youtubemusic",
        "ytmusic",
        "ytmdesktop",
        "pear-desktop",
        "soundcloud",
        "cider",
        "microsoft.zunemusic",
        "dopamine",
        "strawberry",
        "clementine",
        "mediamonkey",
        "plexamp",
        "feishin",
        "supersonic",
        "roon",
        "audacious",
        "bandcamp",
        "alsong",
        "kakaomelon"
    ];

    private static readonly string[] BrowserAppTokens =
    [
        "chrome",
        "msedge",
        "firefox",
        "brave",
        "opera",
        "whale",
        "vivaldi",
        "arc",
        "zen",
        "waterfox",
        "chromium"
    ];

    private static readonly string[] GeneralMediaPlayerTokens =
    [
        "vlc",
        "potplayer",
        "mpv",
        "mpc-hc",
        "mpc-be",
        "wmplayer",
        "microsoft.zunevideo",
        "kmplayer",
        "gom"
    ];

    private static readonly string[] BlockedAppTokens =
    [
        "steam",
        "epicgames",
        "riotclient",
        "league",
        "valorant",
        "genshin",
        "wuthering",
        "client-win64-shipping",
        "unreal",
        "unity",
        "minecraft",
        "overwatch",
        "battle.net",
        "starrail",
        "zenless",
        "discord",
        "teams",
        "zoom",
        "obs64",
        "obs32",
        "netflix",
        "disney",
        "primevideo",
        "twitch",
        "chzzk",
        "afreeca",
        "soop",
        "wallpaper32",
        "wallpaper64",
        "xbox",
        "gamebar"
    ];

    private static readonly string[] BrowserMusicServiceMarkers =
    [
        "youtube music",
        "spotify",
        "soundcloud",
        "apple music",
        "melon",
        "멜론",
        "bugs",
        "벅스",
        "genie",
        "지니",
        "flo",
        "naver vibe",
        "vibe",
        "bandcamp",
        "tidal",
        "deezer",
        "qobuz",
        "amazon music",
        "last.fm",
        "mixcloud"
    ];

    private static readonly string[] BlockedVideoSiteMarkers =
    [
        "twitch",
        "netflix",
        "chzzk",
        "치지직",
        "soop",
        "아프리카tv",
        "disney+",
        "coupang play",
        "쿠팡플레이",
        "tving",
        "티빙",
        "wavve",
        "웨이브",
        "watcha",
        "왓챠",
        "prime video",
        "bilibili",
        "nicovideo",
        "vimeo",
        "tiktok"
    ];

    public static MusicFilterDecision Evaluate(
        MediaSessionCandidate candidate,
        IReadOnlyList<string>? openBrowserTitles = null)
    {
        string app = (candidate.SourceAppId ?? string.Empty).Trim();
        string appLower = app.ToLowerInvariant();
        string title = (candidate.Title ?? string.Empty).Trim();
        string artist = (candidate.Artist ?? string.Empty).Trim();
        string album = (candidate.AlbumTitle ?? string.Empty).Trim();

        if (candidate.PlaybackType == CandidateMediaKind.Image)
            return new(false, "Ignored: Image media");

        // 1. Always block known games, game launchers, voice chat, and video streaming apps
        if (ContainsAny(appLower, BlockedAppTokens))
            return new(false, $"Ignored: Non-music app ({ShortAppName(app)})");

        // 2. Dedicated music players
        if (ContainsAny(appLower, DedicatedMusicAppTokens))
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
                return new(false, $"Ignored: Empty metadata ({ShortAppName(app)})");
            return new(true, $"Music app ({ShortAppName(app)})");
        }

        // 3. General media players (VLC, PotPlayer, etc.) — allow only when SMTC explicitly reports Music
        if (ContainsAny(appLower, GeneralMediaPlayerTokens))
        {
            if (candidate.PlaybackType == CandidateMediaKind.Music &&
                (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(artist)))
            {
                return new(true, $"Media player music ({ShortAppName(app)})");
            }
            return new(false, $"Ignored: Video/non-music playback ({ShortAppName(app)})");
        }

        // 4. Web browsers & browser-hosted PWAs
        bool isBrowser = ContainsAny(appLower, BrowserAppTokens) || appLower.Contains("_crx_");
        if (isBrowser)
        {
            return EvaluateBrowserSession(candidate, openBrowserTitles);
        }

        // 5. Unknown/other desktop apps (e.g., games that hook SMTC or custom audio apps)
        // Strict positive check: must explicitly declare PlaybackType == Music AND provide song metadata
        if (candidate.PlaybackType == CandidateMediaKind.Music &&
            !string.IsNullOrWhiteSpace(title) &&
            !string.IsNullOrWhiteSpace(artist) &&
            (candidate.Duration == TimeSpan.Zero || candidate.Duration >= TimeSpan.FromSeconds(15)))
        {
            return new(true, $"SMTC Music ({ShortAppName(app)})");
        }

        return new(false, $"Ignored: Non-music session ({ShortAppName(app)})");
    }

    private static MusicFilterDecision EvaluateBrowserSession(
        MediaSessionCandidate candidate,
        IReadOnlyList<string>? openBrowserTitles)
    {
        string title = (candidate.Title ?? string.Empty).Trim();
        string artist = (candidate.Artist ?? string.Empty).Trim();
        string album = (candidate.AlbumTitle ?? string.Empty).Trim();
        string combined = $"{title} {artist} {album}".ToLowerInvariant();

        if (ContainsAny(combined, BlockedVideoSiteMarkers))
            return new(false, "Ignored: Video/stream site in browser");

        bool hasMusicTab = false;
        bool hasMatchingMusicTab = false;
        bool isRegularYouTubeTab = false;

        if (openBrowserTitles is not null)
        {
            foreach (string rawWinTitle in openBrowserTitles)
            {
                if (string.IsNullOrWhiteSpace(rawWinTitle)) continue;
                string lower = rawWinTitle.ToLowerInvariant();

                if (ContainsAny(lower, BrowserMusicServiceMarkers))
                {
                    hasMusicTab = true;
                    if (!string.IsNullOrWhiteSpace(title) &&
                        lower.Contains(title.ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        hasMatchingMusicTab = true;
                    }
                }
                else if (lower.Contains("- youtube", StringComparison.Ordinal) &&
                         !lower.Contains("youtube music", StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(title) &&
                        lower.Contains(title.ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        isRegularYouTubeTab = true;
                    }
                }
            }
        }

        bool hasAlbum = !string.IsNullOrWhiteSpace(album) &&
                        !album.Equals("youtube", StringComparison.OrdinalIgnoreCase);

        if (isRegularYouTubeTab && !hasAlbum)
            return new(false, "Ignored: Regular YouTube video");

        // Video playback type in browser is rejected unless explicitly tied to a music service tab (e.g., YouTube Music MV)
        if (candidate.PlaybackType == CandidateMediaKind.Video)
        {
            if (hasMatchingMusicTab || (hasMusicTab && hasAlbum))
                return new(true, "Browser music service");
            return new(false, "Ignored: Browser video playback");
        }

        if (candidate.PlaybackType == CandidateMediaKind.Music)
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
                return new(false, "Ignored: Empty browser media");
            if (hasMusicTab || hasAlbum || !string.IsNullOrWhiteSpace(artist))
                return new(true, "Browser music (SMTC Music)");
            return new(false, "Ignored: Browser audio without music metadata");
        }

        // PlaybackType == Unknown in browser (common for YouTube Music / Melon / SoundCloud web)
        if (hasMatchingMusicTab)
            return new(true, "Browser music tab");

        if (hasAlbum && !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(title))
            return new(true, "Browser music (album metadata)");

        if (hasMusicTab && !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(title))
            return new(true, "Browser music service");

        return new(false, "Ignored: Non-music browser audio/video");
    }

    private static bool ContainsAny(string haystack, string[] needles)
    {
        foreach (string needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string ShortAppName(string sourceAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId)) return "Unknown";
        int bang = sourceAppId.IndexOf('!');
        string basePart = bang > 0 ? sourceAppId[..bang] : sourceAppId;
        int sep = Math.Max(basePart.LastIndexOf('\\'), basePart.LastIndexOf('/'));
        if (sep >= 0 && sep < basePart.Length - 1)
            basePart = basePart[(sep + 1)..];
        if (basePart.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            basePart = basePart[..^4];
        int underscore = basePart.IndexOf('_');
        if (underscore > 0 && basePart.Contains('.'))
            basePart = basePart[..underscore];
        return basePart.Length > 26 ? basePart[..26] : basePart;
    }
}
