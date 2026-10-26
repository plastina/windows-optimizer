using WindowsOptimizer.Core;

namespace WindowsOptimizer.Core.Tests;

public sealed class StartupTests : IDisposable
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private StartupEntryTweak Entry(string name = "Discord") =>
        new(_env.Context, RegHive.CurrentUser, Approved, name, name, @"C:\Apps\" + name + ".exe", "Current user");

    [Fact]
    public void Discovery_lists_run_key_entries_with_unique_kebab_case_ids()
    {
        _env.Registry.SetValue(RegHive.CurrentUser, Run, "Discord", RegValue.Str(@"C:\Apps\Discord.exe"));
        _env.Registry.SetValue(RegHive.LocalMachine, Run, "Discord", RegValue.Str(@"C:\Apps\Discord.exe"));
        _env.Registry.SetValue(RegHive.CurrentUser, Run, "Steam Client (x86)", RegValue.Str(@"C:\Steam.exe"));

        // Skip the real Startup folders: this machine's contents must not leak into the test.
        var found = StartupDiscovery.Discover(_env.Context, includeFolders: false);

        Assert.Equal(3, found.Count(t => t.Category == TweakCategory.Startup));
        Assert.Equal(found.Count, found.Select(t => t.Id).Distinct().Count());
        Assert.All(found, t => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", t.Id));
    }

    [Fact]
    public void Apply_disables_the_entry_and_undo_removes_the_flag_it_added()
    {
        var tweak = Entry();
        Assert.Equal(TweakStatus.NotApplied, tweak.GetStatus());

        Assert.True(tweak.Apply().Success);
        Assert.Equal(TweakStatus.Applied, tweak.GetStatus());
        Assert.Equal(12, _env.Registry.GetBinary(RegHive.CurrentUser, Approved, "Discord")!.Length);

        Assert.True(tweak.Undo().Success);
        Assert.Equal(TweakStatus.NotApplied, tweak.GetStatus());
        Assert.Null(_env.Registry.GetBinary(RegHive.CurrentUser, Approved, "Discord"));
    }

    [Fact]
    public void Undo_restores_the_exact_previous_flag()
    {
        var enabled = new byte[] { 0x02, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        _env.Registry.SetBinary(RegHive.CurrentUser, Approved, "Discord", enabled);
        var tweak = Entry();

        tweak.Apply();
        tweak.Undo();

        Assert.Equal(enabled, _env.Registry.GetBinary(RegHive.CurrentUser, Approved, "Discord"));
    }

    [Fact]
    public void An_entry_Task_Manager_already_disabled_reads_as_applied()
    {
        _env.Registry.SetBinary(RegHive.CurrentUser, Approved, "Discord", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        Assert.Equal(TweakStatus.Applied, Entry().GetStatus());
    }
}

public sealed class CleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WindowsOptimizerCleanup", Guid.NewGuid().ToString("N"));

    public CleanupTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Make(string relative, int bytes, TimeSpan age)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTime(path, DateTime.Now - age);
        return path;
    }

    [Fact]
    public void Measure_counts_only_files_older_than_the_limit()
    {
        Make("old.tmp", 1000, TimeSpan.FromDays(3));
        Make(@"sub\old2.tmp", 500, TimeSpan.FromDays(2));
        Make("new.tmp", 9999, TimeSpan.FromMinutes(5));

        var (files, bytes) = CleanupTweaks.Measure(_root, TimeSpan.FromHours(24));

        Assert.Equal(2, files);
        Assert.Equal(1500, bytes);
    }

    [Fact]
    public void Delete_removes_old_files_and_emptied_folders_but_keeps_recent_ones()
    {
        var old = Make(@"a\b\old.tmp", 100, TimeSpan.FromDays(5));
        var recent = Make("new.tmp", 100, TimeSpan.FromMinutes(1));

        var freed = CleanupTweaks.Delete(_root, TimeSpan.FromHours(24));

        Assert.Equal(100, freed);
        Assert.False(File.Exists(old));
        Assert.False(Directory.Exists(Path.Combine(_root, "a")));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void A_missing_folder_is_not_an_error()
    {
        var missing = Path.Combine(_root, "nope");

        Assert.Equal((0, 0L), CleanupTweaks.Measure(missing, TimeSpan.Zero));
        Assert.Equal(0, CleanupTweaks.Delete(missing, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void FormatBytes_uses_binary_units(long bytes, string expected) =>
        Assert.Equal(expected, CleanupTweaks.FormatBytes(bytes));
}

public sealed class UpdateCheckTests
{
    [Theory]
    [InlineData("0.1.0", "v0.2.0", true)]
    [InlineData("0.1.0", "v0.1.1", true)]
    [InlineData("0.1.0", "v0.1.0", false)]
    [InlineData("0.2.0", "v0.1.9", false)]
    [InlineData("0.1.0+abc123", "v0.1.0", false)]
    [InlineData("0.2.0-rc1", "v0.2.0", false)]
    [InlineData("1.0", "v1.0.1", true)]
    [InlineData("0.1.0", "not a version", false)]
    [InlineData("", "v1.0.0", false)]
    public void IsNewer_compares_numeric_versions_and_ignores_suffixes(string current, string latest, bool expected) =>
        Assert.Equal(expected, UpdateCheck.IsNewer(current, latest));
}

public sealed class NagleAndFingerprintTests : IDisposable
{
    private const string Interfaces = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    public void Nagle_targets_only_interfaces_that_have_an_address()
    {
        _env.Registry.SetValue(RegHive.LocalMachine, Interfaces + @"\{AAA}", "DhcpIPAddress", RegValue.Str("10.0.0.5"));
        _env.Registry.AddKey(RegHive.LocalMachine, Interfaces + @"\{BBB}");

        var tweak = new NagleTweak(_env.Context);

        Assert.True(tweak.IsAvailable());
        Assert.Equal(2, tweak.Preview().Count);
        // The fake registry lower-cases key names, like Windows compares them.
        Assert.All(tweak.Preview(), line => Assert.Contains("{aaa}", line.Detail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Nagle_is_hidden_when_no_interface_has_an_address()
    {
        Assert.False(new NagleTweak(_env.Context).IsAvailable());
    }

    [Fact]
    public void The_self_test_flags_a_tweak_that_reports_success_without_changing_the_real_state()
    {
        var applied = false;
        var liar = new DelegateTweak(
            _env.Context, "liar", "liar", "d", TweakCategory.Gaming, RiskLevel.Low,
            status: () => applied ? TweakStatus.Applied : TweakStatus.NotApplied,
            apply: () => { applied = true; return TweakResult.Ok(); },
            undo: () => { applied = false; return TweakResult.Ok(); },
            fingerprint: () => "constant");

        var result = Assert.Single(SelfTest.Run(new ITweak[] { liar }));

        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
        Assert.Contains("did not change", result.Detail);
    }

    [Fact]
    public void The_self_test_flags_an_undo_that_leaves_the_real_state_different()
    {
        var real = "before";
        var applied = false;
        var leaky = new DelegateTweak(
            _env.Context, "leaky", "leaky", "d", TweakCategory.Gaming, RiskLevel.Low,
            status: () => applied ? TweakStatus.Applied : TweakStatus.NotApplied,
            apply: () => { applied = true; real = "changed"; return TweakResult.Ok(); },
            undo: () => { applied = false; real = "half-restored"; return TweakResult.Ok(); },
            fingerprint: () => real);

        var result = Assert.Single(SelfTest.Run(new ITweak[] { leaky }));

        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
        Assert.Contains("real system state differs", result.Detail);
    }

    [Fact]
    public void The_self_test_passes_when_the_real_state_changes_and_comes_back()
    {
        var real = "before";
        var applied = false;
        var good = new DelegateTweak(
            _env.Context, "good", "good", "d", TweakCategory.Gaming, RiskLevel.Low,
            status: () => applied ? TweakStatus.Applied : TweakStatus.NotApplied,
            apply: () => { applied = true; real = "changed"; return TweakResult.Ok(); },
            undo: () => { applied = false; real = "before"; return TweakResult.Ok(); },
            fingerprint: () => real);

        var result = Assert.Single(SelfTest.Run(new ITweak[] { good }));

        Assert.Equal(SelfTestOutcome.Pass, result.Outcome);
        Assert.Contains("Real state verified", result.Detail);
    }
}
