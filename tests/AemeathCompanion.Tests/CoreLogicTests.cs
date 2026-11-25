using System.IO;
using AemeathCompanion.Engine;
using AemeathCompanion.Media;
using AemeathCompanion.Native;
using AemeathCompanion.Update;

int passed = 0;
int failed = 0;

void Assert(bool condition, string name)
{
    if (condition)
    {
        Console.WriteLine($"[PASS] {name}");
        passed++;
    }
    else
    {
        Console.WriteLine($"[FAIL] {name}");
        failed++;
    }
}

// ============================================================
// 1. Condition 4: MusicSessionFilter Tests
// ============================================================
{
    var spotify = new MediaSessionCandidate("Spotify.exe", CandidateMediaKind.Music, true, false, "Supernova", "aespa", "Armageddon", TimeSpan.FromMinutes(3));
    Assert(MusicSessionFilter.Evaluate(spotify).IsMusic, "Spotify dedicated app accepted");

    var melon = new MediaSessionCandidate("Melon.exe", CandidateMediaKind.Unknown, true, false, "밤양갱", "비비", "밤양갱", TimeSpan.FromMinutes(2.5));
    Assert(MusicSessionFilter.Evaluate(melon).IsMusic, "Melon with Unknown PlaybackType accepted");

    var wuthering = new MediaSessionCandidate("Client-Win64-Shipping.exe", CandidateMediaKind.Unknown, true, false, "Wuthering Waves", "", "", TimeSpan.Zero);
    Assert(!MusicSessionFilter.Evaluate(wuthering).IsMusic, "Wuthering Waves game rejected");

    var genshin = new MediaSessionCandidate("GenshinImpact.exe", CandidateMediaKind.Music, true, false, "BGM", "Hoyoverse", "", TimeSpan.FromMinutes(4));
    Assert(!MusicSessionFilter.Evaluate(genshin).IsMusic, "Blocked game exe rejected even if PlaybackType=Music");

    var discord = new MediaSessionCandidate("Discord.exe", CandidateMediaKind.Unknown, true, false, "Voice Channel", "Discord", "", TimeSpan.Zero);
    Assert(!MusicSessionFilter.Evaluate(discord).IsMusic, "Discord rejected");

    var vlcMovie = new MediaSessionCandidate("vlc.exe", CandidateMediaKind.Video, true, false, "Interstellar.mkv", "", "", TimeSpan.FromHours(2.8));
    Assert(!MusicSessionFilter.Evaluate(vlcMovie).IsMusic, "VLC movie playback rejected");

    var vlcFlac = new MediaSessionCandidate("vlc.exe", CandidateMediaKind.Music, true, false, "Track 01", "Artist", "Album", TimeSpan.FromMinutes(4));
    Assert(MusicSessionFilter.Evaluate(vlcFlac).IsMusic, "VLC music playback accepted");

    var chromeRegularYt = new MediaSessionCandidate("chrome.exe", CandidateMediaKind.Video, true, false, "C# Tutorial 2026", "TechChannel", "", TimeSpan.FromMinutes(20));
    Assert(!MusicSessionFilter.Evaluate(chromeRegularYt, ["C# Tutorial 2026 - YouTube - Google Chrome"]).IsMusic, "Chrome regular YouTube video rejected");

    var chromeRegularYtUnknown = new MediaSessionCandidate("chrome.exe", CandidateMediaKind.Unknown, true, false, "Gaming Highlights", "Streamer", "", TimeSpan.FromMinutes(15));
    Assert(!MusicSessionFilter.Evaluate(chromeRegularYtUnknown, ["Gaming Highlights - YouTube - Google Chrome"]).IsMusic, "Chrome regular YouTube (Unknown type) rejected");

    var chromeYtMusic = new MediaSessionCandidate("chrome.exe", CandidateMediaKind.Unknown, true, false, "Aemeath OST", "Kuro Games", "Digital Ghost", TimeSpan.FromMinutes(3.5));
    Assert(MusicSessionFilter.Evaluate(chromeYtMusic, ["Aemeath OST - YouTube Music - Google Chrome"]).IsMusic, "Chrome YouTube Music accepted");

    var chromeYtMusicBg = new MediaSessionCandidate("chrome.exe", CandidateMediaKind.Unknown, true, false, "Ditto", "NewJeans", "OMG", TimeSpan.FromMinutes(3.1));
    Assert(MusicSessionFilter.Evaluate(chromeYtMusicBg, ["GitHub - Google Chrome"]).IsMusic, "Chrome background YouTube Music with AlbumTitle accepted");

    var chromeSpotifyWeb = new MediaSessionCandidate("msedge.exe", CandidateMediaKind.Music, true, false, "Hype Boy", "NewJeans", "New Jeans", TimeSpan.FromMinutes(3));
    Assert(MusicSessionFilter.Evaluate(chromeSpotifyWeb, ["Spotify – Web Player - Microsoft Edge"]).IsMusic, "Edge Spotify Web accepted");

    var chromeChzzk = new MediaSessionCandidate("chrome.exe", CandidateMediaKind.Music, true, false, "치지직 방송", "CHZZK", "", TimeSpan.Zero);
    Assert(!MusicSessionFilter.Evaluate(chromeChzzk, ["치지직 - Google Chrome"]).IsMusic, "Browser CHZZK stream rejected");

    var chromeTwitch = new MediaSessionCandidate("chrome.exe", CandidateMediaKind.Video, true, false, "LCK Live", "Twitch", "", TimeSpan.Zero);
    Assert(!MusicSessionFilter.Evaluate(chromeTwitch, ["Twitch - Google Chrome"]).IsMusic, "Browser Twitch stream rejected");

    var unknownGame = new MediaSessionCandidate("SomeIndieGame.exe", CandidateMediaKind.Unknown, true, false, "Main Menu", "", "", TimeSpan.Zero);
    Assert(!MusicSessionFilter.Evaluate(unknownGame).IsMusic, "Unknown non-music exe rejected");
}

