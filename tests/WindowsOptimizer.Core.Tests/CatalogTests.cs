using System.Text.RegularExpressions;
using WindowsOptimizer.Core;

namespace WindowsOptimizer.Core.Tests;

public sealed class CatalogTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    public void Ids_are_unique_kebab_case()
    {
        var ids = TweakCatalog.Create(_env.Context).Select(t => t.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches(new Regex("^[a-z0-9]+(-[a-z0-9]+)*$"), id));
    }

    [Fact]
    public void Every_tweak_has_a_name_and_a_description()
    {
        Assert.All(TweakCatalog.Create(_env.Context), t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Description), t.Id);
        });
    }

    [Fact]
    public void High_risk_and_irreversible_tweaks_carry_a_warning()
    {
        Assert.All(
            TweakCatalog.Create(_env.Context).Where(t => t.Risk == RiskLevel.High),
            t => Assert.False(string.IsNullOrWhiteSpace(t.Warning), t.Id));
    }

    [Fact]
    public void Reading_status_never_throws_on_an_empty_registry()
    {
        foreach (var tweak in TweakCatalog.Create(_env.Context).Where(t => t.IsAvailable()))
        {
            var status = Record.Exception(() => tweak.GetStatus());
            Assert.Null(status);
        }
    }

    [Fact]
    public void Service_tweaks_only_touch_services_that_exist()
    {
        const string sysMain = @"SYSTEM\CurrentControlSet\Services\SysMain";
        _env.Registry.SetValue(RegHive.LocalMachine, sysMain, "Start", RegValue.DWord(2));

        var tweak = new ServiceStartTweak(_env.Context, "test-services", "Test", "Test",
            TweakCategory.System, RiskLevel.Low, 4, "SysMain", "NotAService");

        Assert.True(tweak.IsAvailable());
        Assert.True(tweak.Apply().Success);
        Assert.Equal(RegValue.DWord(4), _env.Registry.GetValue(RegHive.LocalMachine, sysMain, "Start"));
        Assert.False(_env.Registry.KeyExists(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\NotAService"));

        Assert.True(tweak.Undo().Success);
        Assert.Equal(RegValue.DWord(2), _env.Registry.GetValue(RegHive.LocalMachine, sysMain, "Start"));
    }

    [Fact]
    public void Service_tweaks_are_hidden_when_no_service_exists() =>
        Assert.False(new ServiceStartTweak(_env.Context, "test-services", "Test", "Test",
            TweakCategory.System, RiskLevel.Low, 4, "NotAService").IsAvailable());

    [Fact]
    public void Task_tweaks_disable_existing_tasks_and_reenable_only_those_that_were_enabled()
    {
        // Exit code 1 mimics PowerShell when the last task looked up doesn't exist.
        var runner = new TaskRunner { States = "\\A\\One|Ready\r\n\\A\\Two|Disabled\r\n", ReadExitCode = 1 };
        var ctx = _env.Context with { Runner = runner };
        var tweak = new ScheduledTaskTweak(ctx, "test-tasks", "Test", "Test",
            TweakCategory.Privacy, RiskLevel.Low, @"\A\One", @"\A\Two", @"\A\Missing");

        Assert.Equal(TweakStatus.Partial, tweak.GetStatus());

        Assert.True(tweak.Apply().Success);
        var disable = runner.Scripts.Last();
        Assert.Contains("Disable-ScheduledTask", disable);
        Assert.Contains(@"'\A\One'", disable);
        Assert.DoesNotContain("Missing", disable);

        Assert.True(tweak.Undo().Success);
        var enable = runner.Scripts.Last();
        Assert.Contains("Enable-ScheduledTask", enable);
        Assert.Contains(@"'\A\One'", enable);
        Assert.DoesNotContain(@"'\A\Two'", enable);
    }

    private sealed class TaskRunner : ICommandRunner
    {
        public string States { get; init; } = "";
        public int ReadExitCode { get; init; }
        public List<string> Scripts { get; } = new();

        public CommandResult Run(string fileName, string arguments, int timeoutMs = 120_000) => new(0, "", "");

        public CommandResult RunPowerShell(string script, int timeoutMs = 300_000)
        {
            Scripts.Add(script);
            return script.Contains("Get-ScheduledTask") ? new(ReadExitCode, States, "") : new(0, "", "");
        }
    }

    [Fact]
    public void Every_registry_toggle_round_trips_on_an_empty_registry()
    {
        foreach (var tweak in TweakCatalog.Create(_env.Context).OfType<RegistryTweak>().Where(t => t is not AutoLoginTweak && t.IsAvailable()))
        {
            var applied = tweak.Apply();
            Assert.True(applied.Success, $"{tweak.Id}: {applied.Message}");
            Assert.Equal(TweakStatus.Applied, tweak.GetStatus());

            var undone = tweak.Undo();
            Assert.True(undone.Success, $"{tweak.Id}: {undone.Message}");
            Assert.Equal(TweakStatus.NotApplied, tweak.GetStatus());
        }
    }
}
