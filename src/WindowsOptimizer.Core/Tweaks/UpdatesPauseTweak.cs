namespace WindowsOptimizer.Core;

/// <summary>
/// Turns off automatic Windows Update by policy and sets the update services to manual start.
/// The service start modes are saved so Undo puts each one back exactly as it was.
/// </summary>
public sealed class UpdatesPauseTweak : RegistryTweak
{
    private const string AutoUpdatePolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";

    private static readonly string[] Services = { "wuauserv", "UsoSvc", "BITS", "DoSvc" };

    public UpdatesPauseTweak(TweakContext ctx)
        : base(ctx, "updates-pause-auto", "Pausar atualizações automáticas",
            "Desliga o download e a instalação automáticos do Windows Update e deixa os serviços de atualização em início manual. Você continua podendo atualizar quando quiser.",
            TweakCategory.Updates, RiskLevel.High,
            Reg.LmDword(AutoUpdatePolicy, "NoAutoUpdate", 1),
            Reg.LmDword(AutoUpdatePolicy, "AUOptions", 2))
    {
    }

    protected override void OnBeforeApply(TweakSnapshot snapshot)
    {
        foreach (var service in Services)
            snapshot.Data["service:" + service] = ReadStartMode(service) ?? "";
    }

    protected override string? OnApplied(TweakSnapshot snapshot)
    {
        var failed = Services.Where(s => !Ctx.Runner.Run("sc.exe", $"config {s} start= demand").Success).ToList();
        return failed.Count == 0
            ? "Atualizações automáticas pausadas. Reinicie o PC para concluir."
            : "Atualizações pausadas, mas estes serviços não puderam ser ajustados: " + string.Join(", ", failed);
    }

    protected override string? OnUndoing(TweakSnapshot snapshot)
    {
        foreach (var (key, mode) in snapshot.Data.Where(e => e.Key.StartsWith("service:", StringComparison.Ordinal)))
        {
            if (mode.Length > 0) Ctx.Runner.Run("sc.exe", $"config {key["service:".Length..]} start= {mode}");
        }
        return "Atualizações automáticas restauradas. Reinicie o PC para concluir.";
    }

    /// <summary>Reads the start mode in the vocabulary of <c>sc config</c>.</summary>
    private string? ReadStartMode(string service)
    {
        var key = $@"SYSTEM\CurrentControlSet\Services\{service}";
        var start = Ctx.Registry.GetValue(RegHive.LocalMachine, key, "Start");
        if (start is not { Kind: RegKind.DWord }) return null;

        var delayed = Ctx.Registry.GetValue(RegHive.LocalMachine, key, "DelayedAutostart") is { Kind: RegKind.DWord, Data: 1 };
        return (int)start.Value.Data switch
        {
            2 => delayed ? "delayed-auto" : "auto",
            3 => "demand",
            4 => "disabled",
            _ => null,
        };
    }
}
