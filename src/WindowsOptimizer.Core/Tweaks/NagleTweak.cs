namespace WindowsOptimizer.Core;

/// <summary>
/// Turns off Nagle's algorithm (small packets go out immediately) on every network interface that has an IP address.
/// The list of interfaces is read once, when the tweak is created.
/// </summary>
public sealed class NagleTweak : RegistryTweak
{
    private const string Interfaces = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    public NagleTweak(TweakContext ctx)
        : base(ctx, "nagle-off", "Turn off Nagle's algorithm",
            "Sends small network packets right away instead of batching them. It can lower latency in some online games and does nothing in others.",
            TweakCategory.Gaming, RiskLevel.Low,
            EditsFor(ctx))
    {
    }

    /// <summary>Hidden on machines where no interface has an address (nothing to change).</summary>
    public override bool IsAvailable() => Edits.Count > 0;

    private static RegistryEdit[] EditsFor(TweakContext ctx)
    {
        var edits = new List<RegistryEdit>();
        foreach (var id in ctx.Registry.GetSubKeyNames(RegHive.LocalMachine, Interfaces))
        {
            var key = $@"{Interfaces}\{id}";
            var names = ctx.Registry.GetValueNames(RegHive.LocalMachine, key);

            // DhcpIPAddress/IPAddress only exist on interfaces that are (or were) configured.
            if (!names.Contains("DhcpIPAddress", StringComparer.OrdinalIgnoreCase)
                && !names.Contains("IPAddress", StringComparer.OrdinalIgnoreCase))
                continue;

            edits.Add(Reg.LmDword(key, "TcpAckFrequency", 1));
            edits.Add(Reg.LmDword(key, "TCPNoDelay", 1));
        }
        return edits.ToArray();
    }
}
