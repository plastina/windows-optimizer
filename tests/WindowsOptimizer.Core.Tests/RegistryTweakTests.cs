using WindowsOptimizer.Core;

namespace WindowsOptimizer.Core.Tests;

public sealed class RegistryTweakTests : IDisposable
{
    private const string Key = @"Software\Test\Games";
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private RegistryTweak Create(params RegistryEdit[] edits) =>
        new(_env.Context, "test-tweak", "Teste", "Descrição", TweakCategory.Gaming, RiskLevel.Low, edits);

    [Fact]
    public void Apply_writes_every_value_and_reports_applied()
    {
        var tweak = Create(Reg.CuDword(Key, "A", 1), Reg.CuString(Key, "B", "x"));

        Assert.Equal(TweakStatus.NotApplied, tweak.GetStatus());
        var result = tweak.Apply();

        Assert.True(result.Success, result.Message);
        Assert.Equal(TweakStatus.Applied, tweak.GetStatus());
        Assert.Equal(RegValue.DWord(1), _env.Registry.GetValue(RegHive.CurrentUser, Key, "A"));
        Assert.Equal(RegValue.Str("x"), _env.Registry.GetValue(RegHive.CurrentUser, Key, "B"));
    }

    [Fact]
    public void Status_is_partial_when_only_some_values_match()
    {
        var tweak = Create(Reg.CuDword(Key, "A", 1), Reg.CuDword(Key, "B", 1));
        _env.Registry.SetValue(RegHive.CurrentUser, Key, "A", RegValue.DWord(1));

        Assert.Equal(TweakStatus.Partial, tweak.GetStatus());
    }

    [Fact]
    public void Undo_restores_the_previous_value()
    {
        _env.Registry.SetValue(RegHive.CurrentUser, Key, "A", RegValue.DWord(7));
        var tweak = Create(Reg.CuDword(Key, "A", 1));

        tweak.Apply();
        var result = tweak.Undo();

        Assert.True(result.Success, result.Message);
        Assert.Equal(RegValue.DWord(7), _env.Registry.GetValue(RegHive.CurrentUser, Key, "A"));
        Assert.False(tweak.CanUndo());
    }

    [Fact]
    public void Undo_deletes_values_and_keys_that_did_not_exist()
    {
        var tweak = Create(Reg.LmDword(@"Software\Policies\Test", "Flag", 1));

        tweak.Apply();
        Assert.True(_env.Registry.KeyExists(RegHive.LocalMachine, @"Software\Policies\Test"));

        tweak.Undo();

        Assert.Null(_env.Registry.GetValue(RegHive.LocalMachine, @"Software\Policies\Test", "Flag"));
        Assert.False(_env.Registry.KeyExists(RegHive.LocalMachine, @"Software\Policies\Test"));
    }

    [Fact]
    public void Undo_keeps_a_key_that_still_holds_other_values()
    {
        _env.Registry.SetValue(RegHive.CurrentUser, Key, "Other", RegValue.DWord(5));
        var tweak = Create(Reg.CuDword(Key, "A", 1));

        tweak.Apply();
        tweak.Undo();

        Assert.Equal(RegValue.DWord(5), _env.Registry.GetValue(RegHive.CurrentUser, Key, "Other"));
        Assert.Null(_env.Registry.GetValue(RegHive.CurrentUser, Key, "A"));
    }

    [Fact]
    public void Applying_twice_keeps_the_original_backup()
    {
        _env.Registry.SetValue(RegHive.CurrentUser, Key, "A", RegValue.DWord(7));
        var tweak = Create(Reg.CuDword(Key, "A", 1));

        tweak.Apply();
        tweak.Apply();
        tweak.Undo();

        Assert.Equal(RegValue.DWord(7), _env.Registry.GetValue(RegHive.CurrentUser, Key, "A"));
    }

    [Fact]
    public void Undo_without_a_backup_fails_and_changes_nothing()
    {
        _env.Registry.SetValue(RegHive.CurrentUser, Key, "A", RegValue.DWord(1));
        var tweak = Create(Reg.CuDword(Key, "A", 1));

        var result = tweak.Undo();

        Assert.False(result.Success);
        Assert.Equal(RegValue.DWord(1), _env.Registry.GetValue(RegHive.CurrentUser, Key, "A"));
    }

    [Fact]
    public void Edits_marked_only_if_key_exists_are_skipped_when_the_key_is_missing()
    {
        var tweak = Create(Reg.LmDword(@"Software\Missing", "Flag", 0, onlyIfKeyExists: true));

        Assert.Equal(TweakStatus.Unknown, tweak.GetStatus());
        tweak.Apply();

        Assert.False(_env.Registry.KeyExists(RegHive.LocalMachine, @"Software\Missing"));
    }

    [Fact]
    public void Preview_shows_current_and_new_values()
    {
        _env.Registry.SetValue(RegHive.CurrentUser, Key, "A", RegValue.DWord(7));
        var tweak = Create(Reg.CuDword(Key, "A", 1), Reg.CuString(Key, "B", "x"));

        var lines = tweak.Preview();

        Assert.Equal(new PreviewLine("A", "7", "1", @"HKCU\" + Key), lines[0]);
        Assert.Equal(new PreviewLine("B", "(ausente)", "\"x\"", @"HKCU\" + Key), lines[1]);
    }
}