// ============================================================
// 2. Condition 3: WindowPlatformScanner Z-Order Occlusion Tests
// ============================================================
{
    var monitor = new MonitorBounds(
        new NativeRect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
        new NativeRect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
        IsPrimary: true);
    var monitors = new List<MonitorBounds> { monitor };

    // Front window covers [400..800, 200..700], back window (Task Manager) is [300..1000, 350..850]
    var frontWin = new RawWindowInfo(new nint(100), new NativeRect { Left = 400, Top = 200, Right = 800, Bottom = 700 }, "Chrome", "Chrome_WidgetWin_1", "chrome");
    var taskMgr = new RawWindowInfo(new nint(200), new NativeRect { Left = 300, Top = 350, Right = 1000, Bottom = 850 }, "작업 관리자", "TaskManagerWindow", "Taskmgr");

    var platforms = WindowPlatformScanner.ComputeVisiblePlatforms([frontWin, taskMgr], monitors);

    var chromePlats = platforms.Where(p => p.Hwnd == new nint(100)).ToList();
    var taskMgrPlats = platforms.Where(p => p.Hwnd == new nint(200)).ToList();
    var floorPlats = platforms.Where(p => p.IsWorkAreaFloor).ToList();

    Assert(chromePlats.Count == 1 && chromePlats[0].Left == 400 && chromePlats[0].Right == 800 && chromePlats[0].Top == 200,
        "Front window emits full top platform");
    Assert(taskMgrPlats.Count == 2 &&
           taskMgrPlats.Any(p => p.Left == 300 && p.Right == 400 && p.Top == 350) &&
           taskMgrPlats.Any(p => p.Left == 800 && p.Right == 1000 && p.Top == 350),
        "Partially occluded Task Manager emits only its two visible top-edge segments");
    Assert(floorPlats.Count == 1 && floorPlats[0].Top == 1040,
        "Taskbar work-area floor platform added at Y=1040");
}

// ============================================================
// 3. Condition 3: CompanionController State B Landing, Riding & Drop Tests
// ============================================================
{
    var monitor = new MonitorBounds(
        new NativeRect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
        new NativeRect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
        IsPrimary: true);
    var monitors = new List<MonitorBounds> { monitor };

    var ctrl = new CompanionController(new Random(42));
    ctrl.InitializeFromState(new CompanionPersistedState { Mode = "B", X = 500, Y = 200, SizeMultiplier = 1.0 }, monitors, 1.0);

    var taskMgrPlat = new PlatformSurface(new nint(200), 350, 900, 480, 900, "작업 관리자", false);
    var floorPlat = new PlatformSurface(0, 0, 1920, 1040, 1080, "Taskbar Floor", true);
    var platforms = new List<PlatformSurface> { taskMgrPlat, floorPlat };

    // Simulate gravity fall for 1.5 seconds (dt = 0.016)
    for (int i = 0; i < 95; i++)
        ctrl.Tick(0.016, monitors, platforms, null, 1.0);

    // screen1~7 center body baseline is 4px above bottom (FootOffsetPx = 96.0, hands hook 4px over ledge)
    Assert(!ctrl.IsFalling && ctrl.LandedWindowHwnd == new nint(200) && Math.Abs((ctrl.Y + 96.0) - 480) < 0.5,
        "screen1-7 center body baseline lands flush on Task Manager top edge (Y+96 == 480)");

    // Drag Task Manager +80px right and +30px down -> Aemeath rides along!
    var movedTaskMgr = taskMgrPlat with { Left = 430, Right = 980, Top = 510 };
    ctrl.Tick(0.016, monitors, [movedTaskMgr, floorPlat], null, 1.0);
    Assert(!ctrl.IsFalling && Math.Abs(ctrl.X - 580) < 0.5 && Math.Abs((ctrl.Y + 96.0) - 510) < 0.5,
        "Platform mode rides along smoothly when landed window moves");

    // Close Task Manager -> Aemeath resumes falling and lands on Taskbar Floor (1040)
    ctrl.Tick(0.016, monitors, [floorPlat], null, 1.0);
    Assert(ctrl.IsFalling && ctrl.LandedWindowHwnd == 0,
        "Closing landed window immediately triggers gravity fall");

    for (int i = 0; i < 120; i++)
        ctrl.Tick(0.016, monitors, [floorPlat], null, 1.0);

    Assert(!ctrl.IsFalling && ctrl.LandedWindowHwnd == 0 && Math.Abs((ctrl.Y + 96.0) - 1040) < 0.5,
        "After window closes, falls cleanly onto Taskbar floor (Y+96 == 1040)");

    // Requirement 1: Platform mode must NOT randomly switch screen sprites over time
    string initialScreen = ctrl.ScreenName;
    for (int i = 0; i < 1200; i++) // 60 seconds
        ctrl.Tick(0.05, monitors, [floorPlat], null, 1.0);
    Assert(ctrl.ScreenName == initialScreen,
        "Platform mode keeps current screen sprite without time-based random rotation");

    // screen8 (300x200, body baseline at y=185 -> 15px from bottom -> 85px foot offset) lands flush on window top edge
    ctrl.SelectManualVisual("screen8", monitors, 1.0);
    var metrics8 = ctrl.ActiveMetrics;
    Assert(metrics8.WidthDip == 300.0 && metrics8.HeightDip == 200.0 && ctrl.FootOffsetPx(1.0) == 85.0,
        "screen8 aligns center-to-right body baseline (300x200, y=185, 15px offset -> 85px foot offset)");
    Assert(Math.Abs((ctrl.Y + ctrl.FootOffsetPx(1.0)) - 1040) < 0.5,
        "Switching to screen8 while resting keeps body baseline flush on Taskbar floor");

    // Drop screen8 from above onto Task Manager (Top=480)
    ctrl.BeginDrag(monitors, 1.0);
    ctrl.UpdateDrag(500, 200, monitors, 1.0);
    ctrl.EndDrag(monitors, 1.0);
    for (int i = 0; i < 95; i++)
        ctrl.Tick(0.016, monitors, [taskMgrPlat, floorPlat], null, 1.0);
    Assert(!ctrl.IsFalling && ctrl.LandedWindowHwnd == new nint(200) && Math.Abs((ctrl.Y + 85.0) - 480) < 0.5,
        "screen8 body baseline lands flush on Task Manager top edge (Y+85 == 480)");

    // screen2 and screen4 require zero bottom offset (FootOffsetPx = 100.0)
    Assert(CompanionController.GetSpriteMetrics("screen2").BottomPadDip == 0.0 &&
           CompanionController.GetSpriteMetrics("screen4").BottomPadDip == 0.0 &&
           CompanionController.GetSpriteMetrics("screen1").BottomPadDip == 4.0,
        "screen2 and screen4 have 0px bottom offset while screen1/3/5/6/7 have 4px offset");

    // Stale state.json healing: Mode=A with ManualVisual=screen8 must heal to Platform + screen8
    var healed = new CompanionController(new Random(99));
    healed.InitializeFromState(new CompanionPersistedState { Mode = "A", ManualVisual = "screen8", X = 500, Y = 300 }, monitors, 1.0);
    healed.BeginDrag(monitors, 1.0);
    healed.UpdateDrag(620, 260, monitors, 1.0);
    Assert(healed.Mode == CompanionMode.B && healed.ActiveSpriteName == "screen8" && Math.Abs(healed.X - 620) < 0.5,
        "Stale Mode=A + ManualVisual=screen8 heals to Platform mode and drags with screen8 cleanly");
    healed.EndDrag(monitors, 1.0);
    var exported = healed.ExportState();
    Assert(exported.Mode == "B" && exported.ScreenName == "screen8",
        "ExportState preserves Mode=B and ScreenName=screen8 across restarts");
}

