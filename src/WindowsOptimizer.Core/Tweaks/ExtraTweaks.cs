namespace WindowsOptimizer.Core;

/// <summary>More system, update, startup and component tweaks, kept apart from the main catalog.</summary>
public static class ExtraTweaks
{
    private const string WindowsUpdateAu = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string WindowsUpdatePolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string AppCompatPolicy = @"SOFTWARE\Policies\Microsoft\Windows\AppCompat";
    private const string SystemPolicies = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string Orchestrator = @"SOFTWARE\Microsoft\WindowsUpdate\Orchestrator\UScheduler_Oobe";

    public static IReadOnlyList<ITweak> Create(TweakContext c)
    {
        var discord = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord", "settings.json");
        const string discordMissing = "Discord isn't installed (or has never been opened) for this user.";

        return new ITweak[]
        {
            // ---------- Updates ----------
            new RegistryTweak(c, "updates-no-reboot-signed-in", "Don't restart for updates while you're signed in",
                "Stops Windows Update from restarting the PC by itself while someone is signed in, so an update never closes your game or your work.",
                TweakCategory.Updates, RiskLevel.Low, Reg.LmDword(WindowsUpdateAu, "NoAutoRebootWithLoggedOnUsers", 1)),
            new RegistryTweak(c, "updates-notify-only", "Windows Update only notifies",
                "Windows Update tells you before it downloads and installs anything, and does nothing on its own. It writes the same values \"Pause automatic updates\" changes, so use one or the other.",
                TweakCategory.Updates, RiskLevel.Medium,
                Reg.LmDword(WindowsUpdateAu, "NoAutoUpdate", 0),
                Reg.LmDword(WindowsUpdateAu, "AUOptions", 2))
            {
                Warning = "Updates, including security fixes, only install when you say so. Check for them from time to time.",
            },
            new RegistryTweak(c, "update-notifications-off", "No Windows Update notifications",
                "Windows Update stops showing notifications, except the warnings that a restart is needed.",
                TweakCategory.Updates, RiskLevel.Low,
                Reg.LmDword(WindowsUpdatePolicy, "SetUpdateNotificationLevel", 1),
                Reg.LmDword(WindowsUpdatePolicy, "UpdateNotificationLevel", 2)),
            new RegistryTweak(c, "store-auto-update-off", "Store apps don't update by themselves",
                "Apps from the Microsoft Store only update when you open the Store and ask for it.",
                TweakCategory.Updates, RiskLevel.Low, Reg.LmDword(@"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload", 2)),
            new RegistryKeyDeleteTweak(c, "outlook-devhome-reinstall-off", "Don't reinstall the new Outlook and Dev Home",
                "Deletes the Windows Update tasks that put the new Outlook and Dev Home back after you remove them. The keys are exported first, so Undo brings them back.",
                TweakCategory.Updates, RiskLevel.Low, RegHive.LocalMachine,
                Orchestrator + @"\OutlookUpdate", Orchestrator + @"\DevHomeUpdate"),

            // ---------- Power and gaming ----------
            new PowerSettingTweak(c, "core-parking-off", "Keep all CPU cores awake",
                "Sets the minimum of unparked cores to 100% on the active power plan, so Windows never parks processor cores. It removes the delay when a core has to wake up, at the cost of a little more idle power.",
                RiskLevel.Low, "54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583", 100, 100,
                "Minimum unparked cores"),
            new RegistryTweak(c, "foreground-priority", "Give the program in front more CPU time",
                "Sets Win32PrioritySeparation to 0x26, so the program you are using gets longer slices of processor time than the ones in the background.",
                TweakCategory.Gaming, RiskLevel.Low, Reg.LmDword(@"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 0x26))
            {
                NeedsRestart = true,
            },

            // ---------- Startup ----------
            new RegistryTweak(c, "startup-delay-off", "Remove the delay before startup programs",
                "Removes the wait of about ten seconds Windows adds between sign-in and starting your startup programs, so they open right away.",
                TweakCategory.Startup, RiskLevel.Low,
                Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0)),
            new JsonSettingTweak(c, "discord-start-minimized", "Discord opens minimized to the tray",
                "Makes Discord start minimized in the tray instead of showing its window every time. Close Discord before you switch this.",
                discord, "START_MINIMIZED", true, discordMissing),
            new JsonSettingTweak(c, "discord-skip-update", "Discord doesn't check for updates when it opens",
                "Skips Discord's update check at startup, so it opens faster. Updates still happen when the app is reinstalled or updated by hand. Close Discord before you switch this.",
                discord, "SKIP_HOST_UPDATE", true, discordMissing),

            // ---------- System ----------
            new RegistryTweak(c, "long-paths-on", "Allow long file paths (over 260 characters)",
                "Removes the 260-character limit on file paths for apps that say they support it. Useful for Node, Git and Docker; older apps stay limited.",
                TweakCategory.System, RiskLevel.Low, Reg.LmDword(@"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1))
            {
                NeedsRestart = true,
            },
            new ServiceStartTweak(c, "print-spooler-manual", "Print spooler on demand",
                "Sets the print spooler service to manual: it starts only when something needs to print, instead of running all the time. The service running now isn't stopped.",
                TweakCategory.System, RiskLevel.Low, 3, "Spooler"),
            ReservedStorageOff(c),
            new RegistryTweak(c, "utc-clock", "Treat the hardware clock as UTC (dual boot)",
                "Tells Windows to read the hardware clock as UTC, as Linux does, so the time doesn't shift when you switch between the two systems.",
                TweakCategory.System, RiskLevel.Medium, Reg.LmDword(@"SYSTEM\CurrentControlSet\Control\TimeZoneInformation", "RealTimeIsUniversal", 1))
            {
                NeedsRestart = true,
                Warning = "If this PC only runs Windows, the clock will be off by your time zone until Windows corrects it from the internet. Only use this if you dual boot with Linux.",
            },
            new BootMenuNameTweak(c),
            new RegistryTweak(c, "verbose-status", "Detailed messages at startup and shutdown",
                "Shows detailed status messages while Windows starts, shuts down, signs in and signs out, which helps find what is slow.",
                TweakCategory.System, RiskLevel.Low, Reg.LmDword(SystemPolicies, "VerboseStatus", 1)),
            new RegistryTweak(c, "numlock-on", "Num Lock on at startup",
                "The numeric keypad starts on, on the sign-in screen and in your session, instead of remembering the last state.",
                TweakCategory.System, RiskLevel.Low,
                Reg.DefaultProfileString(@"Control Panel\Keyboard", "InitialKeyboardIndicators", "2"),
                Reg.CuString(@"Control Panel\Keyboard", "InitialKeyboardIndicators", "2")),
            new RegistryTweak(c, "powershell-remotesigned", "Allow PowerShell scripts (RemoteSigned)",
                "Lets PowerShell run local scripts and asks for a signature only on the ones downloaded from the internet. Without a value, Windows client uses Restricted. A group policy or a narrower scope can still override it.",
                TweakCategory.System, RiskLevel.Low,
                Reg.LmString(@"SOFTWARE\Microsoft\PowerShell\1\ShellIds\Microsoft.PowerShell", "ExecutionPolicy", "RemoteSigned")),
            new RegistryTweak(c, "compat-assistant-off", "No Program Compatibility Assistant (policy)",
                "Windows stops watching old programs to suggest compatibility modes.",
                TweakCategory.Privacy, RiskLevel.Low, Reg.LmDword(AppCompatPolicy, "DisablePCA", 1)),
            new RegistryTweak(c, "compat-engine-off", "Turn off the compatibility engine",
                "Turns off, by policy, the fixes Windows applies on its own to old programs, including the SwitchBack mechanism.",
                TweakCategory.System, RiskLevel.Medium, Reg.LmDword(AppCompatPolicy, "DisableEngine", 1))
            {
                Experimental = true,
                Warning = "Old programs may stop working or behave differently. Turn it off again with Undo if something breaks.",
            },
            new RegistryTweak(c, "device-metadata-off", "Don't download manufacturer apps and icons",
                "Stops Windows from downloading, by policy, apps and icons from device makers (for example the utilities of peripherals). Drivers are still installed.",
                TweakCategory.Privacy, RiskLevel.Low,
                Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\Device Metadata", "PreventDeviceMetadataFromNetwork", 1),
                Reg.LmDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata", "PreventDeviceMetadataFromNetwork", 1)),

            // ---------- Windows components ----------
            new OptionalFeatureTweak(c, "netfx35-on", "Turn on .NET Framework 3.5",
                "Installs .NET Framework 3.5 (which includes 2.0 and 3.0) as an optional Windows feature. It downloads the files from Windows Update, so it needs an internet connection.",
                RiskLevel.Low, "NetFx3", enable: true, "This version of Windows doesn't offer .NET Framework 3.5 as an optional feature."),
            new OptionalFeatureTweak(c, "rdp-client-remove", "Remove the Remote Desktop client",
                "Uninstalls the Remote Desktop Connection feature (mstsc), used to control another PC over the network.",
                RiskLevel.Medium, "Microsoft-RemoteDesktopConnection", enable: false,
                "This version of Windows doesn't have the Remote Desktop Connection feature as a removable component.")
            {
                Warning = "You won't be able to connect to other PCs with Remote Desktop until you install it again (Undo).",
            },
            new CapabilityTweak(c, "handwriting-remove", "Remove handwriting recognition",
                "Uninstalls the handwriting and math recognition packs of every installed language.",
                RiskLevel.Low, "Language.Handwriting"),
            new CapabilityTweak(c, "speech-remove", "Remove voices and speech recognition",
                "Uninstalls the speech recognition and text-to-speech packs of every installed language.",
                RiskLevel.Medium, "Language.Speech", "Language.TextToSpeech")
            {
                Warning = "Voice typing, Voice Access and Narrator voices stop working until you install them again (Undo).",
            },
            new CapabilityTweak(c, "quick-assist-remove", "Remove Quick Assist",
                "Uninstalls the remote support app Quick Assist, which scammers have used in fake support calls.",
                RiskLevel.Low, "App.Support.QuickAssist"),
            new CapabilityTweak(c, "steps-recorder-remove", "Remove Steps Recorder",
                "Uninstalls Steps Recorder for good. The policy tweak that only turns it off stays available on its own.",
                RiskLevel.Low, "App.StepsRecorder"),
            new CapabilityTweak(c, "hello-face-remove", "Remove Windows Hello Face",
                "Uninstalls the face recognition component of Windows Hello.",
                RiskLevel.Medium, "Hello.Face")
            {
                Warning = "Signing in with your face stops working until you install it again (Undo).",
            },
            new CapabilityTweak(c, "onesync-remove", "Remove the account sync service (OneSync)",
                "Uninstalls the service that syncs mail, contacts and calendar of connected accounts.",
                RiskLevel.Medium, "OneCoreUAP.OneSync")
            {
                Warning = "The Mail and Calendar apps stop syncing your accounts until you install it again (Undo).",
            },
        };
    }

    /// <summary>
    /// Windows holds back about 7 GB for updates (Reserved Storage). DISM turns it off and on; the registry says which it is.
    /// </summary>
    private static ITweak ReservedStorageOff(TweakContext c)
    {
        const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager";

        return new DelegateTweak(
            c, "reserved-storage-off", "Turn off Reserved Storage",
            "Frees the roughly 7 GB Windows keeps in reserve to download updates. Without the reserve, Windows Update may need more free space when it runs.",
            TweakCategory.System, RiskLevel.Low,
            status: () => c.Registry.GetValue(RegHive.LocalMachine, key, "ShippedWithReserves") is { Kind: RegKind.DWord } v
                ? ((int)v.Data == 0 ? TweakStatus.Applied : TweakStatus.NotApplied)
                : TweakStatus.Unknown,
            apply: () => FromCommand(c.Runner.Run("dism.exe", "/Online /Set-ReservedStorageState /State:Disabled", timeoutMs: 600_000), "Reserved Storage turned off."),
            undo: () => FromCommand(c.Runner.Run("dism.exe", "/Online /Set-ReservedStorageState /State:Enabled", timeoutMs: 600_000), "Reserved Storage turned back on."),
            preview: () => new[] { new PreviewLine("Reserved Storage", "on", "off", "DISM /Set-ReservedStorageState") },
            fingerprint: () => c.Runner.Run("dism.exe", "/Online /Get-ReservedStorageState").Output.Trim())
        {
            NeedsAdmin = true,
            UnavailableReason = "This version of Windows doesn't use Reserved Storage.",
            Warning = "Windows refuses to change this while an update is waiting to install. If that happens, finish the update and try again.",
        };
    }

    private static TweakResult FromCommand(CommandResult result, string success) =>
        result.Success
            ? TweakResult.Ok(success)
            : TweakResult.Fail(result.Text.Length > 0 ? result.Text : $"The command exited with code {result.ExitCode} and no message.");
}
