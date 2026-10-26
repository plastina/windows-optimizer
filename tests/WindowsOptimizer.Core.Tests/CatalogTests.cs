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
    public void Every_registry_toggle_round_trips_on_an_empty_registry()
    {
        foreach (var tweak in TweakCatalog.Create(_env.Context).OfType<RegistryTweak>().Where(t => t is not AutoLoginTweak))
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