// ============================================================
// 4. Music Mode, Pause Hold & User Interruption Tests
// ============================================================
{
    var monitor = new MonitorBounds(
        new NativeRect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
        new NativeRect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 },
        IsPrimary: true);
    var monitors = new List<MonitorBounds> { monitor };
    var floorPlat = new PlatformSurface(0, 0, 1920, 1040, 1080, "Taskbar Floor", true);

    var ctrl = new CompanionController(new Random(7));
    ctrl.InitializeFromState(new CompanionPersistedState { Mode = "B", X = 500, Y = 120, SizeMultiplier = 1.0 }, monitors, 1.0);

    // Requirement: Platform mode must NOT auto-switch to music mode when music starts,
    // and instead exposes the compact Platform mini player
    ctrl.UpdateMusicState(true, monitors, 1.0);
    Assert(ctrl.Mode == CompanionMode.B && !ctrl.MusicPlaying && !ctrl.ShouldShowBubble && ctrl.ShouldShowPlatformMiniPlayer,
        "Platform mode shows compact mini player (without switching to full Music mode) when music plays");

    // Playing in Platform mode: dragging & clicking keep mini player visible
    ctrl.BeginDrag(monitors, 1.0);
    Assert(ctrl.ShouldShowPlatformMiniPlayer, "Platform mini player stays visible while dragging during active playback");
    ctrl.EndDrag(monitors, 1.0);

    string beforeScreen = ctrl.ScreenName;
    ctrl.HandleSingleClick(monitors, 1.0);
    Assert(ctrl.ScreenName != beforeScreen && ctrl.ShouldShowPlatformMiniPlayer,
        "Platform single-click changes screen sprite and keeps mini player visible while playing");

    // Paused in Platform mode: mini player stays held, then hides immediately on drag or click
    ctrl.UpdateMusicState(false, monitors, 1.0);
    Assert(ctrl.ShouldShowPlatformMiniPlayer && ctrl.PlatformMiniPlayerHeld,
        "Pausing music in Platform mode holds mini player");

    ctrl.BeginDrag(monitors, 1.0);
    Assert(!ctrl.ShouldShowPlatformMiniPlayer, "Dragging while paused immediately hides Platform mini player");
    ctrl.EndDrag(monitors, 1.0);

    // Paused in Platform mode: clicking also hides mini player immediately
    ctrl.UpdateMusicState(true, monitors, 1.0);
    ctrl.UpdateMusicState(false, monitors, 1.0);
    Assert(ctrl.ShouldShowPlatformMiniPlayer, "Mini player re-held after pause");
    ctrl.HandleSingleClick(monitors, 1.0);
    Assert(!ctrl.ShouldShowPlatformMiniPlayer, "Clicking while paused immediately hides Platform mini player");

    // Paused in Platform mode: auto-hides after 5 minutes
    ctrl.UpdateMusicState(true, monitors, 1.0);
    ctrl.UpdateMusicState(false, monitors, 1.0);
    for (int i = 0; i < 6005; i++)
        ctrl.Tick(0.05, monitors, [floorPlat], null, 1.0);
    Assert(!ctrl.ShouldShowPlatformMiniPlayer, "Platform mini player auto-hides after 5 minutes paused");

    // Explicitly selecting Roam while music plays stays in Roam AND displays the horizontal mini player
    ctrl.UpdateMusicState(true, monitors, 1.0);
    ctrl.SetMode(CompanionMode.A, monitors, 1.0);
    ctrl.UpdateMusicState(true, monitors, 1.0);
    Assert(ctrl.Mode == CompanionMode.A && !ctrl.MusicPlaying && !ctrl.ShouldShowBubble && ctrl.ShouldShowPlatformMiniPlayer,
        "Explicitly selecting Roam while music plays stays in Roam and shows the horizontal mini player");

    // In Roam with mini player: dragging & clicking while playing keeps mini player visible
    ctrl.BeginDrag(monitors, 1.0);
    Assert(ctrl.IsDragging && ctrl.Mode == CompanionMode.A && ctrl.ShouldShowPlatformMiniPlayer,
        "Roam mini player stays visible while dragging during active playback");
    ctrl.EndDrag(monitors, 1.0);
    ctrl.HandleSingleClick(monitors, 1.0);
    Assert(ctrl.Mode == CompanionMode.A && ctrl.ShouldShowPlatformMiniPlayer,
        "Roam mini player stays visible on single-click during active playback");

    // Pausing music in Roam mini player holds for 5 minutes and hides immediately on click or drag
    ctrl.UpdateMusicState(false, monitors, 1.0);
    Assert(ctrl.Mode == CompanionMode.A && ctrl.ShouldShowPlatformMiniPlayer && ctrl.PlatformMiniPlayerHeld,
        "Pausing music in Roam mini player holds mini player");
    ctrl.HandleSingleClick(monitors, 1.0);
    Assert(!ctrl.ShouldShowPlatformMiniPlayer,
        "Single-clicking while paused immediately hides Roam mini player");

    // Restarting playback in Roam enters full Music mode, and selecting Roam from Music mode shows Roam mini player
    ctrl.UpdateMusicState(true, monitors, 1.0);
    Assert(ctrl.MusicPlaying && ctrl.ShouldShowBubble && !ctrl.ShouldShowPlatformMiniPlayer,
        "Restarting playback in Roam enters full Music mode");
    ctrl.SetMode(CompanionMode.A, monitors, 1.0);
    Assert(ctrl.Mode == CompanionMode.A && !ctrl.MusicPlaying && !ctrl.ShouldShowBubble && ctrl.ShouldShowPlatformMiniPlayer,
        "Selecting Roam from active Music mode exits Music mode and shows horizontal mini player in Roam");

    ctrl.SetMode(CompanionMode.B, monitors, 1.0);
    Assert(ctrl.Mode == CompanionMode.B && ctrl.ShouldShowPlatformMiniPlayer,
        "Switching Roam -> Platform while music plays immediately shows Platform mini player");

    // Double-clicking in Platform mode while music is actively playing enters full Music mode
    ctrl.HandleDoubleClick(monitors, 1.0);
    Assert(ctrl.MusicPlaying && ctrl.ShouldShowBubble && !ctrl.ShouldShowPlatformMiniPlayer,
        "Double-clicking in Platform mode while music plays switches into full Music mode");

    // Single-clicking in active Music mode must NOT switch to Platform mode
    ctrl.HandleSingleClick(monitors, 1.0);
    Assert(ctrl.MusicPlaying && ctrl.ShouldShowBubble && ctrl.Mode == CompanionMode.A,
        "Single-clicking in active Music mode stays in Music mode (does not jump to Platform)");

    // Double-clicking in Music mode switches cleanly to Platform mode (with mini player active)
    ctrl.HandleDoubleClick(monitors, 1.0);
    Assert(!ctrl.MusicPlaying && !ctrl.ShouldShowBubble && ctrl.Mode == CompanionMode.B && ctrl.ShouldShowPlatformMiniPlayer,
        "Double-clicking in Music mode switches to Platform mode with mini player");

    // Placement test: default above head; falls back to right/left side when monitor top margin is tight
    var workRect = monitor.WorkRect;
    var abovePlace = AemeathCompanion.UI.PlatformMiniPlayerWindow.ComputePlacement(960, 500, 100, 100, 64, workRect, 1.0);
    Assert(abovePlace.TailEdge == AemeathCompanion.UI.MiniPlayerTailEdge.Bottom,
        "Mini player places above head with bottom tail when top room is available");
    var rightPlace = AemeathCompanion.UI.PlatformMiniPlayerWindow.ComputePlacement(180, 90, 100, 100, 30, workRect, 1.0);
    Assert(rightPlace.TailEdge == AemeathCompanion.UI.MiniPlayerTailEdge.Left,
        "Mini player falls back to right side (left tail) when near top-left edge");
    var leftPlace = AemeathCompanion.UI.PlatformMiniPlayerWindow.ComputePlacement(1800, 90, 100, 100, 30, workRect, 1.0);
    Assert(leftPlace.TailEdge == AemeathCompanion.UI.MiniPlayerTailEdge.Right,
        "Mini player falls back to left side (right tail) when near top-right edge");

    // Active drag in Roam must not be interrupted when music starts playing
    ctrl.SetMode(CompanionMode.A, monitors, 1.0);
    ctrl.UpdateMusicState(false, monitors, 1.0);
    ctrl.BeginDrag(monitors, 1.0);
    ctrl.UpdateMusicState(true, monitors, 1.0);
    Assert(ctrl.IsDragging && !ctrl.MusicPlaying && !ctrl.ShouldShowBubble,
        "Active drag is never interrupted by automatic music start");
    ctrl.EndDrag(monitors, 1.0);

    // Return to Platform for explicit Music button test
    ctrl.SetMode(CompanionMode.B, monitors, 1.0);
    ctrl.UpdateMusicState(true, monitors, 1.0);

    // Requirement: Explicit Music mode button in right-click menu enters music mode even from Platform
    ctrl.EnterMusicMode(monitors, 1.0);
    Assert(ctrl.MusicPlaying && ctrl.ActiveSpriteName == "aemeath" && ctrl.ShouldShowBubble,
        "Explicit Music mode button enters music mode from Platform");
    Assert(ctrl.Y >= 100 + CompanionController.BubbleHeightDip,
        "Entering music mode clamps Y downward to reserve space for speech bubble");

    // Single-clicking during active Music mode keeps Music mode; double-clicking switches to Platform
    ctrl.HandleSingleClick(monitors, 1.0);
    Assert(ctrl.MusicPlaying && ctrl.ShouldShowBubble,
        "Single-click during active Music mode does not exit to Platform");
    ctrl.HandleDoubleClick(monitors, 1.0);
    Assert(!ctrl.MusicPlaying && !ctrl.ShouldShowBubble && ctrl.Mode == CompanionMode.B && ctrl.ShouldShowPlatformMiniPlayer,
        "Double-click during Music mode switches to Platform mode with mini player");

    // Switch to Roam mode -> auto music transition works normally
    ctrl.SetMode(CompanionMode.A, monitors, 1.0);
    ctrl.UpdateMusicState(false, monitors, 1.0);
    ctrl.UpdateMusicState(true, monitors, 1.0);
    Assert(ctrl.MusicPlaying && ctrl.ShouldShowBubble,
        "Roam mode automatically enters music mode when music starts");

    // Dismissing in Roam via Roam menu selection stays out while SMTC keeps playing continuously
    ctrl.SetMode(CompanionMode.A, monitors, 1.0);
    Assert(!ctrl.MusicPlaying && !ctrl.ShouldShowBubble && ctrl.Mode == CompanionMode.A,
        "Selecting Roam during music mode exits music mode");
    ctrl.UpdateMusicState(true, monitors, 1.0);
    Assert(!ctrl.MusicPlaying && !ctrl.ShouldShowBubble,
        "Continuous SMTC polling does not re-trigger dismissed music mode");

    // Once music stops and starts playing again in Roam, music mode activates again
    ctrl.UpdateMusicState(false, monitors, 1.0);
    ctrl.UpdateMusicState(true, monitors, 1.0);
    Assert(ctrl.MusicPlaying && ctrl.ShouldShowBubble,
        "Music mode re-activates in Roam after playback stops and restarts");

    // Dragging during music mode immediately exits music mode (Roam -> drag.gif)
    // Dragging during active Music mode (even when entered from Platform) stays in Music mode
    ctrl.SetMode(CompanionMode.B, monitors, 1.0);
    ctrl.EnterMusicMode(monitors, 1.0);
    ctrl.BeginDrag(monitors, 1.0);
    ctrl.UpdateDrag(900, 620, monitors, 1.0);
    Assert(ctrl.MusicPlaying && ctrl.ShouldShowBubble && ctrl.IsDragging && ctrl.ActiveSpriteName == "aemeath" && Math.Abs(ctrl.X - 900) < 0.1,
        "Dragging during active Music mode (entered from Platform) stays in Music mode with bubble");
    ctrl.EndDrag(monitors, 1.0);
    Assert(ctrl.MusicPlaying && ctrl.ShouldShowBubble && !ctrl.IsDragging && ctrl.ActiveSpriteName == "aemeath" && ctrl.Mode == CompanionMode.A,
        "Dropping after drag in active Music mode keeps Music mode (does not switch to Platform)");

    // Pausing music mid-drag in Music mode hides bubble immediately and switches to drag sprite
    ctrl.BeginDrag(monitors, 1.0);
    ctrl.UpdateMusicState(false, monitors, 1.0);
    Assert(!ctrl.MusicPlaying && !ctrl.PlaybackPoseHeld && !ctrl.ShouldShowBubble && ctrl.IsDragging && ctrl.ActiveSpriteName == "drag",
        "Pausing music while dragging in Music mode immediately hides bubble and shows drag sprite");
    ctrl.EndDrag(monitors, 1.0);

    // Dragging during paused Music mode (5-minute hold) immediately dismisses hold to Roam
    ctrl.UpdateMusicState(true, monitors, 1.0);
    ctrl.UpdateMusicState(false, monitors, 1.0);
    Assert(ctrl.PlaybackPoseHeld && ctrl.ShouldShowBubble, "Paused Music mode holds speech bubble before drag");
    ctrl.BeginDrag(monitors, 1.0);
    Assert(!ctrl.MusicPlaying && !ctrl.PlaybackPoseHeld && !ctrl.ShouldShowBubble && ctrl.IsDragging && ctrl.Mode == CompanionMode.A && ctrl.ActiveSpriteName == "drag",
        "Dragging during paused Music mode immediately dismisses pause hold into Roam drag");
    ctrl.EndDrag(monitors, 1.0);
    ctrl.UpdateMusicState(true, monitors, 1.0);

    // Dragging during Platform mode keeps Platform screen sprite (not drag.gif)
    ctrl.SetMode(CompanionMode.B, monitors, 1.0);
    ctrl.BeginDrag(monitors, 1.0);
    Assert(ctrl.IsDragging && ctrl.Mode == CompanionMode.B && ctrl.ActiveSpriteName == ctrl.ScreenName,
        "Dragging in Platform mode keeps Platform screen sprite");
    ctrl.EndDrag(monitors, 1.0);

    // Selecting Platform or Roam from right-click menu during music mode switches immediately
    ctrl.EnterMusicMode(monitors, 1.0);
    Assert(ctrl.MusicPlaying || ctrl.PlaybackPoseHeld, "Music mode active before menu mode switch");

    ctrl.SetMode(CompanionMode.B, monitors, 1.0);
    Assert(!ctrl.MusicPlaying && !ctrl.ShouldShowBubble && ctrl.Mode == CompanionMode.B && ctrl.IsFalling,
        "Selecting Platform during music mode immediately exits music mode and enters Platform");

    ctrl.EnterMusicMode(monitors, 1.0);
    ctrl.SetMode(CompanionMode.A, monitors, 1.0);
    Assert(!ctrl.MusicPlaying && !ctrl.ShouldShowBubble && ctrl.Mode == CompanionMode.A && ctrl.ActiveSpriteName != "aemeath",
        "Selecting Roam during music mode immediately exits music mode and enters Roam");

    // Pause hold 5-minute timeout
    ctrl.UpdateMusicState(false, monitors, 1.0);
    ctrl.UpdateMusicState(true, monitors, 1.0);
    ctrl.UpdateMusicState(false, monitors, 1.0);
    Assert(ctrl.PlaybackPoseHeld && ctrl.ShouldShowBubble && ctrl.ActiveSpriteName == "aemeath",
        "Pausing music holds playback pose and speech bubble");

    // Advance 5 minutes (6000 steps of 0.05s = 300s)
    for (int i = 0; i < 6005; i++)
        ctrl.Tick(0.05, monitors, [floorPlat], null, 1.0);

    Assert(!ctrl.PlaybackPoseHeld && !ctrl.ShouldShowBubble && ctrl.ActiveSpriteName != "aemeath",
        "After 5 minutes paused, releases hold and hides speech bubble");
}

