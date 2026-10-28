using System.Text.RegularExpressions;

namespace WindowsOptimizer.Core;

/// <summary>Tweaks that trade protection or features for speed. Each one is High risk and warns before it runs.</summary>
public static class SecurityTradeoffTweaks
{
    private static readonly Regex LaunchType = new(@"hypervisorlaunchtype\s+(\w+)", RegexOptions.IgnoreCase);

    public static ITweak HypervisorOff(TweakContext c)
    {
        string? Current() =>
            LaunchType.Match(c.Runner.Run("bcdedit.exe", "/enum {current}").Output) is { Success: true } m ? m.Groups[1].Value : null;

        return new DelegateTweak(
            c, "hypervisor-off", "Turn off the Windows hypervisor",
            "Stops Windows from starting its hypervisor at boot. Without it, games run on the real hardware instead of on top of a thin virtualization layer, which can lower CPU overhead and input delay on some PCs.",
            TweakCategory.System, RiskLevel.High,
            status: () => Current() is { } now && now.Equals("Off", StringComparison.OrdinalIgnoreCase)
                ? TweakStatus.Applied
                : TweakStatus.NotApplied,
            apply: () => FromCommand(c.Runner.Run("bcdedit.exe", "/set hypervisorlaunchtype off"), "Hypervisor turned off. Restart the PC to finish."),
            undo: () => FromCommand(c.Runner.Run("bcdedit.exe", "/set hypervisorlaunchtype auto"), "Hypervisor turned back on. Restart the PC to finish."),
            preview: () => new[] { new PreviewLine("Hypervisor launch type", Current() ?? "(default)", "Off", "Boot configuration (bcdedit)") },
            fingerprint: () => Current() ?? "(default)")
        {
            NeedsRestart = true,
            NeedsAdmin = true,
            Warning = "WSL 2, Docker, Hyper-V, Windows Sandbox, Android emulators that use Windows virtualization and Memory integrity all need the hypervisor and stop working while it is off. Undo turns it back on.",
        };
    }

    private static TweakResult FromCommand(CommandResult result, string success) =>
        result.Success
            ? TweakResult.Ok(success)
            : TweakResult.Fail(result.Text.Length > 0 ? result.Text : $"The command exited with code {result.ExitCode} and no message.");
}
