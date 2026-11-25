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

    // Requirement: Platform mode must NOT auto-switch to music mode when music starts
    ctrl.UpdateMusicState(true, monitors, 1.0);
    Assert(ctrl.Mode == CompanionMode.B && !ctrl.MusicPlaying && !ctrl.ShouldShowBubble,
        "Platform mode blocks automatic music mode transition");

    // Switching Platform -> Roam while music is already playing prioritizes Roam over Music mode
    ctrl.SetMode(CompanionMode.A, monitors, 1.0);
    ctrl.UpdateMusicState(true, monitors, 1.0);
    Assert(ctrl.Mode == CompanionMode.A && !ctrl.MusicPlaying && !ctrl.ShouldShowBubble,
        "Switching Platform -> Roam while music plays stays in Roam (does not hijack into Music mode)");

    // Active drag in Roam must not be interrupted when music starts playing
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

    // Clicking during music mode immediately stops music mode & restores prior Platform mode
    ctrl.HandleSingleClick(monitors, 1.0);
    Assert(!ctrl.MusicPlaying && !ctrl.ShouldShowBubble && ctrl.Mode == CompanionMode.B && ctrl.ActiveSpriteName != "aemeath",
        "Clicking during music mode exits music mode and restores prior Platform mode");

    // Switch to Roam mode -> auto music transition works normally
    ctrl.SetMode(CompanionMode.A, monitors, 1.0);
    ctrl.UpdateMusicState(false, monitors, 1.0);
    ctrl.UpdateMusicState(true, monitors, 1.0);
    Assert(ctrl.MusicPlaying && ctrl.ShouldShowBubble,
        "Roam mode automatically enters music mode when music starts");

    // Dismissing in Roam keeps staying out while SMTC keeps playing continuously
    ctrl.HandleSingleClick(monitors, 1.0);
    Assert(!ctrl.MusicPlaying && !ctrl.ShouldShowBubble && ctrl.Mode == CompanionMode.A,
        "Clicking during music mode in Roam exits music mode");
    ctrl.UpdateMusicState(true, monitors, 1.0);
    Assert(!ctrl.MusicPlaying && !ctrl.ShouldShowBubble,
        "Continuous SMTC polling does not re-trigger dismissed music mode");

    // Once music stops and starts playing again in Roam, music mode activates again
    ctrl.UpdateMusicState(false, monitors, 1.0);
    ctrl.UpdateMusicState(true, monitors, 1.0);
    Assert(ctrl.MusicPlaying && ctrl.ShouldShowBubble,
        "Music mode re-activates in Roam after playback stops and restarts");

    // Dragging during music mode immediately exits music mode (Roam -> drag.gif)
    ctrl.BeginDrag(monitors, 1.0);
    Assert(!ctrl.MusicPlaying && !ctrl.ShouldShowBubble && ctrl.IsDragging && ctrl.ActiveSpriteName == "drag",
        "Dragging during music mode (from Roam) exits music mode and uses drag sprite");
    ctrl.EndDrag(monitors, 1.0);

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
    Assert(AppUpdateService.ReadAssemblyVersion() == "0.0.3", "Assembly version matches project Version (0.0.3)");

    // AutoStartService formatting & parsing
    string formatted = AutoStartService.FormatRunCommand(@"C:\Program Files\Aemeath\AemeathCompanion.exe");
    Assert(formatted == "\"C:\\Program Files\\Aemeath\\AemeathCompanion.exe\"",
        "AutoStartService quotes executable path with spaces");
    Assert(AutoStartService.NormalizeRegisteredPath(formatted) == @"C:\Program Files\Aemeath\AemeathCompanion.exe",
        "AutoStartService extracts clean path from quoted registry value");
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

Console.WriteLine($"\nSummary: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
