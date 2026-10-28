using System.Text.Json.Nodes;
using WindowsOptimizer.Core;

namespace WindowsOptimizer.Core.Tests;

public sealed class MoreTweaksTests : IDisposable
{
    private readonly TestEnvironment _env = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WindowsOptimizerMore", Guid.NewGuid().ToString("N"));

    public MoreTweaksTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        _env.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Answers the component listings and records every other script, like a Windows with some components installed.</summary>
    private sealed class ComponentRunner : ICommandRunner
    {
        public string Capabilities { get; set; } = "";
        public string Features { get; set; } = "";
        public List<string> Scripts { get; } = new();
        public List<string> Commands { get; } = new();

        public CommandResult Run(string fileName, string arguments, int timeoutMs = 120_000)
        {
            Commands.Add($"{fileName} {arguments}");
            return new(0, "", "");
        }

        public CommandResult RunPowerShell(string script, int timeoutMs = 300_000)
        {
            Scripts.Add(script);
            if (script.StartsWith("Get-WindowsCapability")) return new(0, Capabilities, "");
            if (script.StartsWith("Get-WindowsOptionalFeature")) return new(0, Features, "");
            return new(0, "", "");
        }
    }

    [Fact]
    public void A_capability_tweak_reads_state_removes_what_is_installed_and_adds_it_back()
    {
        var runner = new ComponentRunner
        {
            Capabilities = "Language.Handwriting~~~en-US~0.0.1.0|Installed\nLanguage.Handwriting~~~pt-BR~0.0.1.0|NotPresent\nOther.Thing~~~~0.0.1.0|Installed\n",
        };
        var tweak = new CapabilityTweak(_env.Context with { Runner = runner }, "t", "T", "d", RiskLevel.Low, "Language.Handwriting");

        Assert.Equal(TweakStatus.NotApplied, tweak.GetStatus());
        Assert.True(tweak.Apply().Success);
        var remove = runner.Scripts.Last();
        Assert.Contains("Remove-WindowsCapability", remove);
        Assert.Contains("Language.Handwriting~~~en-US~0.0.1.0", remove);
        Assert.DoesNotContain("pt-BR", remove);
        Assert.DoesNotContain("Other.Thing", remove);

        runner.Capabilities = "Language.Handwriting~~~en-US~0.0.1.0|NotPresent\nLanguage.Handwriting~~~pt-BR~0.0.1.0|NotPresent\n";
        Assert.Equal(TweakStatus.Applied, tweak.GetStatus());

        Assert.True(tweak.Undo().Success);
        Assert.Contains("Add-WindowsCapability", runner.Scripts.Last());
        Assert.Contains("Language.Handwriting~~~en-US~0.0.1.0", runner.Scripts.Last());
    }

    [Fact]
    public void A_capability_this_windows_does_not_have_has_an_unknown_state_and_a_reason()
    {
        var tweak = new CapabilityTweak(_env.Context with { Runner = new ComponentRunner { Capabilities = "Something.Else~~~~0.0.1.0|Installed" } },
            "t", "T", "d", RiskLevel.Low, "Hello.Face");

        Assert.Equal(TweakStatus.Unknown, tweak.GetStatus());
        Assert.NotNull(tweak.UnavailableReason);
    }

    [Fact]
    public void A_capability_with_an_odd_name_is_never_sent_to_powershell()
    {
        var runner = new ComponentRunner { Capabilities = "Language.Speech~~~x'; calc #|Installed" };
        var tweak = new CapabilityTweak(_env.Context with { Runner = runner }, "t", "T", "d", RiskLevel.Low, "Language.Speech");

        Assert.False(tweak.Apply().Success);
        Assert.DoesNotContain(runner.Scripts, s => s.Contains("Remove-WindowsCapability"));
    }

    [Fact]
    public void An_optional_feature_can_be_turned_off_or_on_and_undone()
    {
        var runner = new ComponentRunner { Features = "Recall|Enabled\nNetFx3|Disabled\n" };
        var ctx = _env.Context with { Runner = runner };
        var remove = new OptionalFeatureTweak(ctx, "r", "R", "d", RiskLevel.Medium, "Recall", enable: false, "none", TweakCategory.AI);
        var install = new OptionalFeatureTweak(ctx, "n", "N", "d", RiskLevel.Low, "NetFx3", enable: true, "none");

        Assert.Equal(TweakStatus.NotApplied, remove.GetStatus());
        Assert.Equal(TweakStatus.NotApplied, install.GetStatus());

        Assert.True(remove.Apply().Success);
        Assert.Contains("Disable-WindowsOptionalFeature -Online -FeatureName Recall", runner.Scripts.Last());
        Assert.True(remove.CanUndo());
        Assert.True(remove.Undo().Success);
        Assert.Contains("Enable-WindowsOptionalFeature -Online -FeatureName Recall", runner.Scripts.Last());

        Assert.True(install.Apply().Success);
        Assert.Contains("Enable-WindowsOptionalFeature -Online -FeatureName NetFx3", runner.Scripts.Last());

        runner.Features = "Recall|Disabled\nNetFx3|Enabled\n";
        WindowsComponents.Invalidate(ctx);
        Assert.Equal(TweakStatus.Applied, remove.GetStatus());
        Assert.Equal(TweakStatus.Applied, install.GetStatus());
    }

    [Fact]
    public void A_feature_windows_does_not_have_is_unknown()
    {
        var tweak = new OptionalFeatureTweak(_env.Context with { Runner = new ComponentRunner { Features = "Other|Enabled" } },
            "r", "R", "d", RiskLevel.Low, "Recall", enable: false, "none");

        Assert.Equal(TweakStatus.Unknown, tweak.GetStatus());
    }