// ============================================================
// 5. GIF Sprite Bank Load Verification & Visual Bubble Preview
// ============================================================
{
    string spriteDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Sprites");
    var bank = new GifSpriteBank();
    bank.LoadFromDirectory(spriteDir);
    Assert(bank.Sprites.Count == 15, "GifSpriteBank loads all 15 GIF sprites (including screen8)");
    Assert(bank.Get("aemeath").Frames.Count == 37, "aemeath.gif decoded all 37 frames");
    Assert(bank.Get("screen1").Frames.Count == 12, "screen1.gif decoded all 12 frames");
    Assert(bank.Get("screen8").Frames.Count == 4 && bank.Get("screen8").PixelWidth == 300 && bank.Get("screen8").PixelHeight == 200,
        "screen8.gif decoded all 4 frames at 300x200 matching screen1-7 height");
    Assert(bank.Get("idle1").Frames.Count == 25, "idle1.gif decoded all 25 frames");
}

// ============================================================
// 6. Update Version Comparison Tests
// ============================================================
{
    Assert(AppUpdateService.IsNewerVersion("0.0.1", "v0.0.2"), "SemVer detects patch update (0.0.1 -> v0.0.2)");
    Assert(AppUpdateService.IsNewerVersion("0.1.9", "v0.10.0"), "SemVer compares minor numerically (0.1.9 -> v0.10.0)");
    Assert(AppUpdateService.IsNewerVersion("1.0.0-beta.1", "1.0.0"), "SemVer treats stable release as newer than pre-release");
    Assert(!AppUpdateService.IsNewerVersion("1.2.0", "v1.2.0"), "SemVer returns false for identical versions");
    Assert(!AppUpdateService.IsNewerVersion("1.2.0", "v1.1.9"), "SemVer returns false for older candidate");
    Assert(AppUpdateService.ReadAssemblyVersion() == "0.0.6", "Assembly version matches project Version (0.0.6)");

    // AutoStartService formatting & parsing
    string formatted = AutoStartService.FormatRunCommand(@"C:\Program Files\Aemeath\AemeathCompanion.exe");
    Assert(formatted == "\"C:\\Program Files\\Aemeath\\AemeathCompanion.exe\"",
        "AutoStartService quotes executable path with spaces");
    Assert(AutoStartService.NormalizeRegisteredPath(formatted) == @"C:\Program Files\Aemeath\AemeathCompanion.exe",
        "AutoStartService extracts clean path from quoted registry value");

    // BoingAnimator single-cycle Squash & Stretch curve verification
    var (sqX, sqY) = BoingAnimator.Evaluate(BoingAnimator.SquishPeakSeconds);
    Assert(sqX > 1.12 && sqY < 0.87 && Math.Abs(sqX * sqY - 1.0) < 0.06,
        "Boing squish phase widens and flattens while roughly preserving volume");

    var (stX, stY) = BoingAnimator.Evaluate(BoingAnimator.StretchPeakSeconds);
    Assert(stX < 0.94 && stY > 1.09 && Math.Abs(stX * stY - 1.0) < 0.06,
        "Boing stretch phase rebounds upward with counter-narrowing");

    // Verify strictly monotonic return (no second squish or extra bounce after StretchPeakSeconds)
    bool noSecondBounce = true;
    double prevY = stY;
    for (double t = BoingAnimator.StretchPeakSeconds + 0.01; t <= BoingAnimator.TotalDurationSeconds; t += 0.01)
    {
        var (_, sy) = BoingAnimator.Evaluate(t);
        if (sy > prevY + 1e-6 || sy < 1.0 - 1e-6) { noSecondBounce = false; break; }
        prevY = sy;
    }
    Assert(noSecondBounce, "Boing settles monotonically in 1 cycle with no second bounce");

    var boing = new BoingAnimator();
    boing.Trigger();
    Assert(boing.IsActive, "BoingAnimator activates on Trigger()");
    for (int i = 0; i < 25; i++) boing.Update(0.016);
    Assert(!boing.IsActive && boing.ScaleX == 1.0 && boing.ScaleY == 1.0,
        "BoingAnimator settles cleanly back to (1.0, 1.0) after 0.34s");
}

