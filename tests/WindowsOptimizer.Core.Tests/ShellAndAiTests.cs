using WindowsOptimizer.Core;

namespace WindowsOptimizer.Core.Tests;

public sealed class ShellAndAiTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private sealed class AppxRunner(string listJson) : ICommandRunner
    {
        public List<string> Scripts { get; } = new();

        public CommandResult Run(string fileName, string arguments, int timeoutMs = 120_000) => new(0, "", "");

        public CommandResult RunPowerShell(string script, int timeoutMs = 300_000)
        {
            Scripts.Add(script);
            return new(0, script.Contains("Get-AppxPackage") ? listJson : "", "");
        }
    }

    [Fact]
    public void Registry_tweaks_need_admin_only_when_they_write_to_HKLM()
    {
        var all = TweakCatalog.Create(_env.Context).ToDictionary(t => t.Id);

        Assert.True(all["recall-snapshots-off"].RequiresAdmin);
        Assert.True(all["updates-pause-auto"].RequiresAdmin);
        Assert.False(all["file-extensions-show"].RequiresAdmin);
        Assert.False(all["mouse-accel-off"].RequiresAdmin);
        Assert.True(all["dns-google"].RequiresAdmin);
    }

    [Fact]
    public void Taskbar_and_Explorer_tweaks_say_they_restart_Explorer()
    {
        var all = TweakCatalog.Create(_env.Context).ToDictionary(t => t.Id);

        Assert.True(all["file-extensions-show"].RestartsExplorer);
        Assert.True(all["explorer-compact"].RestartsExplorer);
        Assert.False(all["game-mode-on"].RestartsExplorer);
    }

    [Fact]
    public void A_curated_set_is_marked_recommended_and_none_of_them_is_high_risk()
    {
        var recommended = TweakCatalog.Create(_env.Context).Where(t => t.Recommended).ToList();

        Assert.NotEmpty(recommended);
        Assert.DoesNotContain(recommended, t => t.Risk == RiskLevel.High);
    }

    [Fact]
    public void Apps_that_are_not_installed_are_unavailable_with_a_reason()
    {
        var nvidia = TweakCatalog.Create(_env.Context).Single(t => t.Id == "nvidia-telemetry-off");

        Assert.False(nvidia.IsAvailable());
        Assert.Contains("NVIDIA", nvidia.UnavailableReason);
    }

    [Fact]
    public void The_Windows_11_tweaks_round_trip_when_they_apply_to_this_PC()
    {
        var ids = new[] { "file-extensions-show", "hidden-files-show", "context-menu-classic", "taskbar-left", "photo-viewer-classic", "new-bat-reg", "paint-ai-off" };
        var all = TweakCatalog.Create(_env.Context).Where(t => ids.Contains(t.Id) && t.IsAvailable()).ToList();

        Assert.NotEmpty(all);
        foreach (var tweak in all)
        {
            Assert.True(tweak.Apply().Success, tweak.Id);
            Assert.Equal(TweakStatus.Applied, tweak.GetStatus());
            Assert.True(tweak.Undo().Success, tweak.Id);
            Assert.Equal(TweakStatus.NotApplied, tweak.GetStatus());
        }
    }

    [Fact]
    public void The_classic_context_menu_writes_the_default_value_of_its_key_and_removes_it_again()
    {
        var tweak = TweakCatalog.Create(_env.Context).Single(t => t.Id == "context-menu-classic");
        if (!tweak.IsAvailable()) return;

        const string key = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
        tweak.Apply();
        Assert.Equal(RegValue.Str(""), _env.Registry.GetValue(RegHive.CurrentUser, key, ""));
        Assert.Equal("(Default)", tweak.Preview().Count > 0 ? "(Default)" : "");

        tweak.Undo();
        Assert.False(_env.Registry.KeyExists(RegHive.CurrentUser, key));
    }

    [Fact]
    public void Installed_AI_apps_are_read_from_a_list_or_a_single_object()
    {
        var many = new AppxRunner("""[{"name":"Microsoft.Copilot","full":"Microsoft.Copilot_1.0_x64__abc"},{"name":"Microsoft.MicrosoftOfficeHub","full":"Microsoft.MicrosoftOfficeHub_2.0_x64__abc"}]""");
        var one = new AppxRunner("""{"name":"Microsoft.Copilot","full":"Microsoft.Copilot_1.0_x64__abc"}""");

        Assert.Equal(new[] { "Microsoft Copilot", "Microsoft 365 Copilot" }, InstalledAiApps.List(_env.Context with { Runner = many }).Select(a => a.DisplayName));
        Assert.Single(InstalledAiApps.List(_env.Context with { Runner = one }));
        Assert.Empty(InstalledAiApps.List(_env.Context with { Runner = new AppxRunner("") }));
        Assert.Empty(InstalledAiApps.List(_env.Context with { Runner = new AppxRunner("not json") }));
    }

    [Fact]
    public void Only_apps_on_the_known_list_with_a_plain_package_name_can_be_removed()
    {
        var runner = new AppxRunner("");
        var ctx = _env.Context with { Runner = runner };

        Assert.True(InstalledAiApps.Remove(ctx, new AiApp("Microsoft Copilot", "Microsoft.Copilot", "Microsoft.Copilot_1.0_x64__abc")).Success);
        Assert.Contains("Remove-AppxPackage -Package 'Microsoft.Copilot_1.0_x64__abc'", runner.Scripts.Last());

        runner.Scripts.Clear();
        Assert.False(InstalledAiApps.Remove(ctx, new AiApp("Something", "Contoso.Thing", "Contoso.Thing_1_x64__abc")).Success);
        Assert.False(InstalledAiApps.Remove(ctx, new AiApp("Microsoft Copilot", "Microsoft.Copilot", "x'; Remove-Item C:\\ -Recurse #")).Success);
        Assert.Empty(runner.Scripts);
    }
}
