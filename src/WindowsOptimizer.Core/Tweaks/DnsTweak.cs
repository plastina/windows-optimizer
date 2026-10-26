using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace WindowsOptimizer.Core;

/// <summary>Points every physical network adapter at Google DNS. Undo restores static servers or DHCP per adapter.</summary>
public sealed class DnsTweak : TweakBase
{
    private static readonly string[] Google4 = { "8.8.8.8", "8.8.4.4" };
    private static readonly string[] Google6 = { "2001:4860:4860::8888", "2001:4860:4860::8844" };

    private static readonly Regex VirtualAdapter = new(
        "Virtual|VMware|Hyper-V|VirtualBox|Loopback|TAP-|WireGuard|Bluetooth|Tunnel|Npcap|vEthernet",
        RegexOptions.IgnoreCase);

    public DnsTweak(TweakContext ctx)
        : base(ctx, "dns-google", "DNS do Google",
            "Usa 8.8.8.8 e 8.8.4.4 (e os equivalentes IPv6) nas placas de rede ativas. Pode melhorar a resolução de nomes, mas não reduz o ping dos jogos.",
            TweakCategory.Network, RiskLevel.Low)
    {
    }

    public override TweakStatus GetStatus()
    {
        var adapters = ActiveAdapters();
        if (adapters.Count == 0) return TweakStatus.Unknown;

        var matching = adapters.Count(a => Split(ReadServers(a.Id, v6: false)).SequenceEqual(Google4));
        return matching == adapters.Count ? TweakStatus.Applied
            : matching == 0 ? TweakStatus.NotApplied
            : TweakStatus.Partial;
    }

    public override IReadOnlyList<PreviewLine> Preview() =>
        ActiveAdapters()
            .Select(a =>
            {
                var current = ReadServers(a.Id, v6: false);
                return new PreviewLine(a.Name, current.Length == 0 ? "automático (DHCP)" : current, string.Join(", ", Google4));
            })
            .ToList();

    public override TweakResult Apply()
    {
        var adapters = ActiveAdapters();
        if (adapters.Count == 0) return TweakResult.Fail("Nenhuma placa de rede ativa foi encontrada.");

        var snapshot = Ctx.State.Load(Id) ?? new TweakSnapshot { TweakId = Id };
        foreach (var adapter in adapters)
        {
            // Never overwrite the first backup of an adapter.
            snapshot.Data.TryAdd("v4:" + adapter.Id, ReadServers(adapter.Id, v6: false));
            snapshot.Data.TryAdd("v6:" + adapter.Id, ReadServers(adapter.Id, v6: true));
        }
        Ctx.State.Save(snapshot);

        var errors = new List<string>();
        foreach (var adapter in adapters)
        {
            Collect(SetServers(adapter.Name, v6: false, Google4), adapter.Name, errors);
            if (adapter.Supports(NetworkInterfaceComponent.IPv6))
                Collect(SetServers(adapter.Name, v6: true, Google6), adapter.Name, errors);
        }

        Ctx.Runner.Run("ipconfig", "/flushdns");
        return errors.Count == 0
            ? TweakResult.Ok($"DNS do Google aplicado em {adapters.Count} placa(s).")
            : TweakResult.Fail(string.Join(Environment.NewLine, errors));
    }

    public override TweakResult Undo()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null) return TweakResult.Fail("Não há backup deste ajuste; o app não o aplicou.");

        var all = NetworkInterface.GetAllNetworkInterfaces();
        var errors = new List<string>();
        foreach (var key in snapshot.Data.Keys.Where(k => k.StartsWith("v4:", StringComparison.Ordinal)).ToList())
        {
            var id = key[3..];
            var adapter = all.FirstOrDefault(a => a.Id == id);
            if (adapter is null) continue;

            Collect(SetServers(adapter.Name, v6: false, Split(snapshot.Data[key])), adapter.Name, errors);
            if (adapter.Supports(NetworkInterfaceComponent.IPv6) && snapshot.Data.TryGetValue("v6:" + id, out var v6))
                Collect(SetServers(adapter.Name, v6: true, Split(v6)), adapter.Name, errors);
        }

        Ctx.Runner.Run("ipconfig", "/flushdns");
        if (errors.Count > 0) return TweakResult.Fail(string.Join(Environment.NewLine, errors));

        Ctx.State.Delete(Id);
        return TweakResult.Ok("DNS anterior restaurado.");
    }

    private List<NetworkInterface> ActiveAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && (n.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                            or NetworkInterfaceType.GigabitEthernet
                            or NetworkInterfaceType.Wireless80211)
                        && !VirtualAdapter.IsMatch(n.Description)
                        && !VirtualAdapter.IsMatch(n.Name))
            .ToList();

    /// <summary>Statically configured servers; empty means the adapter gets DNS from DHCP.</summary>
    private string ReadServers(string adapterId, bool v6)
    {
        var stack = v6 ? "Tcpip6" : "Tcpip";
        var value = Ctx.Registry.GetValue(
            RegHive.LocalMachine,
            $@"SYSTEM\CurrentControlSet\Services\{stack}\Parameters\Interfaces\{adapterId}",
            "NameServer");
        return value is { Kind: RegKind.String } text ? (string)text.Data : "";
    }

    private static string[] Split(string servers) =>
        servers.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);

    private CommandResult SetServers(string adapterName, bool v6, string[] servers)
    {
        var family = v6 ? "ipv6" : "ip";
        if (servers.Length == 0)
            return Ctx.Runner.Run("netsh", $"interface {family} set dnsservers name=\"{adapterName}\" source=dhcp");

        var result = Ctx.Runner.Run("netsh",
            $"interface {family} set dnsservers name=\"{adapterName}\" source=static address={servers[0]} register=primary validate=no");
        for (var i = 1; i < servers.Length && result.Success; i++)
        {
            result = Ctx.Runner.Run("netsh",
                $"interface {family} add dnsservers name=\"{adapterName}\" address={servers[i]} index={i + 1} validate=no");
        }
        return result;
    }

    private static void Collect(CommandResult result, string adapterName, List<string> errors)
    {
        if (!result.Success) errors.Add($"{adapterName}: {result.Text}");
    }
}
