namespace WindowsOptimizer.Core;

/// <summary>Every tweak the app offers, in display order. Add a new one here.</summary>
public static class TweakCatalog
{
    private const string WindowsUpdatePolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string International = @"Control Panel\International";
    private const string MultimediaProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string Tcpip6Parameters = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters";
    private const string DataCollection = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";
    private const string SystemPolicy = @"SOFTWARE\Policies\Microsoft\Windows\System";
    private const string CloudContentUser = @"Software\Policies\Microsoft\Windows\CloudContent";
    private const string CloudContentMachine = @"SOFTWARE\Policies\Microsoft\Windows\CloudContent";
    private const string WindowsSearchPolicy = @"SOFTWARE\Policies\Microsoft\Windows\Windows Search";
    private const string EdgePolicy = @"SOFTWARE\Policies\Microsoft\Edge";
    private const string AppPrivacy = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";
    private const string AppCompat = @"SOFTWARE\Policies\Microsoft\Windows\AppCompat";
    private const string ErrorReportingPolicy = @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting";
    private const string NvidiaPanel = @"Software\NVIDIA Corporation\NvControlPanel2\Client";

    // Low risk, a clear benefit for most people, and nothing lost by turning them on.
    private static readonly HashSet<string> RecommendedIds = new()
    {
        "game-mode-on", "game-dvr-off", "mouse-accel-off", "accessibility-shortcuts-off", "power-plan-max",
        "file-extensions-show", "suggestions-off", "edge-preload-off", "background-apps-off", "recall-snapshots-off",
        "click-to-do-off", "updates-no-reboot-signed-in", "advertising-id-off", "telemetry-minimum", "activity-history-off", "taskbar-end-task",
    };

    public static IReadOnlyList<ITweak> Create(TweakContext c)
    {
        var tweaks = Builtin(c)
            .Concat(ShellAndAiTweaks.Create(c))
            .Concat(ExtraTweaks.Create(c))
            .Concat(StartupDiscovery.Discover(c))
            .Concat(new GameLibrary(c.DataRoot).Games().SelectMany(game => GameProfileTweaks.For(c, game)))
            .ToList();

        foreach (var tweak in tweaks.OfType<TweakBase>().Where(t => RecommendedIds.Contains(t.Id))) tweak.Recommended = true;
        return tweaks;
    }

    private static ITweak[] Builtin(TweakContext c) => new ITweak[]
    {
        // Updates
        new UpdatesPauseTweak(c)
        {
            NeedsRestart = true,
            Warning = "Without automatic updates the PC stops getting security fixes until you update it by hand. Use Undo to go back to normal.",
        },
        new RegistryTweak(c, "updates-exclude-drivers", "Don't install drivers through Windows Update",
            "Stops Windows Update from swapping graphics and peripheral drivers behind your back. Update drivers from the manufacturer's site instead.",
            TweakCategory.Updates, RiskLevel.Medium,
            Reg.LmDword(WindowsUpdatePolicy, "ExcludeWUDriversInQualityUpdate", 1),
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\DriverSearching", "SearchOrderConfig", 0),
            Reg.LmDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0)),

