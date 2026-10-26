using System.Diagnostics;
using System.Text;

namespace WindowsOptimizer.Core;

public readonly record struct CommandResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;

    public string Text => string.IsNullOrWhiteSpace(Error) ? Output.Trim() : (Output + Environment.NewLine + Error).Trim();
}

public interface ICommandRunner
{
    CommandResult Run(string fileName, string arguments, int timeoutMs = 120_000);

    /// <summary>Runs a script in Windows PowerShell 5.1 (stops on the first error, UTF-8 output).</summary>
    CommandResult RunPowerShell(string script, int timeoutMs = 300_000);
}

public sealed class CommandRunner : ICommandRunner
{
    public CommandResult Run(string fileName, string arguments, int timeoutMs = 120_000) =>
        Execute(fileName, arguments, null, timeoutMs);

    public CommandResult RunPowerShell(string script, int timeoutMs = 300_000)
    {
        var full = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $ErrorActionPreference = 'Stop'; " + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return Execute(
            "powershell.exe",
            $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            Encoding.UTF8,
            timeoutMs);
    }

    private static CommandResult Execute(string fileName, string arguments, Encoding? encoding, int timeoutMs)
    {
        var info = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (encoding is not null)
        {
            info.StandardOutputEncoding = encoding;
            info.StandardErrorEncoding = encoding;
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");

        // Read both streams while waiting so a chatty process can't block on a full pipe.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new CommandResult(-1, "", $"Timed out running {fileName}.");
        }

        process.WaitForExit();
        return new CommandResult(process.ExitCode, stdout.Result, stderr.Result);
    }
}
