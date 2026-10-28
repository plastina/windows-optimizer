using WindowsOptimizer.Core;

namespace WindowsOptimizer.Core.Tests;

public sealed class GameProfileTests : IDisposable
{
    private readonly TestEnvironment _env = new();
    private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "WindowsOptimizerGames", Guid.NewGuid().ToString("N"), "MyGame");
    private readonly string _exe;

    public GameProfileTests()
    {
        Directory.CreateDirectory(_gameDir);
        _exe = Path.Combine(_gameDir, "MyGame.exe");
        File.WriteAllText(_exe, "x");
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_gameDir)!, recursive: true);

    private const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    [Fact]
    public void The_library_adds_a_game_once_and_removes_it()
    {
        var library = new GameLibrary(Path.Combine(_gameDir, "data"));

        Assert.Null(library.Add(_exe));
        Assert.Equal(new[] { _exe }, library.Games());
        Assert.NotNull(library.Add(_exe));

        library.Remove(_exe.ToUpperInvariant());
        Assert.Empty(library.Games());
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData(@"C:\does\not\exist.exe")]
    [InlineData("")]
    public void The_library_rejects_things_that_are_not_a_game_exe(string path) =>
        Assert.NotNull(GameLibrary.Validate(path));

    [Fact]
    public void A_game_in_the_root_of_a_drive_or_a_shared_folder_is_rejected()
    {
        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        if (File.Exists(shared)) Assert.NotNull(GameLibrary.Validate(shared));
    }

    [Fact]
    public void Each_game_gets_three_tweaks_with_unique_kebab_case_ids()
    {
        var tweaks = GameProfileTweaks.For(_env.Context, _exe);

        Assert.Equal(3, tweaks.Count);
        Assert.Equal(3, tweaks.Select(t => t.Id).Distinct().Count());
        Assert.All(tweaks, t =>
        {
            Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", t.Id);
            Assert.Equal(TweakCategory.GameProfiles, t.Category);
            Assert.Equal(_exe, ((IGameTweak)t).GamePath);
        });
    }

    [Fact]
    public void The_gpu_preference_is_written_for_the_game_and_undone()
    {
        var gpu = GameProfileTweaks.For(_env.Context, _exe)[0];

        Assert.True(gpu.Apply().Success);
        Assert.Equal(RegValue.Str("GpuPreference=2;"), _env.Registry.GetValue(RegHive.CurrentUser, GameProfileTweaks.GpuPreferenceKey, _exe));

        Assert.True(gpu.Undo().Success);
        Assert.Null(_env.Registry.GetValue(RegHive.CurrentUser, GameProfileTweaks.GpuPreferenceKey, _exe));
    }

    [Fact]
    public void The_fullscreen_flag_keeps_the_flags_that_were_already_set()
    {
        _env.Registry.SetValue(RegHive.CurrentUser, Layers, _exe, RegValue.Str("~ RUNASADMIN"));
        var tweak = GameProfileTweaks.For(_env.Context, _exe)[1];

        Assert.Equal(TweakStatus.NotApplied, tweak.GetStatus());
        Assert.True(tweak.Apply().Success);
        Assert.Equal(RegValue.Str("~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE"), _env.Registry.GetValue(RegHive.CurrentUser, Layers, _exe));
        Assert.Equal(TweakStatus.Applied, tweak.GetStatus());

        Assert.True(tweak.Undo().Success);
        Assert.Equal(RegValue.Str("~ RUNASADMIN"), _env.Registry.GetValue(RegHive.CurrentUser, Layers, _exe));
    }

    [Fact]
    public void The_fullscreen_flag_removes_its_value_when_there_was_none()
    {
        var tweak = GameProfileTweaks.For(_env.Context, _exe)[1];

        tweak.Apply();
        Assert.Equal(RegValue.Str("~ DISABLEDXMAXIMIZEDWINDOWEDMODE"), _env.Registry.GetValue(RegHive.CurrentUser, Layers, _exe));

        tweak.Undo();
        Assert.Null(_env.Registry.GetValue(RegHive.CurrentUser, Layers, _exe));
    }

    [Fact]
    public void The_defender_exclusion_warns_and_only_runs_defender_commands_for_the_game_folder()
    {
        var tweak = GameProfileTweaks.For(_env.Context, _exe)[2];
        Assert.Equal(RiskLevel.Medium, tweak.Risk);
        Assert.False(string.IsNullOrWhiteSpace(tweak.Warning));

        Assert.True(tweak.Apply().Success);
        var script = ((FakeRunner)_env.Runner).Commands.Last();
        Assert.Contains("Add-MpPreference", script);
        Assert.Contains(_gameDir, script);

        Assert.True(tweak.Undo().Success);
        Assert.Contains("Remove-MpPreference", ((FakeRunner)_env.Runner).Commands.Last());
    }

    [Fact]
    public void A_folder_that_was_already_excluded_is_not_removed_by_undo()
    {
        _env.Registry.SetValue(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths", _gameDir, RegValue.DWord(0));
        var tweak = GameProfileTweaks.For(_env.Context, _exe)[2];

        Assert.Equal(TweakStatus.Applied, tweak.GetStatus());
        Assert.True(tweak.Apply().Success);

        Assert.False(tweak.CanUndo());
        Assert.DoesNotContain(((FakeRunner)_env.Runner).Commands, c => c.Contains("MpPreference"));
    }

    [Fact]
    public void The_catalog_includes_the_tweaks_of_the_games_in_the_library()
    {
        new GameLibrary(_env.Context.DataRoot).Add(_exe);

        var ids = TweakCatalog.Create(_env.Context).Where(t => t.Category == TweakCategory.GameProfiles).Select(t => t.Id).ToList();

        Assert.Equal(3, ids.Count);
    }
}

