using System.Text.RegularExpressions;

namespace WindowsOptimizer.Core;

/// <summary>Windows AI features, and the File Explorer / taskbar / Start menu behaviour people usually change by hand.</summary>
public static class ShellAndAiTweaks
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string WindowsAi = @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI";
    private const string ExplorerPolicyMachine = @"SOFTWARE\Policies\Microsoft\Windows\Explorer";
    private const string ExplorerPoliciesMachine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string ShellBlocked = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    private static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    public static IReadOnlyList<ITweak> Create(TweakContext c)
    {
        // Windows blocks third-party programs from writing some taskbar values (UCPD). Better to say so than to fail.
        bool Ucpd() => c.Registry.GetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\UCPD", "Start") is { Kind: RegKind.DWord } start
            && (int)start.Data != 4;

        RegistryTweak Explorer(string id, string name, string description, TweakCategory category, params RegistryEdit[] edits) =>
            new(c, id, name, description, category, RiskLevel.Low, edits) { RestartsExplorer = true };

        RegistryTweak Plain(string id, string name, string description, TweakCategory category, params RegistryEdit[] edits) =>
            new(c, id, name, description, category, RiskLevel.Low, edits);

        const string Win11Only = "This setting only exists on Windows 11.";

        var tweaks = new List<ITweak>
        {
            // ---------- AI and Copilot ----------
            Plain("recall-snapshots-off", "Don't save screenshots for Recall",
                "Turns off, by policy, the screenshots Windows saves for Recall. It only matters on Copilot+ PCs; on other PCs it just makes sure the feature stays off.",
                TweakCategory.AI, Reg.LmDword(WindowsAi, "DisableAIDataAnalysis", 1)),
            Plain("click-to-do-off", "Turn off Click to Do",
                "Turns off, by policy, Click to Do, which looks at what is on the screen to suggest actions. It only matters on Copilot+ PCs.",
                TweakCategory.AI, Reg.LmDword(WindowsAi, "DisableClickToDo", 1)),
            Plain("settings-ai-agent-off", "No AI search in Settings",
                "Turns off, by policy, the AI agent that reads the search box in the Settings app as plain language. It only has an effect on Windows builds that include it.",
                TweakCategory.AI, Reg.LmDword(WindowsAi, "DisableSettingsAgent", 1)),
            Plain("paint-ai-off", "Turn off Paint's AI features",
                "Turns off, by policy, Cocreator, generative fill and the image creator in Paint.",
                TweakCategory.AI,
                Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\Paint", "DisableCocreator", 1),
                Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\Paint", "DisableGenerativeFill", 1),
                Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\Paint", "DisableImageCreator", 1)),
            Plain("notepad-ai-off", "Turn off Notepad's AI features",
                "Turns off, by policy, the AI writing tools (such as Rewrite) in Notepad.",
                TweakCategory.AI, Reg.LmDword(@"SOFTWARE\Policies\WindowsNotepad", "DisableAIFeatures", 1)),
            Plain("edge-sidebar-off", "Turn off the Edge sidebar (Copilot)",
                "Turns off, by policy, the Edge sidebar, where Copilot and pinned apps live. The bar never shows up.",
                TweakCategory.AI, Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Edge", "HubsSidebarEnabled", 0)),
            new OptionalFeatureTweak(c, "recall-component-remove", "Remove the Recall component",
                "Uninstalls the optional Windows feature called Recall. You can install it again by undoing this; the reinstall may need an internet connection.",
                RiskLevel.Medium, "Recall", enable: false, "This version of Windows doesn't include the Recall component.", TweakCategory.AI),

            // ---------- File Explorer ----------
            Explorer("file-extensions-show", "Show file extensions",
                "File Explorer shows the extension of every file (.exe, .zip), which helps spot files pretending to be something else.",
                TweakCategory.Appearance, Reg.CuDword(Advanced, "HideFileExt", 0)),
            Explorer("hidden-files-show", "Show hidden files",
                "File Explorer shows files and folders marked as hidden. Protected system files stay hidden.",
                TweakCategory.Appearance, Reg.CuDword(Advanced, "Hidden", 1)),
            Explorer("explorer-compact", "Compact File Explorer",
                "Reduces the spacing between items in File Explorer, as in Windows 10.",
                TweakCategory.Appearance, Reg.CuDword(Advanced, "UseCompactMode", 1)),
            Explorer("explorer-no-home-gallery", "File Explorer without Home and Gallery",
                "File Explorer opens on This PC and drops the Home and Gallery items from the side panel.",
                TweakCategory.Appearance,
                Reg.CuDword(Advanced, "LaunchTo", 1),
                Reg.CuDword(@"Software\Classes\CLSID\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}", "System.IsPinnedToNameSpaceTree", 0),
                Reg.CuDword(@"Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}", "System.IsPinnedToNameSpaceTree", 0)),
            Plain("copy-details-always", "Always show copy details",
                "The copy and move window always shows speed, remaining items and the graph, without having to expand it.",
                TweakCategory.Appearance, Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\OperationStatusManager", "EnthusiastMode", 1)),
            Plain("shortcut-suffix-off", "New shortcuts without \"- Shortcut\"",
                "Shortcuts created from now on get only the file's name, without the \"- Shortcut\" suffix. Existing shortcuts don't change.",
                TweakCategory.Appearance,
                Reg.CuString(@"Software\Microsoft\Windows\CurrentVersion\Explorer\NamingTemplates", "ShortcutNameTemplate", "\"%s.lnk\"")),
            new RegistryTweak(c, "context-menu-many-files", "Context menu with many files selected",
                "Lets the right-click menu and multiple actions work with up to 200 selected files, instead of the small default limit.",
                TweakCategory.Appearance, RiskLevel.Low, Reg.LmDword(ExplorerPoliciesMachine, "MultipleInvokePromptMinimum", 200)),
            Explorer("cast-to-device-off", "No \"Cast to device\" in the menu",
                "Removes \"Cast to device\" from the right-click menu of files and folders.",
                TweakCategory.Appearance, Reg.CuString(ShellBlocked, "{7AD84985-87B4-4a16-BE58-8B72A5B390F7}", "Play to menu")),
            Explorer("compat-troubleshoot-off", "No \"Troubleshoot compatibility\" in the menu",
                "Removes \"Troubleshoot compatibility\" from the right-click menu of programs.",
                TweakCategory.Appearance, Reg.CuString(ShellBlocked, "{1d27f844-3a1f-4410-85ac-14651078412d}", "Compatibility troubleshooter")),
            new RegistryTweak(c, "new-bat-reg", "Create .bat and .reg files from the New menu",
                "Adds \"Batch file\" (.bat) and \"Registration entries\" (.reg) to the New item of the right-click menu.",
                TweakCategory.Appearance, RiskLevel.Low,
                Reg.LmString(@"SOFTWARE\Classes\.bat\ShellNew", "NullFile", ""),
                Reg.LmString(@"SOFTWARE\Classes\.reg\ShellNew", "NullFile", "")) { RestartsExplorer = true },
            Plain("print-screen-no-snip", "Print Screen without the Snipping Tool",
                "The Print Screen key goes back to copying the screen to the clipboard instead of opening the Snipping Tool.",
                TweakCategory.Appearance, Reg.CuDword(@"Control Panel\Keyboard", "PrintScreenKeyForSnippingEnabled", 0)),
            new RegistryTweak(c, "photo-viewer-classic", "Classic Photo Viewer in \"Open with\"",
                "Brings the classic Windows Photo Viewer back as an option in \"Open with\" for pictures. It doesn't change your default app.",
                TweakCategory.Appearance, RiskLevel.Low, PhotoViewerEdits()),
            Plain("wallpaper-quality", "Wallpaper without quality loss",
                "Windows stops recompressing the wallpaper. It applies to the next picture you pick, not to the current one.",
                TweakCategory.Appearance, Reg.CuDword(@"Control Panel\Desktop", "JPEGImportQuality", 100)),
            Plain("snap-off", "Turn off window snapping",
                "Dragging a window to the edge of the screen no longer resizes it (Snap).",
                TweakCategory.Appearance, Reg.CuString(@"Control Panel\Desktop", "WindowArrangementActive", "0")),
            Plain("shake-minimize-off", "No minimizing by shaking",
                "Shaking a window with the mouse no longer minimizes all the others.",
                TweakCategory.Appearance, Reg.CuDword(Advanced, "DisallowShaking", 1)),
            Plain("alt-tab-windows-only", "Alt+Tab with windows only",
                "Alt+Tab lists only open windows, without Edge tabs or suggested items.",
                TweakCategory.Appearance, Reg.CuDword(Advanced, "MultiTaskingAltTabFilter", 3)),
            Plain("scrollbars-always", "Scroll bars always visible",
                "The scroll bars of windows no longer shrink or hide when they aren't in use.",
                TweakCategory.Appearance, Reg.CuDword(@"Control Panel\Accessibility", "DynamicScrollbars", 0)),
            Plain("no-peek-desktop", "No desktop preview",
                "Turns off the desktop preview that appears when the mouse rests on the corner of the taskbar.",
                TweakCategory.Appearance, Reg.CuDword(Advanced, "DisablePreviewDesktop", 1)),

            // ---------- Taskbar and Start menu ----------
            Explorer("taskbar-left", "Align the taskbar to the left",
                "Moves the taskbar icons from the center to the left, as in Windows 10.",
                TweakCategory.Appearance, Reg.CuDword(Advanced, "TaskbarAl", 0)),
            Explorer("task-view-hide", "Hide the Task View button",
                "Removes the Task View button (virtual desktops) from the taskbar.",
                TweakCategory.Appearance, Reg.CuDword(Advanced, "ShowTaskViewButton", 0)),
            Explorer("taskbar-search-hidden", "Hide the taskbar search",
                "Removes the search box from the taskbar completely. Search is still in the Start menu. It writes the same value as \"Search as an icon\".",
                TweakCategory.Appearance, Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 0)),
            Explorer("taskbar-search-icon", "Search as an icon",
                "Shrinks the taskbar search box to a magnifier icon. It writes the same value as \"Hide the taskbar search\".",
                TweakCategory.Appearance, Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 1)),
            Plain("taskbar-end-task", "\"End task\" in the taskbar right-click menu",
                "Adds \"End task\" to the right-click menu of apps in the taskbar, which closes the whole process without opening Task Manager.",
                TweakCategory.Appearance,
                Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", 1)),
            new RegistryTweak(c, "start-no-recommended", "Start menu without Recommended",
                "Hides the Recommended section of the Start menu, which lists suggested files and apps.",
                TweakCategory.Appearance, RiskLevel.Low, Reg.LmDword(ExplorerPolicyMachine, "HideRecommendedSection", 1)) { RestartsExplorer = true },
            Explorer("start-more-pins", "More pins in the Start menu",
                "Gives more room to pinned apps in the Start menu, leaving less for suggestions.",
                TweakCategory.Appearance, Reg.CuDword(Advanced, "Start_Layout", 1)),
            new RegistryTweak(c, "settings-no-home", "Settings without the Home page",
                "Hides the Home page of the Settings app, which gathers cards and suggestions. The other pages stay available.",
                TweakCategory.Appearance, RiskLevel.Low, Reg.LmString(ExplorerPoliciesMachine, "SettingsPageVisibility", "hide:home")),
            new RegistryTweak(c, "notification-center-off", "Turn off the notification center",
                "Removes the Notification Center and the calendar from the notification area of the taskbar.",
                TweakCategory.Appearance, RiskLevel.Medium, Reg.LmDword(ExplorerPolicyMachine, "DisableNotificationCenter", 1))
            {
                NeedsRestart = true,
                Warning = "Notifications stop showing up and you lose the calendar flyout. Turn it back on with Undo.",
            },
            new RegistryTweak(c, "login-blur-off", "Sign-in screen without blur",
                "Removes the acrylic blur from the background of the sign-in screen, showing the picture sharp.",
                TweakCategory.Appearance, RiskLevel.Low, Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\System", "DisableAcrylicBackgroundOnLogon", 1)),
            new RegistryTweak(c, "meet-now-off", "No \"Meet Now\" button",
                "Removes the Meet Now icon from the notification area, next to the clock.",
                TweakCategory.Appearance, RiskLevel.Low, Reg.LmDword(ExplorerPoliciesMachine, "HideSCAMeetNow", 1)) { RestartsExplorer = true },
        };

        // Windows 11 only.
        foreach (var id in new[] { "taskbar-left", "taskbar-end-task", "start-no-recommended", "start-more-pins", "explorer-no-home-gallery" })
        {
            var tweak = (RegistryTweak)tweaks.First(t => t.Id == id);
            tweak.Available = () => IsWindows11;
            tweak.UnavailableReason = Win11Only;
        }

        // The Widgets policy is one of the settings UCPD protects from other programs.
        tweaks.Add(new RegistryTweak(c, "widgets-hide", "Turn off Widgets",
            "Turns off the Widgets panel and its taskbar button, by policy, for every user.",
            TweakCategory.Appearance, RiskLevel.Low, Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0))
        {
            RestartsExplorer = true,
            Available = () => !Ucpd(),
            UnavailableReason = "Windows blocks this setting for other programs (UCPD protection). Hide the button in Settings > Personalization > Taskbar.",
        });

        tweaks.Add(MakeContextMenu(c));
        return tweaks;
    }

    /// <summary>The Windows 10 style right-click menu: an empty default value under a special COM key brings it back.</summary>
    private static ITweak MakeContextMenu(TweakContext c) =>
        new RegistryTweak(c, "context-menu-classic", "Full right-click menu (classic)",
            "The right-click menu opens the full Windows 10 menu again, with no need for \"Show more options\".",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.CuString(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", ""))
        {
            RestartsExplorer = true,
            Available = () => IsWindows11,
            UnavailableReason = "This setting only exists on Windows 11.",
        };

    private static RegistryEdit[] PhotoViewerEdits()
    {
        const string capabilities = @"SOFTWARE\Microsoft\Windows Photo Viewer\Capabilities";
        var edits = new List<RegistryEdit>
        {
            Reg.LmString(capabilities, "ApplicationName", "@%ProgramFiles%\\Windows Photo Viewer\\photoviewer.dll,-3009"),
            Reg.LmString(capabilities, "ApplicationDescription", "@%ProgramFiles%\\Windows Photo Viewer\\photoviewer.dll,-3069"),
            Reg.LmString(@"SOFTWARE\RegisteredApplications", "Windows Photo Viewer", capabilities),
        };
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" })
            edits.Add(Reg.LmString(capabilities + @"\FileAssociations", extension, "PhotoViewer.FileAssoc.Tiff"));
        return edits.ToArray();
    }

    private static TweakResult FromPowerShell(CommandResult result, string success) =>
        result.Success
            ? TweakResult.Ok(success)
            : TweakResult.Fail(result.Text.Length > 0 ? result.Text : $"The command exited with code {result.ExitCode} and no message.");
}

/// <summary>An AI app installed for the current user that the app can remove.</summary>
public sealed record AiApp(string DisplayName, string PackageName, string PackageFullName);

/// <summary>Finds the AI apps that come with Windows or Microsoft 365 and removes them for the current user.</summary>
public static class InstalledAiApps
{
    // Only apps that are safe to remove and can be installed again from the Microsoft Store.
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.Copilot"] = "Microsoft Copilot",
        ["Microsoft.MicrosoftOfficeHub"] = "Microsoft 365 Copilot",
    };

    public static IReadOnlyList<AiApp> List(TweakContext c)
    {
        var names = string.Join(",", Known.Keys.Select(k => $"'{k}'"));
        var result = c.Runner.RunPowerShell($$"""
            @(Get-AppxPackage | Where-Object { @({{names}}) -contains $_.Name } |
              ForEach-Object { [ordered]@{ name = $_.Name; full = $_.PackageFullName } }) | ConvertTo-Json -Compress
            """);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return Array.Empty<AiApp>();

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(result.Output);
            var items = document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToList()
                : new List<System.Text.Json.JsonElement> { document.RootElement };

            return items
                .Select(e => (Name: e.TryGetProperty("name", out var n) ? n.GetString() : null,
                              Full: e.TryGetProperty("full", out var f) ? f.GetString() : null))
                .Where(x => x.Name is not null && x.Full is not null && Known.ContainsKey(x.Name))
                .Select(x => new AiApp(Known[x.Name!], x.Name!, x.Full!))
                .ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<AiApp>();
        }
    }

    public static TweakResult Remove(TweakContext c, AiApp app)
    {
        // The package name comes from our own list, but it still goes through a strict pattern before it reaches PowerShell.
        if (!Known.ContainsKey(app.PackageName) || !Regex.IsMatch(app.PackageFullName, @"^[A-Za-z0-9._~-]+$"))
            return TweakResult.Fail("That package is not on the list of apps this tool can remove.");

        var result = c.Runner.RunPowerShell($"Remove-AppxPackage -Package '{app.PackageFullName}'");
        return result.Success
            ? TweakResult.Ok($"{app.DisplayName} removed. You can install it again from the Microsoft Store.")
            : TweakResult.Fail(result.Text);
    }
}