// ============================================================
// 7. Preview Render Hook (optional)
// ============================================================
if (args.Length >= 2 && args[0] == "--render-preview")
{
    string spriteDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Sprites");
    var bank = new GifSpriteBank();
    bank.LoadFromDirectory(spriteDir);
        string outPath = args[1];
        Exception? uiErr = null;
        var t = new Thread(() =>
        {
            try
            {
                var bubble = new AemeathCompanion.UI.MusicBubbleControl();
                bubble.ApplySnapshot(new MediaSnapshot
                {
                    HasMusicSession = true,
                    IsMusicPlaying = true,
                    CanPlayPause = true,
                    CanSkipPrevious = true,
                    CanSkipNext = true,
                    Title = "Aemeath — Digital Ghost (Pixel Remix)",
                    Artist = "Kuro Games · Solaris Sound Team",
                    SourceApp = "Spotify",
                    Position = TimeSpan.FromSeconds(94),
                    Duration = TimeSpan.FromSeconds(218),
                    AlbumArt = null,
                    AlbumTint = System.Windows.Media.Color.FromRgb(0xF0, 0xA0, 0xB8)
                });
                float[] bands = [0.45f, 0.72f, 0.88f, 0.96f, 0.78f, 0.62f, 0.84f, 0.91f, 0.70f, 0.55f, 0.68f, 0.80f, 0.64f, 0.50f, 0.58f, 0.74f, 0.62f, 0.48f, 0.42f, 0.52f, 0.38f, 0.30f, 0.24f, 0.18f];
                float[] peaks = [0.60f, 0.85f, 0.98f, 1.00f, 0.90f, 0.75f, 0.92f, 0.96f, 0.82f, 0.68f, 0.78f, 0.88f, 0.76f, 0.62f, 0.70f, 0.84f, 0.72f, 0.58f, 0.52f, 0.60f, 0.48f, 0.40f, 0.32f, 0.24f];
                bubble.ApplyPreviewState(138, bands, peaks);
                bubble.Measure(new System.Windows.Size(276, 324));
                bubble.Arrange(new System.Windows.Rect(0, 0, 276, 324));

                // Verify selective hit-testing on transport buttons vs click-through elsewhere
                if (bubble.HitTestTransportButton(new System.Windows.Point(78, 280)) != AemeathCompanion.UI.MediaTransportButton.Previous ||
                    bubble.HitTestTransportButton(new System.Windows.Point(138, 280)) != AemeathCompanion.UI.MediaTransportButton.PlayPause ||
                    bubble.HitTestTransportButton(new System.Windows.Point(198, 280)) != AemeathCompanion.UI.MediaTransportButton.Next ||
                    bubble.HitTestTransportButton(new System.Windows.Point(138, 100)) != AemeathCompanion.UI.MediaTransportButton.None)
                {
                    throw new InvalidOperationException("Transport button hit-test coordinates mismatch");
                }

                var sprite = bank.Get("aemeath").Frames[0].Bitmap;
                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // Light-to-dark split background to verify outline contrast on both themes
                    dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEA, 0xEC, 0xF2)), null, new System.Windows.Rect(0, 0, 170, 550));
                    dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x18, 0x22)), null, new System.Windows.Rect(170, 0, 170, 550));
                    var vb = new System.Windows.Media.VisualBrush(bubble) { Stretch = System.Windows.Media.Stretch.None };
                    dc.DrawRectangle(vb, null, new System.Windows.Rect(32, 16, 276, 324));
                    dc.DrawImage(sprite, new System.Windows.Rect(70, 332, 200, 200));
                }
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(340, 550, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                using var fs = File.Create(outPath);
                enc.Save(fs);
            }
            catch (Exception ex) { uiErr = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        Assert(uiErr is null, $"Preview render succeeded ({uiErr?.Message ?? "OK"})");
}