public sealed class HealthCheckTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private sealed class FakeDisplay(DisplayInfo? info) : IDisplayProbe
    {
        public DisplayInfo? Primary() => info;
    }

    private sealed class JsonRunner(string json) : ICommandRunner
    {
        public CommandResult Run(string fileName, string arguments, int timeoutMs = 120_000) =>
            new(0, "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)", "");

        public CommandResult RunPowerShell(string script, int timeoutMs = 300_000) => new(0, json, "");
    }

    private HealthCheck Check(DisplayInfo? display, string json) =>
        new(_env.Context with { Runner = new JsonRunner(json) }, new FakeDisplay(display));

    private static HealthItem Find(IReadOnlyList<HealthItem> items, string start) => items.First(i => i.Title.StartsWith(start));

    [Fact]
    public void A_monitor_below_its_maximum_refresh_rate_needs_attention()
    {
        var items = Check(new DisplayInfo(2560, 1440, 60, 165), "{}").Run();

        var item = Find(items, "Monitor");
        Assert.Equal(HealthLevel.Attention, item.Level);
        Assert.Contains("165 Hz", item.Detail);
        Assert.NotNull(item.Advice);
    }

    [Fact]
    public void A_monitor_at_its_maximum_refresh_rate_is_good()
    {
        Assert.Equal(HealthLevel.Good, Find(Check(new DisplayInfo(1920, 1080, 144, 144), "{}").Run(), "Monitor").Level);
    }

    [Fact]
    public void Memory_running_below_its_rated_speed_points_at_xmp_and_expo()
    {
        var json = """{"ram":[{"speed":6000,"configured":4800,"gb":16},{"speed":6000,"configured":4800,"gb":16}]}""";

        var item = Find(Check(null, json).Run(), "Memory");

        Assert.Equal(HealthLevel.Attention, item.Level);
        Assert.Contains("32 GB", item.Detail);
        Assert.Contains("XMP", item.Advice);
    }

    [Fact]
    public void Memory_at_its_rated_speed_is_good_and_a_single_module_object_is_accepted()
    {
        var json = """{"ram":{"speed":3200,"configured":3200,"gb":16}}""";

        Assert.Equal(HealthLevel.Good, Find(Check(null, json).Run(), "Memory").Level);
    }

    [Fact]
    public void An_old_graphics_driver_needs_attention_and_a_new_one_does_not()
    {
        var old = DateTime.Now.AddMonths(-20).ToString("yyyy-MM-dd");
        var recent = DateTime.Now.AddMonths(-2).ToString("yyyy-MM-dd");
        var json = $$"""{"gpu":[{"name":"Old GPU","date":"{{old}}"},{"name":"New GPU","date":"{{recent}}"}]}""";

        var items = Check(null, json).Run().Where(i => i.Title.StartsWith("Graphics driver:")).ToList();

        Assert.Equal(HealthLevel.Attention, items.Single(i => i.Title.Contains("Old GPU")).Level);
        Assert.Equal(HealthLevel.Good, items.Single(i => i.Title.Contains("New GPU")).Level);
    }

    [Fact]
    public void A_hard_disk_is_reported_with_advice_about_ssds()
    {
        var json = """{"disk":[{"name":"Fast","media":"SSD"},{"name":"Slow","media":"HDD"}]}""";

        var item = Find(Check(null, json).Run(), "Drive type");

        Assert.Contains("Slow (HDD)", item.Detail);
        Assert.Contains("SSD", item.Advice);
    }

    [Fact]
    public void A_balanced_power_plan_is_info_and_unreadable_hardware_never_throws()
    {
        var items = Check(null, "not json at all").Run();

        Assert.Equal(HealthLevel.Info, Find(items, "Power plan").Level);
        Assert.Equal(HealthLevel.Info, Find(items, "Memory").Level);
        Assert.Equal(HealthLevel.Info, Find(items, "Monitor").Level);
    }
}

