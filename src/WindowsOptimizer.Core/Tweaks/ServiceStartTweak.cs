namespace WindowsOptimizer.Core;

/// <summary>
/// Sets the start type of Windows services (2 = automatic, 3 = manual, 4 = disabled).
/// Services this Windows doesn't have are skipped when the tweak is created, like the interfaces in <see cref="NagleTweak"/>.
/// </summary>
public sealed class ServiceStartTweak : RegistryTweak
{
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

    public ServiceStartTweak(
        TweakContext ctx, string id, string name, string description,
        TweakCategory category, RiskLevel risk, int startType, params string[] services)
        : base(ctx, id, name, description, category, risk, EditsFor(ctx, startType, services))
    {
    }

    /// <summary>Hidden on machines that have none of these services (nothing to change).</summary>
    public override bool IsAvailable() => Edits.Count > 0;

    private static RegistryEdit[] EditsFor(TweakContext ctx, int startType, string[] services) =>
        services
            .Where(s => ctx.Registry.KeyExists(RegHive.LocalMachine, $@"{ServicesKey}\{s}"))
            .Select(s => Reg.LmDword($@"{ServicesKey}\{s}", "Start", startType))
            .ToArray();
}