if (args.Length >= 2 && args[0] == "--render-mini-preview")
{
    string spriteDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Sprites");
    var bank = new GifSpriteBank();
    bank.LoadFromDirectory(spriteDir);
    string outPath = args[1];
    Exception? uiErr = null;
    var t = new Thread(() =>
    {
        try
        {
            var mini = new AemeathCompanion.UI.PlatformMiniPlayerControl();
            mini.ApplySnapshot(new MediaSnapshot
            {
                HasMusicSession = true,
                IsMusicPlaying = true,
                CanPlayPause = true,
                CanSkipPrevious = true,
                CanSkipNext = true,
                Title = "Aemeath — Digital Ghost (Pixel Remix)",
                Artist = "Kuro Games · Solaris Sound Team",
                SourceApp = "Spotify",
                Position = TimeSpan.FromSeconds(94),
                Duration = TimeSpan.FromSeconds(218),
                AlbumArt = null,
                AlbumTint = System.Windows.Media.Color.FromRgb(0xF0, 0xA0, 0xB8)
            });
            float[] bands = [0.45f, 0.72f, 0.88f, 0.96f, 0.78f, 0.62f, 0.84f, 0.91f, 0.70f, 0.55f, 0.68f, 0.80f, 0.64f, 0.50f, 0.58f, 0.74f, 0.62f, 0.48f, 0.42f, 0.52f, 0.38f, 0.30f, 0.24f, 0.18f];
            float[] peaks = [0.60f, 0.85f, 0.98f, 1.00f, 0.90f, 0.75f, 0.92f, 0.96f, 0.82f, 0.68f, 0.78f, 0.88f, 0.76f, 0.62f, 0.70f, 0.84f, 0.72f, 0.58f, 0.52f, 0.60f, 0.48f, 0.40f, 0.32f, 0.24f];
            mini.ApplyPreviewState(AemeathCompanion.UI.MiniPlayerTailEdge.Bottom, 162, bands, peaks);
            mini.Measure(new System.Windows.Size(324, 66));
            mini.Arrange(new System.Windows.Rect(0, 0, 324, 66));

            if (mini.HitTestTransportButton(new System.Windows.Point(240, 28)) != AemeathCompanion.UI.MediaTransportButton.Previous ||
                mini.HitTestTransportButton(new System.Windows.Point(267, 28)) != AemeathCompanion.UI.MediaTransportButton.PlayPause ||
                mini.HitTestTransportButton(new System.Windows.Point(293, 28)) != AemeathCompanion.UI.MediaTransportButton.Next ||
                mini.HitTestTransportButton(new System.Windows.Point(100, 28)) != AemeathCompanion.UI.MediaTransportButton.None)
            {
                throw new InvalidOperationException("Mini player hit-test coordinates mismatch");
            }

            var screen1 = bank.Get("screen1").Frames[0].Bitmap;
            var screen8 = bank.Get("screen8").Frames[0].Bitmap;
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEA, 0xEC, 0xF2)), null, new System.Windows.Rect(0, 0, 190, 500));
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x18, 0x22)), null, new System.Windows.Rect(190, 0, 190, 500));

                // Top demo: Mini player above screen1 perched on a mock window bar
                var vb = new System.Windows.Media.VisualBrush(mini) { Stretch = System.Windows.Media.Stretch.None };
                dc.DrawRectangle(vb, null, new System.Windows.Rect(28, 16, 324, 66));
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x31, 0x42)), null, new System.Windows.Rect(40, 212, 300, 24));
                dc.DrawImage(screen1, new System.Windows.Rect(90, 16, 200, 200));

                // Bottom demo: Mini player above screen8 seal perched on a mock window bar
                dc.DrawRectangle(vb, null, new System.Windows.Rect(28, 252, 324, 66));
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x31, 0x42)), null, new System.Windows.Rect(40, 470, 300, 24));
                dc.DrawImage(screen8, new System.Windows.Rect(40, 285, 300, 200));
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(380, 500, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            using var fs = File.Create(outPath);
            enc.Save(fs);
        }
        catch (Exception ex) { uiErr = ex; }
    });
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    t.Join();
    Assert(uiErr is null, $"Mini player preview render succeeded ({uiErr?.Message ?? "OK"})");
}

