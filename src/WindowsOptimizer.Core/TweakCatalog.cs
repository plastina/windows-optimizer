namespace WindowsOptimizer.Core;

/// <summary>Every tweak the app offers, in display order. Add a new one here.</summary>
public static class TweakCatalog
{
    private const string WindowsUpdatePolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string International = @"Control Panel\International";

    public static IReadOnlyList<ITweak> Create(TweakContext c) => new ITweak[]
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

        // Language and region
        new LanguageTweak(c)
        {
            NeedsRestart = true,
            Warning = "Changes the Windows language, region and keyboard. If the en-US language pack isn't installed, Windows downloads it, which can take a few minutes.",
        },

        // Network
        new DnsTweak(c),

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
        MaintenanceTweaks.ClearRecentHistory(c),
        MaintenanceTweaks.OneDriveRemove(c),

        // System
        MaintenanceTweaks.RestorePoint(c),
        MaintenanceTweaks.TrimSystemDrive(c),
        MaintenanceTweaks.LargeFilesReport(c),
        new AutoLoginTweak(c),
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