        new RegistryTweak(c, "delivery-optimization-off", "Don't share updates with other PCs",
            "Turns off peer-to-peer Delivery Optimization: the PC stops sending pieces of Windows updates to other computers and downloads only from Microsoft.",
            TweakCategory.Updates, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)),

        // Power
        new PowerPlanTweak(c),
        new RegistryTweak(c, "power-no-idle-lock", "Turn off screen saver and idle lock",
            "Keeps the PC from locking or starting the screen saver on its own during a race or a stream.",
            TweakCategory.Power, RiskLevel.Low,
            Reg.CuString(@"Control Panel\Desktop", "ScreenSaveActive", "0"),
            Reg.CuString(@"Control Panel\Desktop", "ScreenSaverIsSecure", "0"),
            Reg.LmDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "InactivityTimeoutSecs", 0)),
        new RegistryTweak(c, "power-throttling-off", "Turn off power throttling",
            "Stops Windows from slowing processes down to save power. Useful on a desktop PC.",
            TweakCategory.Power, RiskLevel.Low,
            Reg.LmDword(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1))
        {
            NeedsRestart = true,
        },
        MaintenanceTweaks.HibernationOff(c),
        new PcieAspmTweak(c),
        new PowerSettingTweak(c, "usb-suspend-off", "Turn off USB selective suspend",
            "Stops Windows from putting USB devices to sleep to save power. It avoids the brief lag when a mouse, keyboard or wheel wakes up, and keeps USB audio from cutting out.",
            RiskLevel.Low, "2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4738-a8f2-de8e4a0c4c53", 0, 0,
            "USB selective suspend"),
        new PowerSettingTweak(c, "cpu-min-100", "Keep the CPU at full speed while plugged in",
            "Sets the minimum processor state to 100% on AC power, so the CPU never drops its clock between bursts of work. It removes the ramp-up delay at the cost of more idle power and heat. Meant for desktop PCs.",
            RiskLevel.Low, "SUB_PROCESSOR", "PROCTHROTTLEMIN", 100, null,
            "Minimum processor state"),

        // Gaming
        new RegistryTweak(c, "game-mode-on", "Turn on Game Mode",
            "Asks Windows to prioritize the game in the foreground and postpone background tasks.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.CuDword(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
            Reg.CuDword(@"Software\Microsoft\GameBar", "AllowAutoGameMode", 1)),
        new RegistryTweak(c, "game-dvr-off", "Turn off background recording",
            "Disables Game DVR, which records the last minutes of gameplay and costs disk and CPU.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.CuDword(@"System\GameConfigStore", "GameDVR_Enabled", 0),
            Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0)),
        new RegistryTweak(c, "game-bar-off","Turn off Xbox Game Bar",
            "Disables the Game Bar overlay and its controller shortcut, and blocks Game DVR by policy.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.CuDword(@"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0),
            Reg.CuDword(@"Software\Microsoft\GameBar", "ShowStartupPanel", 0),
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0)),
        new RegistryTweak(c, "hags-on", "Turn on hardware-accelerated GPU scheduling",
            "Lets the GPU manage its own video memory scheduling. It helps some games and hurts others, depending on the graphics driver. Needs a recent GPU and driver.",
            TweakCategory.Gaming, RiskLevel.Medium,
            Reg.LmDword(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "mouse-accel-off", "Turn off mouse acceleration",
            "Turns off \"Enhance pointer precision\", so the cursor moves the same distance for the same hand movement. Aim stays consistent in games.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.CuString(@"Control Panel\Mouse", "MouseSpeed", "0"),
            Reg.CuString(@"Control Panel\Mouse", "MouseThreshold1", "0"),
            Reg.CuString(@"Control Panel\Mouse", "MouseThreshold2", "0")),
        new RegistryTweak(c, "mouse-hover-fast", "Shorten the mouse hover delay",
            "Lowers the time the pointer must rest before a hover tip or menu opens, from 400 ms to 10 ms.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.CuString(@"Control Panel\Mouse", "MouseHoverTime", "10")),
        new RegistryTweak(c, "keyboard-repeat-fast", "Faster key repeat",
            "Shortens the wait before a held key repeats and makes it repeat at the fastest rate.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.CuString(@"Control Panel\Keyboard", "KeyboardDelay", "0"),
            Reg.CuString(@"Control Panel\Keyboard", "KeyboardSpeed", "31")),
        new RegistryTweak(c, "accessibility-shortcuts-off", "Turn off accessibility keyboard shortcuts",
            "Stops Sticky Keys (Shift 5 times), Filter Keys (right Shift for 8 seconds) and Toggle Keys (Num Lock for 5 seconds) from opening by accident during a game. The features themselves still work from Settings.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.CuString(@"Control Panel\Accessibility\StickyKeys", "Flags", "506"),
            Reg.CuString(@"Control Panel\Accessibility\Keyboard Response", "Flags", "122"),
            Reg.CuString(@"Control Panel\Accessibility\ToggleKeys", "Flags", "58")),
        new RegistryTweak(c, "games-scheduler-priority", "Give games high priority in the multimedia scheduler",
            "Sets the high priority the Windows multimedia scheduler (MMCSS) gives to apps that register as games. Only affects games that use this registration, which most do.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.LmDword(MultimediaProfile + @"\Tasks\Games", "Priority", 6),
            Reg.LmString(MultimediaProfile + @"\Tasks\Games", "Scheduling Category", "High"),
            Reg.LmDword(MultimediaProfile + @"\Tasks\Games", "GPU Priority", 8)),
        new RegistryTweak(c, "dynamic-lighting-off", "Turn off Windows Dynamic Lighting",
            "Stops Windows from taking control of RGB lights, so the software for your peripherals keeps control of them.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\Lighting", "AmbientLightingEnabled", 0)),
        new RegistryTweak(c, "no-volume-ducking", "Don't lower volume during calls",
            "Stops Windows from turning down other sounds when it detects a voice call or chat.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.CuDword(@"Software\Microsoft\Multimedia\Audio", "UserDuckingPreference", 3)),
        new RegistryTweak(c, "system-responsiveness-games", "Reserve less CPU for background tasks while gaming",
            "Lowers the share of CPU the multimedia scheduler keeps for background tasks while a game uses it, from 20% to 10%. Windows treats values under 10 as 20, so 10 is the lowest that works.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.LmDword(MultimediaProfile, "SystemResponsiveness", 10)),
        new RegistryTweak(c, "network-throttling-off", "Turn off network throttling during multimedia",
            "Removes the packets-per-second limit Windows puts on normal traffic while a game or player uses the multimedia scheduler. It doesn't lower ping; it only avoids that cap.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.LmDword(MultimediaProfile, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF)))
        {
            NeedsRestart = true,
        },
        new NagleTweak(c),
        MaintenanceTweaks.RacingGtEvoAutostart(c),

        // Appearance
        new RegistryTweak(c, "dark-theme", "Dark theme",
            "Uses the dark theme in apps and in the system.",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.CuDword(Personalize, "AppsUseLightTheme", 0),
            Reg.CuDword(Personalize, "SystemUsesLightTheme", 0)),
        new RegistryTweak(c, "visual-effects-reduced", "Reduce visual effects and transparency",
            "Picks the best-performance setting for visual effects and turns off taskbar animations and transparency.",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2),
            Reg.CuDword(ExplorerAdvanced, "TaskbarAnimations", 0),
            Reg.CuDword(Personalize, "EnableTransparency", 0))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "taskbar-chat-off", "Hide Chat from the taskbar",
            "Removes the Chat (Teams) icon from the taskbar.",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.CuDword(ExplorerAdvanced, "TaskbarMn", 0)),
        new RegistryTweak(c, "datetime-iso-24h", "YYYY-MM-DD dates and 24-hour clock",
            "Shows dates like 2017-04-05 and times like 14:30:00.",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.CuString(International, "sShortDate", "yyyy-MM-dd"),
            Reg.CuString(International, "sShortTime", "HH:mm"),
            Reg.CuString(International, "sTimeFormat", "HH:mm:ss"))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "start-pins-empty", "Empty the Start menu pins",
            "Applies a policy that leaves the pinned list in the Start menu empty.",
            TweakCategory.Appearance, RiskLevel.Medium,
            Reg.LmString(@"SOFTWARE\Policies\Microsoft\Windows\Explorer", "ConfigureStartPins", """{"pinnedList":[]}"""))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "lock-screen-off", "Skip the lock screen",
            "Goes straight to the sign-in screen, without the lock screen picture.",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", 1)),
        new RegistryTweak(c, "copilot-off", "Turn off Windows Copilot",
            "Hides the Copilot button and turns Copilot off by policy, so it doesn't start in the background.",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.CuDword(ExplorerAdvanced, "ShowCopilotButton", 0),
            Reg.CuDword(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1),
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "menu-delay-off", "Open menus without delay",
            "Removes the wait before menus appear: context menus and submenus open right away.",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.CuString(@"Control Panel\Desktop", "MenuShowDelay", "0")),

        // Language and region
        new LanguageTweak(c)
        {
            NeedsRestart = true,
            Warning = "Changes the Windows language, region and keyboard. If the en-US language pack isn't installed, Windows downloads it, which can take a few minutes.",
        },

        new RegistryTweak(c, "language-hotkey-off", "Turn off the Alt+Shift language shortcut",
            "Stops Alt+Shift from switching the keyboard layout, so you don't change language by accident during a game. Sign out and back in to apply.",
            TweakCategory.Language, RiskLevel.Low,
            Reg.CuString(@"Keyboard Layout\Toggle", "Layout Hotkey", "3"),
            Reg.CuString(@"Keyboard Layout\Toggle", "Language Hotkey", "3"))
        {
            NeedsRestart = true,
        },

        // Network
        new DnsTweak(c),
        new RegistryTweak(c, "ipv4-preferred", "Prefer IPv4 over IPv6",
            "Sets bit 0x20 of DisabledComponents so Windows picks IPv4 when both are available. IPv6 stays on.",
            TweakCategory.Network, RiskLevel.Low,
            Reg.LmDword(Tcpip6Parameters, "DisabledComponents", 0x20))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "ipv6-off", "Turn off IPv6",
            "Sets DisabledComponents to 0xFF, which turns IPv6 off on every tunnel interface and in the network stack.",
            TweakCategory.Network, RiskLevel.Medium,
            Reg.LmDword(Tcpip6Parameters, "DisabledComponents", 0xFF))
        {
            NeedsRestart = true,
            Warning = "Some Windows features and networks expect IPv6. Microsoft recommends preferring IPv4 instead of turning IPv6 off. This replaces \"Prefer IPv4 over IPv6\", which writes the same value.",
        },
        NetworkTweaks.TeredoOff(c),

        // Cleanup and privacy
        new RegistryTweak(c, "suggestions-off", "Turn off suggestions and promoted apps",
            "Disables tips, Start menu ads, promoted apps and search box suggestions.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(ContentDelivery, "SystemPaneSuggestionsEnabled", 0),
            Reg.CuDword(ContentDelivery, "SubscribedContent-338389Enabled", 0),
            Reg.CuDword(ContentDelivery, "SubscribedContent-353694Enabled", 0),
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1),
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableSearchBoxSuggestions", 1)),
        new RegistryTweak(c, "recent-tracking-off", "Stop tracking recent files",
            "Stops keeping the list of recent and frequent files in File Explorer and the Start menu.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(ExplorerAdvanced, "Start_TrackDocs", 0),
            Reg.CuDword(ExplorerAdvanced, "ShowRecent", 0),
            Reg.CuDword(ExplorerAdvanced, "ShowFrequent", 0)),
        new RegistryTweak(c, "telemetry-minimum", "Limit diagnostic data to the minimum",
            "Sets diagnostic data to Required only, by policy, so optional data isn't sent. The lowest level (0) only works on Enterprise and Education, so this uses 1.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(DataCollection, "AllowTelemetry", 1)),
        new RegistryTweak(c, "feedback-off", "Don't ask for Windows feedback",
            "Stops Windows from showing notifications that ask for your opinion.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(DataCollection, "DoNotShowFeedbackNotifications", 1)),
        new RegistryTweak(c, "advertising-id-off", "Turn off the advertising ID",
            "Turns off the advertising ID by policy for every user, so apps can't use a unique ID for personalized ads.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1)),
        new RegistryTweak(c, "tailored-experiences-off", "Turn off tailored experiences",
            "Stops Windows from using your diagnostic data to suggest tips, offers and personalized content.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(CloudContentUser, "DisableTailoredExperiencesWithDiagnosticData", 1)),
        new RegistryTweak(c, "activity-history-off", "Don't keep or send activity history",
            "Turns off, by policy, publishing and uploading your activity history (Timeline) to Microsoft.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(SystemPolicy, "PublishUserActivities", 0),
            Reg.LmDword(SystemPolicy, "UploadUserActivities", 0)),
        new RegistryTweak(c, "silent-app-installs-off", "Turn off tips and apps installed without asking",
            "Turns off suggestions in Start and Settings, app tips and the automatic install of suggested apps.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(ContentDelivery, "SilentInstalledAppsEnabled", 0),
            Reg.CuDword(ContentDelivery, "SoftLandingEnabled", 0),
            Reg.CuDword(ContentDelivery, "SubscribedContent-338388Enabled", 0)),
        new RegistryTweak(c, "web-search-off", "Remove web results from Start search",
            "Turns off Bing suggestions and web results when you search from Start or the search box, keeping only local results.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "onedrive-ads-off", "No OneDrive ads in File Explorer",
            "Turns off the sync provider notifications File Explorer uses to suggest OneDrive and other services.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(ExplorerAdvanced, "ShowSyncProviderNotifications", 0)),
        new RegistryTweak(c, "search-highlights-off", "Search without highlights or cloud results",
            "Turns off search highlights and cloud results in the Windows search box.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(WindowsSearchPolicy, "EnableDynamicContentInWSB", 0),
            Reg.LmDword(WindowsSearchPolicy, "AllowCloudSearch", 0),
            Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsMSACloudSearchEnabled", 0),
            Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsAADCloudSearchEnabled", 0)),
        new RegistryTweak(c, "ceip-off", "Turn off the Customer Experience Improvement Program",
            "Turns off CEIP, which collects usage statistics for Microsoft.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0)),
        new RegistryTweak(c, "experiments-off", "No Microsoft experiments",
            "Stops Microsoft from running experiments and A/B tests in Windows on this PC, including features under evaluation.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Microsoft\PolicyManager\current\device\System", "AllowExperimentation", 0)),
        new RegistryTweak(c, "mrt-reports-off", "Malicious Software Removal Tool without reports",
            "Stops the Malicious Software Removal Tool (MRT) from sending infection data to Microsoft.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\MRT", "DontReportInfectionInformation", 1)),
        new RegistryTweak(c, "start-account-notices-off", "No Microsoft account notices in Start",
            "Turns off the Microsoft account notices and reminders in the Start menu and Settings.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(ExplorerAdvanced, "Start_AccountNotifications", 0)),
        new RegistryTweak(c, "autoplay-off", "Turn off AutoPlay",
            "Turns off AutoPlay on every type of drive, so media and devices don't run anything when you plug them in.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun", 255),
            Reg.LmDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoAutoplayfornonVolume", 1)),
        new RegistryTweak(c, "remote-assistance-off", "Turn off Remote Assistance",
            "Stops this PC from accepting Remote Assistance invitations, which let another person see and control the screen.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0)),
        new RegistryTweak(c, "edge-ads-telemetry-off", "Microsoft Edge without ads or telemetry",
            "Turns off, by policy and for every user, Edge recommendations, personalization reports, diagnostic data, the shopping assistant, Microsoft Rewards and the new tab page content.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(EdgePolicy, "ShowRecommendationsEnabled", 0),
            Reg.LmDword(EdgePolicy, "PersonalizationReportingEnabled", 0),
            Reg.LmDword(EdgePolicy, "DiagnosticData", 0),
            Reg.LmDword(EdgePolicy, "EdgeShoppingAssistantEnabled", 0),
            Reg.LmDword(EdgePolicy, "ShowMicrosoftRewards", 0),
            Reg.LmDword(EdgePolicy, "SpotlightExperiencesAndRecommendationsEnabled", 0),
            Reg.LmDword(EdgePolicy, "NewTabPageContentEnabled", 0)),
        new RegistryTweak(c, "edge-preload-off", "Don't preload Edge",
            "Stops Edge, by policy, from starting with Windows (startup boost) and from running after its last window closes, which frees memory.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(EdgePolicy, "StartupBoostEnabled", 0),
            Reg.LmDword(EdgePolicy, "BackgroundModeEnabled", 0)),
        new RegistryTweak(c, "explorer-cloud-recommendations-off", "File Explorer without account recommendations",
            "Stops File Explorer, by policy, from fetching recent, favorite and recommended files tied to your Microsoft account from the cloud.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableGraphRecentItems", 1)),
        new RegistryTweak(c, "push-install-off", "Turn off push installs",
            "Stops the Microsoft Store, by policy, from installing apps on this PC from another device or from the Store website.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\PushToInstall", "DisablePushToInstall", 1)),
        new RegistryTweak(c, "notepad-store-banner-off", "Notepad without the Store banner",
            "Removes the banner in Notepad that offers the Microsoft Store version.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(@"Software\Microsoft\Notepad", "ShowStoreBanner", 0)),
        new ServiceStartTweak(c, "diagtrack-off", "Turn off Windows telemetry",
            "Disables the DiagTrack service, which collects and sends diagnostic data in the background.",
            TweakCategory.Privacy, RiskLevel.Low, 4, "DiagTrack")
        {
            NeedsRestart = true,
        },
        new ScheduledTaskTweak(c, "telemetry-tasks-off", "Turn off scheduled telemetry tasks",
            "Disables the scheduled tasks that collect usage data and feedback (Customer Experience Improvement Program, compatibility telemetry, error reporting).",
            TweakCategory.Privacy, RiskLevel.Low,
            @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
            @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
            @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
            @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
            @"\Microsoft\Windows\Windows Error Reporting\QueueReporting",
            @"\Microsoft\Windows\Feedback\Siuf\DmClient",
            @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload"),
        new RegistryTweak(c, "error-reporting-off", "Turn off Windows Error Reporting",
            "Stops Windows from collecting and sending app crash reports to Microsoft. When an app crashes, no report leaves the PC.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(ErrorReportingPolicy, "Disabled", 1)),
        new RegistryTweak(c, "error-reporting-log-off", "Turn off Error Reporting logging",
            "Windows Error Reporting events stop being written to Event Viewer.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(ErrorReportingPolicy, "LoggingDisabled", 1)),
        new RegistryTweak(c, "lock-screen-tips-off", "No tips or ads on the lock screen",
            "Turns off the fun facts, tips, tricks and suggestions Windows shows on the lock screen.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(ContentDelivery, "RotatingLockScreenOverlayEnabled", 0),
            Reg.CuDword(ContentDelivery, "SubscribedContent-338387Enabled", 0)),
        new RegistryTweak(c, "spotlight-off", "Turn off Windows Spotlight",
            "Turns off Windows Spotlight on the lock screen: no rotating pictures, tips or Microsoft promotional content.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(CloudContentUser, "DisableWindowsSpotlightFeatures", 1)),
        new RegistryTweak(c, "store-apps-background-off", "Stop Store apps running in the background",
            "Blocks Microsoft Store apps, by policy, from running in the background, which saves CPU, memory and battery.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(AppPrivacy, "LetAppsRunInBackground", 2)),
        new RegistryTweak(c, "background-apps-off", "Turn off background apps (this user)",
            "Stops the apps you installed from the Microsoft Store from running in the background for your account. Unlike the policy tweak, it needs no administrator rights and can be changed from Settings.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1),
            Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 0)),
        new RegistryTweak(c, "motion-background-off", "No background access to motion",
            "Stops apps, by policy, from reading motion and position sensors while they are closed.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(AppPrivacy, "LetAppsAccessMotion", 2)),
        new RegistryTweak(c, "steps-recorder-off", "Turn off Steps Recorder",
            "Turns off Steps Recorder by policy, which records user actions, screens and interface data for diagnostics.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(AppCompat, "DisableUAR", 1)),
        new RegistryTweak(c, "compatibility-telemetry-off", "Turn off compatibility data collection",
            "Turns off Application Impact Telemetry and the AppCompat program inventory, which send compatibility data to Microsoft.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(AppCompat, "AITEnable", 0),
            Reg.LmDword(AppCompat, "DisableInventory", 1)),
        new RegistryTweak(c, "cloud-clipboard-off", "Don't sync the clipboard to the cloud",
            "Stops copied content, by policy, from being sent to other devices signed in to the same Microsoft account.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(SystemPolicy, "AllowCrossDeviceClipboard", 0)),
        new RegistryTweak(c, "finish-setup-nag-off", "No \"Finish setting up your device\" screen",
            "Turns off the full-screen reminder Windows shows to finish setting up your account and services.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0)),
        new RegistryTweak(c, "location-off", "Turn off the location service",
            "Turns off the Windows location service for every user and app.",
            TweakCategory.Privacy, RiskLevel.Medium,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1))
        {
            Warning = "Maps, weather, Find My Device and the automatic time zone stop knowing where the PC is.",
        },
        new RegistryTweak(c, "nvidia-telemetry-off", "Turn off NVIDIA Control Panel telemetry",
            "Turns off the NVIDIA Control Panel setting that sends usage data to NVIDIA. Only shows up if the NVIDIA Control Panel is installed.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(NvidiaPanel, "OptInOrOutPreference", 0))
        {
            Available = () => c.Registry.KeyExists(RegHive.CurrentUser, NvidiaPanel),
            UnavailableReason = "The NVIDIA Control Panel isn't installed on this PC.",
        },
        new RegistryTweak(c, "handwriting-data-off", "No handwriting and typing data collection",
            "Stops handwriting error reports, handwriting data sharing and input personalization.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\HandwritingErrorReports", "PreventHandwritingErrorReports", 1),
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\TabletPC", "PreventHandwritingDataSharing", 1),
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\InputPersonalization", "AllowInputPersonalization", 0)),
        new RegistryTweak(c, "accept-language-off", "Hide the language list from websites",
            "Stops websites from seeing your Windows language list through the Accept-Language header, which makes the browser harder to fingerprint.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(@"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1)),
        new RegistryTweak(c, "settings-sync-off", "No Microsoft account sync",
            "Turns off syncing of settings, passwords and preferences between PCs through the Microsoft account.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\SettingSync", "DisableSettingSync", 2),
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\SettingSync", "DisableSettingSyncUserOverride", 1)),
        new RegistryTweak(c, "wpbt-off", "Turn off WPBT (BIOS software)",
            "Stops the Windows Platform Binary Table from installing manufacturer programs from the BIOS/UEFI at every startup, such as motherboard utilities that keep coming back.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SYSTEM\CurrentControlSet\Control\Session Manager", "DisableWpbtExecution", 1))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "notifications-off", "Turn off Windows notifications",
            "Turns off all Windows toast notifications: reminders, alarms and app messages stop showing.",
            TweakCategory.Privacy, RiskLevel.Medium,
            Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 0))
        {
            Warning = "Alarms, calendar reminders and chat messages no longer pop up.",
        },
        new RegistryTweak(c, "brave-extras-off", "Brave without Rewards, Wallet, VPN and AI",
            "Turns off, by policy and for every user, Brave Rewards, the wallet, the VPN and the Brave AI chat. Only shows up if Brave is installed.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\BraveSoftware\Brave", "BraveRewardsDisabled", 1),
            Reg.LmDword(@"SOFTWARE\Policies\BraveSoftware\Brave", "BraveWalletDisabled", 1),
            Reg.LmDword(@"SOFTWARE\Policies\BraveSoftware\Brave", "BraveVPNDisabled", 1),
            Reg.LmDword(@"SOFTWARE\Policies\BraveSoftware\Brave", "BraveAIChatEnabled", 0))
        {
            Available = () => c.Registry.KeyExists(RegHive.CurrentUser, @"Software\BraveSoftware")
                || c.Registry.KeyExists(RegHive.LocalMachine, @"SOFTWARE\BraveSoftware"),
            UnavailableReason = "Brave isn't installed on this PC.",
        },
        new RegistryTweak(c, "search-web-location-policy-off", "Search without web or location (policy)",
            "Enforces for every account on the PC that Windows search doesn't query the web, doesn't use your location and doesn't show connected results.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(WindowsSearchPolicy, "ConnectedSearchUseWeb", 0),
            Reg.LmDword(WindowsSearchPolicy, "DisableWebSearch", 1),
            Reg.LmDword(WindowsSearchPolicy, "AllowSearchToUseLocation", 0)),
        new RegistryTweak(c, "cortana-off", "Turn off Cortana by policy",
            "Turns off Cortana by policy for every user, on top of the tweak that takes search off the cloud.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(WindowsSearchPolicy, "AllowCortana", 0)),
        new RegistryTweak(c, "cloud-account-tips-off", "No account notices or cloud tips (policy)",
            "For every account: Windows stops showing notices about the Microsoft account state and tips downloaded from the internet.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(CloudContentMachine, "DisableConsumerAccountStateContent", 1),
            Reg.LmDword(CloudContentMachine, "DisableCloudOptimizedContent", 1)),
        new RegistryTweak(c, "content-delivery-off", "Turn off content delivery completely",
            "Goes further than the ad tweaks: turns off the service that downloads suggestions, sponsored apps and featured content for Start, the lock screen and Settings.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(ContentDelivery, "ContentDeliveryAllowed", 0),
            Reg.CuDword(ContentDelivery, "FeatureManagementEnabled", 0),
            Reg.CuDword(ContentDelivery, "SubscribedContentEnabled", 0)),
        new RegistryTweak(c, "suggestions-extra-off", "Turn off extra Windows suggestions",
            "Goes further than the suggestion and tip tweaks: turns off three more kinds of suggested content, including the welcome experience after updates.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(ContentDelivery, "SubscribedContent-310093Enabled", 0),
            Reg.CuDword(ContentDelivery, "SubscribedContent-338393Enabled", 0),
            Reg.CuDword(ContentDelivery, "SubscribedContent-353696Enabled", 0)),
        new RegistryTweak(c, "oem-apps-off", "No preinstalled manufacturer apps",
            "Turns off the values that let Windows install manufacturer and preinstalled apps on its own in your account. Doesn't remove what is already installed.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(ContentDelivery, "OemPreInstalledAppsEnabled", 0),
            Reg.CuDword(ContentDelivery, "PreInstalledAppsEnabled", 0),
            Reg.CuDword(ContentDelivery, "PreInstalledAppsEverEnabled", 0)),
        new RegistryTweak(c, "speech-model-updates-off", "Don't download speech models",
            "Stops Windows from downloading speech recognition and text-to-speech model updates in the background.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Speech", "AllowSpeechModelUpdate", 0)),
        new ServiceStartTweak(c, "tracking-services-off", "Turn off tracking and diagnostic services",
            "Disables Distributed Link Tracking (TrkWks), the Diagnostics Hub collector and the Program Compatibility Assistant (PcaSvc).",
            TweakCategory.Privacy, RiskLevel.Low, 4, "TrkWks", "diagnosticshub.standardcollector.service", "PcaSvc")
        {
            NeedsRestart = true,
        },
        new ScheduledTaskTweak(c, "data-collection-tasks-off", "Turn off data collection tasks",
            "Disables scheduled tasks that collect disk, resource usage, compatibility and power efficiency data in the background.",
            TweakCategory.Privacy, RiskLevel.Low,
            @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
            @"\Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem",
            @"\Microsoft\Windows\Autochk\Proxy",
            @"\Microsoft\Windows\Application Experience\PcaPatchDbTask",
            @"\Microsoft\Windows\Application Experience\MareBackup",
            @"\Microsoft\Windows\Application Experience\StartupAppTask"),
        new RegistryTweak(c, "find-my-device-off", "Turn off Find My Device",
            "Turns off, by policy, the feature that registers the PC and its location in your Microsoft account so it can be found from the account website.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\FindMyDevice", "AllowFindMyDevice", 0)),
        MaintenanceTweaks.ClearRecentHistory(c),
        CleanupTweaks.TempFiles(c),
        CleanupTweaks.UpdateCache(c),
        MaintenanceTweaks.OneDriveRemove(c),

        // System
        MaintenanceTweaks.RestorePoint(c),
        MaintenanceTweaks.TrimSystemDrive(c),
        MaintenanceTweaks.LargeFilesReport(c),
        new RegistryTweak(c, "ntfs-last-access-off", "Don't record last access time (NTFS)",
            "Stops NTFS from updating the last access date of every file it reads, which saves disk writes.",
            TweakCategory.System, RiskLevel.Low,
            // 0x80000001 is what "fsutil behavior set disablelastaccess 1" writes: user-managed, updates off.
            Reg.LmDword(@"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", unchecked((int)0x80000001)))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "shutdown-fast", "Shut down faster",
            "Cuts to 2 seconds the time Windows waits for apps and services to answer before closing them at shutdown.",
            TweakCategory.System, RiskLevel.Low,
            Reg.CuString(@"Control Panel\Desktop", "WaitToKillAppTimeout", "2000"),
            Reg.LmString(@"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000")),
        new RegistryTweak(c, "fault-tolerant-heap-off", "Turn off the Fault Tolerant Heap",
            "Turns off the Fault Tolerant Heap, which watches apps that crash and applies fixes that can change how games and apps behave.",
            TweakCategory.System, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Microsoft\FTH", "Enabled", 0))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "storage-sense-off", "Turn off Storage Sense",
            "Stops Storage Sense from deleting temporary files and old items in the Recycle Bin and Downloads on its own.",
            TweakCategory.System, RiskLevel.Low,
            Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 0)),
        new ServiceStartTweak(c, "sysmain-off", "Turn off SysMain (SuperFetch)",
            "Disables the SysMain service, which preloads apps into memory and manages memory compression.",
            TweakCategory.System, RiskLevel.Medium, 4, "SysMain")
        {
            NeedsRestart = true,
            Warning = "On a hard disk, apps may open more slowly. With SysMain off, Windows also stops compressing memory.",
        },
        new ServiceStartTweak(c, "search-indexing-off", "Turn off Windows Search indexing",
            "Disables the WSearch service, which indexes files in the background for Windows search. Saves disk writes and CPU.",
            TweakCategory.System, RiskLevel.Medium, 4, "WSearch")
        {
            NeedsRestart = true,
            Warning = "Searching files from the Start menu and File Explorer becomes slower, and Outlook search may stop working.",
        },
        new ServiceStartTweak(c, "idle-services-manual", "Set idle services to manual",
            "Sets Offline Files, Maps and Storage services to start only when something needs them.",
            TweakCategory.System, RiskLevel.Low, 3, "CscService", "MapsBroker", "StorSvc")
        {
            NeedsRestart = true,
        },
        new AutoLoginTweak(c),

        // Security trade-offs: real performance, real cost. They are last, high risk, and say so before they run.
        new RegistryTweak(c, "memory-integrity-off", "Turn off Memory integrity (HVCI)",
            "Turns off Memory integrity, the Core isolation feature that runs the kernel's code checks inside a virtual machine. It can give back some frame rate in games, mostly on older processors.",
            TweakCategory.System, RiskLevel.High,
            Reg.LmDword(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0))
        {
            NeedsRestart = true,
            Warning = "Memory integrity protects Windows against malware that targets the kernel and against vulnerable drivers. With it off, a bad driver can take over the PC. Leave it on unless you measured a real loss in your games, and turn it back on with Undo.",
        },
        SecurityTradeoffTweaks.HypervisorOff(c),
    };
}

/// <summary>Reveals the "users must enter a password" checkbox and opens netplwiz so the user can set up auto sign-in.</summary>
public sealed class AutoLoginTweak : RegistryTweak
{
    public AutoLoginTweak(TweakContext ctx)
        : base(ctx, "autologin-netplwiz", "Set up automatic sign-in (netplwiz)",
            "Opens netplwiz so you can pick the account that signs in without a password. The app never stores a password.",
            TweakCategory.System, RiskLevel.Medium,
            Reg.LmDword(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device", "DevicePasswordLessBuildVersion", 0, onlyIfKeyExists: true))
    {
        Warning = "Signing in without a password lets anyone with physical access to the PC use your account.";
    }

    protected override string? OnApplied(TweakSnapshot snapshot)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("netplwiz.exe") { UseShellExecute = true });
        return "netplwiz opened. Clear \"Users must enter a user name and password to use this computer\" and confirm the account.";
    }
}
