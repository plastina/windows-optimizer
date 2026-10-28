namespace WindowsOptimizer.Core;

/// <summary>Network tweaks that run netsh instead of writing registry values.</summary>
public static class NetworkTweaks
{
    public static ITweak TeredoOff(TweakContext c) => new DelegateTweak(
        c, "teredo-off", "Turn off the Teredo tunnel",
        "Turns off the Teredo interface, which gave IPv6 over IPv4 to older networks. Modern networks don't need it.",
        TweakCategory.Network, RiskLevel.Low,
        status: () =>
        {
            var result = c.Runner.Run("netsh", "interface teredo show state");
            if (!result.Success) return TweakStatus.Unknown;

            // Windows prints the state in the display language, so accept the English and Portuguese words.
            var off = result.Output.Contains("disable", StringComparison.OrdinalIgnoreCase)
                || result.Output.Contains("desabilit", StringComparison.OrdinalIgnoreCase);
            return off ? TweakStatus.Applied : TweakStatus.NotApplied;
        },
        apply: () => Netsh(c, "interface teredo set state disabled", "Teredo turned off."),
        undo: () => Netsh(c, "interface teredo set state default", "Teredo set back to the default."));

    private static TweakResult Netsh(TweakContext c, string arguments, string success)
    {
        var result = c.Runner.Run("netsh", arguments);
        return result.Success
            ? TweakResult.Ok(success)
            : TweakResult.Fail(result.Text.Length > 0 ? result.Text : $"netsh exited with code {result.ExitCode}.");
    }
}