    [Fact]
    public void A_json_setting_is_set_and_restored_without_touching_the_other_keys()
    {
        var file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, """{"BACKGROUND_COLOR":"#000","START_MINIMIZED":false}""");
        var tweak = new JsonSettingTweak(_env.Context, "j", "J", "d", file, "START_MINIMIZED", true, "not installed");

        Assert.True(tweak.IsAvailable());
        Assert.Equal(TweakStatus.NotApplied, tweak.GetStatus());

        Assert.True(tweak.Apply().Success);
        var applied = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        Assert.True((bool)applied["START_MINIMIZED"]!);
        Assert.Equal("#000", (string)applied["BACKGROUND_COLOR"]!);
        Assert.Equal(TweakStatus.Applied, tweak.GetStatus());

        Assert.True(tweak.Undo().Success);
        Assert.False((bool)JsonNode.Parse(File.ReadAllText(file))!["START_MINIMIZED"]!);
    }

    [Fact]
    public void A_json_setting_that_was_missing_is_removed_again_and_a_missing_file_is_unavailable()
    {
        var file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, """{"A":1}""");
        var tweak = new JsonSettingTweak(_env.Context, "j", "J", "d", file, "SKIP_HOST_UPDATE", true, "not installed");

        tweak.Apply();
        tweak.Undo();
        Assert.Null(JsonNode.Parse(File.ReadAllText(file))!["SKIP_HOST_UPDATE"]);

        Assert.False(new JsonSettingTweak(_env.Context, "j2", "J", "d", Path.Combine(_dir, "nope.json"), "X", true, "not installed").IsAvailable());
    }

    [Fact]
    public void Deleting_registry_keys_exports_them_first_and_imports_them_on_undo()
    {
        var runner = new ComponentRunner();
        var ctx = _env.Context with { Runner = runner };
        _env.Registry.AddKey(RegHive.LocalMachine, @"SOFTWARE\Test\KeyA");
        var tweak = new RegistryKeyDeleteTweak(ctx, "k", "K", "d", TweakCategory.Updates, RiskLevel.Low, RegHive.LocalMachine, @"SOFTWARE\Test\KeyA", @"SOFTWARE\Test\KeyB");

        Assert.Equal(TweakStatus.Partial, tweak.GetStatus());
        Assert.True(tweak.Apply().Success);

        Assert.Contains(runner.Commands, c => c.StartsWith("reg.exe export") && c.Contains(@"HKLM\SOFTWARE\Test\KeyA"));
        Assert.DoesNotContain(runner.Commands, c => c.Contains("KeyB"));
        Assert.False(_env.Registry.KeyExists(RegHive.LocalMachine, @"SOFTWARE\Test\KeyA"));
        Assert.Equal(TweakStatus.Applied, tweak.GetStatus());

        // The fake runner didn't really write the export file; create it so Undo can delete it after the import.
        var exported = Directory.GetFiles(Path.Combine(_env.Context.DataRoot, "state"), "k-*.reg").SingleOrDefault();
        if (exported is null) File.WriteAllText(Path.Combine(_env.Context.DataRoot, "state", "k-0.reg"), "x");

        Assert.True(tweak.Undo().Success);
        Assert.Contains(runner.Commands, c => c.StartsWith("reg.exe import"));
    }

    [Fact]
    public void The_boot_menu_name_is_added_and_the_old_one_restored()
    {
        var runner = new BootRunner();
        var tweak = new BootMenuNameTweak(_env.Context with { Runner = runner });

        Assert.Equal(TweakStatus.NotApplied, tweak.GetStatus());
        Assert.True(tweak.Apply().Success);
        Assert.Contains("/set {current} description \"Windows 11 (Windows Optimizer)\"", runner.Commands.Last());

        Assert.True(tweak.Undo().Success);
        Assert.Contains("/set {current} description \"Windows 11\"", runner.Commands.Last());
    }

    private sealed class BootRunner : ICommandRunner
    {
        public List<string> Commands { get; } = new();

        public CommandResult Run(string fileName, string arguments, int timeoutMs = 120_000)
        {
            Commands.Add(arguments);
            return arguments.StartsWith("/enum") ? new(0, "identifier {current}\r\ndescription             Windows 11\r\n", "") : new(0, "", "");
        }

        public CommandResult RunPowerShell(string script, int timeoutMs = 300_000) => new(0, "", "");
    }

    [Fact]
    public void The_default_profile_value_lives_under_HKU_and_needs_admin()
    {
        var tweak = TweakCatalog.Create(_env.Context).Single(t => t.Id == "numlock-on");

        Assert.True(tweak.RequiresAdmin);
        Assert.True(tweak.Apply().Success);
        Assert.Equal(RegValue.Str("2"), _env.Registry.GetValue(RegHive.Users, @".DEFAULT\Control Panel\Keyboard", "InitialKeyboardIndicators"));
        Assert.True(tweak.Undo().Success);
        Assert.Null(_env.Registry.GetValue(RegHive.Users, @".DEFAULT\Control Panel\Keyboard", "InitialKeyboardIndicators"));
    }

    [Fact]
    public void The_extra_tweaks_are_flagged_where_it_matters()
    {
        var all = TweakCatalog.Create(_env.Context).ToDictionary(t => t.Id);

        Assert.True(all["compat-engine-off"].Experimental);
        Assert.True(all["handwriting-remove"].SlowOperation);
        Assert.True(all["updates-no-reboot-signed-in"].Recommended);
        Assert.Equal(RiskLevel.Medium, all["utc-clock"].Risk);
        Assert.False(string.IsNullOrWhiteSpace(all["utc-clock"].Warning));
        Assert.Equal(TweakCategory.AI, all["recall-component-remove"].Category);
    }
}
