using WindowsOptimizer.Core;

namespace WindowsOptimizer.Core.Tests;

public sealed class SelfTestTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private RegistryTweak Registry(string id, params RegistryEdit[] edits) =>
        new(_env.Context, id, id, "d", TweakCategory.Gaming, RiskLevel.Low, edits);

    [Fact]
    public void A_working_toggle_passes()
    {
        var results = SelfTest.Run(new[] { Registry("ok", Reg.CuDword(@"Software\T", "A", 1)) });

        Assert.Equal(SelfTestOutcome.Pass, Assert.Single(results).Outcome);
    }

    [Fact]
    public void A_tweak_whose_undo_does_not_restore_the_state_fails()
    {
        var broken = new DelegateTweak(
            _env.Context, "broken", "broken", "d", TweakCategory.Gaming, RiskLevel.Low,
            status: () => TweakStatus.Applied,
            apply: () => TweakResult.Ok(),
            undo: () => TweakResult.Ok());

        // Status is Applied before, so a "restored" state of Applied passes; flip it to prove the check can fail.
        var applied = true;
        var flipping = new DelegateTweak(
            _env.Context, "flipping", "flipping", "d", TweakCategory.Gaming, RiskLevel.Low,
            status: () => applied ? TweakStatus.Applied : TweakStatus.NotApplied,
            apply: () => { applied = true; return TweakResult.Ok(); },
            undo: () => TweakResult.Ok()); // never turns `applied` off, but starts NotApplied below

        applied = false;
        var results = SelfTest.Run(new ITweak[] { broken, flipping });

        Assert.Equal(SelfTestOutcome.Pass, results[0].Outcome);
        Assert.Equal(SelfTestOutcome.Fail, results[1].Outcome);
    }

    [Fact]
    public void A_failing_apply_is_reported_as_a_failure()
    {
        var failing = new DelegateTweak(
            _env.Context, "failing", "failing", "d", TweakCategory.Gaming, RiskLevel.Low,
            status: () => TweakStatus.NotApplied,
            apply: () => TweakResult.Fail("access denied"));

        var result = Assert.Single(SelfTest.Run(new ITweak[] { failing }));

        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
        Assert.Contains("access denied", result.Detail);
    }

    [Fact]
    public void Skipped_and_unavailable_tweaks_are_not_run()
    {
        var ran = false;
        var tweak = new DelegateTweak(
            _env.Context, "skipme", "skipme", "d", TweakCategory.Gaming, RiskLevel.Low,
            status: () => TweakStatus.NotApplied,
            apply: () => { ran = true; return TweakResult.Ok(); });

        var results = SelfTest.Run(new ITweak[] { tweak }, new HashSet<string> { "skipme" });

        Assert.False(ran);
        Assert.Equal(SelfTestOutcome.Skip, Assert.Single(results).Outcome);
    }
}