// =========================================================================
// 7. Local-Dev-Only Graceful Shutdown Control (DevLifecycleControl)
// =========================================================================
{
    // Local repo bin directory is detected; deployed/non-bin directories are strictly rejected
    bool foundRepo = DevLifecycleControl.TryFindLocalDevRepoRoot(AppContext.BaseDirectory, out string detectedRoot);
    Assert(foundRepo && File.Exists(Path.Combine(detectedRoot, "src", "AemeathCompanion", "AemeathCompanion.csproj")),
        "DevLifecycleControl recognizes local repo bin/ output directory");
    Assert(!DevLifecycleControl.IsLocalDevDirectory(detectedRoot),
        "DevLifecycleControl rejects repo root directly (not under bin/)");
    Assert(!DevLifecycleControl.IsLocalDevDirectory(@"C:\Program Files\AemeathCompanion"),
        "DevLifecycleControl rejects deployed Program Files directory");
    Assert(!DevLifecycleControl.IsLocalDevDirectory(Path.GetTempPath()),
        "DevLifecycleControl rejects arbitrary temp directory");

    // Target PID matching
    Assert(DevLifecycleControl.MatchesTargetPid("", 4242), "Empty shutdown signal matches any dev PID");
    Assert(DevLifecycleControl.MatchesTargetPid("all", 4242), "'all' shutdown signal matches any dev PID");
    Assert(DevLifecycleControl.MatchesTargetPid("shutdown 2026-11-25T00:00:00Z abc123", 4242), "Token shutdown signal matches any dev PID");
    Assert(DevLifecycleControl.MatchesTargetPid("4242", 4242), "Exact PID shutdown signal matches target PID");
    Assert(!DevLifecycleControl.MatchesTargetPid("9999", 4242), "Different PID shutdown signal does not match");
    Assert(DevLifecycleControl.MatchesTargetPid("pid:4242", 4242), "Prefixed PID shutdown signal matches target PID");
    Assert(!DevLifecycleControl.MatchesTargetPid("pid:9999", 4242), "Different prefixed PID does not match");

    // File-based signal: clears stale pre-existing signal without triggering, then triggers once on fresh signal
    string tempControlDir = Path.Combine(Path.GetTempPath(), $"aemeath-devctl-test-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(tempControlDir);
        string staleSignal = Path.Combine(tempControlDir, DevLifecycleControl.ShutdownSignalFileName);
        File.WriteAllText(staleSignal, "stale-before-start");

        using var fileTriggered = new ManualResetEventSlim(false);
        int triggerCount = 0;

        using (var ctl = new DevLifecycleControl(
            tempControlDir,
            onShutdownRequested: () =>
            {
                Interlocked.Increment(ref triggerCount);
                fileTriggered.Set();
            },
            currentPid: 77771,
            enableNamedEvent: false))
        {
            ctl.Start();
            Assert(File.Exists(ctl.InstanceFilePath), "DevLifecycleControl writes instance.json on Start()");
            Assert(!File.Exists(staleSignal), "DevLifecycleControl clears stale shutdown.signal on Start()");
            Thread.Sleep(300);
            Assert(triggerCount == 0, "Stale shutdown.signal before Start() never triggers shutdown");

            File.WriteAllText(ctl.SignalFilePath, $"shutdown {Guid.NewGuid():N}");
            bool fired = fileTriggered.Wait(2000);
            Assert(fired && triggerCount == 1, "Fresh shutdown.signal triggers graceful shutdown callback exactly once");
            Assert(!File.Exists(ctl.SignalFilePath), "Consumed shutdown.signal is deleted after triggering");
        }

        Assert(!File.Exists(Path.Combine(tempControlDir, DevLifecycleControl.InstanceFileName)),
            "DevLifecycleControl deletes instance.json on Dispose()");

        // Named event signal test (isolated event name)
        string testEventName = $@"Local\AemeathCompanion.Test.Shutdown.{Guid.NewGuid():N}";
        using var eventTriggered = new ManualResetEventSlim(false);
        using (var ctl2 = new DevLifecycleControl(
            tempControlDir,
            onShutdownRequested: () => eventTriggered.Set(),
            currentPid: 77772,
            enableNamedEvent: true,
            eventName: testEventName))
        {
            ctl2.Start();
            using var sender = EventWaitHandle.OpenExisting(testEventName);
            sender.Set();
            Assert(eventTriggered.Wait(2000), "Named EventWaitHandle triggers graceful shutdown callback");
        }
    }
    finally
    {
        try { if (Directory.Exists(tempControlDir)) Directory.Delete(tempControlDir, recursive: true); } catch { }
    }
}

Console.WriteLine($"\nSummary: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