public sealed class MeasurementTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WindowsOptimizerMeasure", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static SystemSnapshot Snap(string label, double cpu, long mb, int processes, int startup, int? boot, string plan = "Balanced") =>
        new(DateTimeOffset.Now, label, cpu, mb, processes, startup, boot, plan);

    [Fact]
    public void Compare_marks_lower_numbers_as_better_and_ignores_jitter()
    {
        var before = Snap("before", 6.0, 9000, 210, 12, 45);
        var after = Snap("after", 2.5, 7600, 180, 5, 44);

        var rows = Measurement.Compare(before, after).ToDictionary(r => r.Metric);

        Assert.Equal(Verdict.Better, rows["Idle CPU use"].Verdict);
        Assert.Equal("−3.5 points", rows["Idle CPU use"].Change);
        Assert.Equal(Verdict.Better, rows["Memory in use"].Verdict);
        Assert.Equal(Verdict.Better, rows["Running processes"].Verdict);
        Assert.Equal(Verdict.Better, rows["Programs starting with Windows"].Verdict);
        Assert.Equal(Verdict.Same, rows["Last boot time"].Verdict);
    }

    [Fact]
    public void Compare_flags_a_regression_and_tolerates_a_missing_boot_time()
    {
        var rows = Measurement.Compare(Snap("a", 2, 7000, 150, 4, null), Snap("b", 9, 7000, 150, 4, 50)).ToDictionary(r => r.Metric);

        Assert.Equal(Verdict.Worse, rows["Idle CPU use"].Verdict);
        Assert.Equal("not available", rows["Last boot time"].Before);
        Assert.Equal("changed", Measurement.Compare(Snap("a", 1, 1, 1, 1, 1, "Balanced"), Snap("b", 1, 1, 1, 1, 1, "Ultimate")).Last().Change);
    }

    [Fact]
    public void The_store_keeps_snapshots_in_order_and_removes_one()
    {
        var store = new SnapshotStore(_dir);
        var first = Snap("before", 1, 1, 1, 1, 1);
        var second = first with { Label = "after", TakenAt = first.TakenAt.AddMinutes(5) };

        store.Add(first);
        store.Add(second);
        Assert.Equal(new[] { "before", "after" }, store.All().Select(s => s.Label));

        store.Remove(first);
        Assert.Equal(new[] { "after" }, store.All().Select(s => s.Label));
    }

    [Fact]
    public async Task A_real_snapshot_reads_plausible_numbers()
    {
        using var env = new TestEnvironment();

        var snapshot = await Measurement.CaptureAsync(env.Context, "now", TimeSpan.FromMilliseconds(400));

        Assert.InRange(snapshot.IdleCpuPercent, 0, 100);
        Assert.True(snapshot.MemoryUsedMb > 0);
        Assert.True(snapshot.Processes > 10);
    }
}

public sealed class NewTweakTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    public void The_security_trade_offs_are_high_risk_and_warn()
    {
        var all = TweakCatalog.Create(_env.Context);

        foreach (var id in new[] { "memory-integrity-off", "hypervisor-off" })
        {
            var tweak = all.Single(t => t.Id == id);
            Assert.Equal(RiskLevel.High, tweak.Risk);
            Assert.False(string.IsNullOrWhiteSpace(tweak.Warning), id);
        }
    }

    [Fact]
    public void Power_setting_tweaks_read_write_and_restore_through_powercfg()
    {
        var runner = new PowerRunner();
        var ctx = _env.Context with { Runner = runner };
        var tweak = new PowerSettingTweak(ctx, "t", "T", "d", RiskLevel.Low, "SUB_PROCESSOR", "PROCTHROTTLEMIN", 100, null, "Minimum processor state");

        Assert.Equal(TweakStatus.NotApplied, tweak.GetStatus());
        Assert.True(tweak.Apply().Success);
        Assert.Contains("/setacvalueindex 381b4222-f694-41f0-9685-ff5bb260df2e SUB_PROCESSOR PROCTHROTTLEMIN 100", runner.Commands);
        Assert.DoesNotContain(runner.Commands, c => c.StartsWith("/setdcvalueindex"));

        Assert.True(tweak.Undo().Success);
        Assert.Contains("/setacvalueindex 381b4222-f694-41f0-9685-ff5bb260df2e SUB_PROCESSOR PROCTHROTTLEMIN 5", runner.Commands);
        Assert.Contains("/setdcvalueindex 381b4222-f694-41f0-9685-ff5bb260df2e SUB_PROCESSOR PROCTHROTTLEMIN 5", runner.Commands);
    }

    private sealed class PowerRunner : ICommandRunner
    {
        public List<string> Commands { get; } = new();

        public CommandResult Run(string fileName, string arguments, int timeoutMs = 120_000)
        {
            Commands.Add(arguments);
            if (arguments.StartsWith("/getactivescheme")) return new(0, "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)", "");
            if (arguments.StartsWith("/query")) return new(0, "Current AC Power Setting Index: 0x00000005\nCurrent DC Power Setting Index: 0x00000005", "");
            return new(0, "", "");
        }

        public CommandResult RunPowerShell(string script, int timeoutMs = 300_000) => new(0, "", "");
    }
}
