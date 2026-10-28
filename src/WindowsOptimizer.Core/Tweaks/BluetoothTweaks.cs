using System.Text;

namespace WindowsOptimizer.Core;

/// <summary>
/// Turns the Bluetooth radio off every time you sign in. The radio state can only be changed through the
/// WinRT Radio API from inside the user's session, so Apply registers a scheduled task with a logon trigger
/// for the current user. Bluetooth stays usable: you can turn it back on from Quick Settings at any time.
/// </summary>
public static class BluetoothTweaks
{
    public const string TaskName = "WindowsOptimizer - Bluetooth off at sign-in";

    // Device class of Bluetooth adapters; it has numbered subkeys once an adapter driver has been installed.
    private const string BluetoothClass = @"SYSTEM\CurrentControlSet\Control\Class\{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";

    /// <summary>
    /// Waits up to a minute for the adapter to show up (it can appear a few seconds after sign-in),
    /// then switches every Bluetooth radio off. Runs in Windows PowerShell 5.1, which can await WinRT calls.
    /// </summary>
    private const string RadioOffScript = """
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
        function Await($op, [Type]$type) { $t = $asTask.MakeGenericMethod($type).Invoke($null, @($op)); $t.Wait(-1) | Out-Null; $t.Result }
        [Windows.Devices.Radios.Radio, Windows.System.Devices, ContentType = WindowsRuntime] | Out-Null
        Await ([Windows.Devices.Radios.Radio]::RequestAccessAsync()) ([Windows.Devices.Radios.RadioAccessStatus]) | Out-Null
        for ($i = 0; $i -lt 30; $i++) {
            $radios = Await ([Windows.Devices.Radios.Radio]::GetRadiosAsync()) ([System.Collections.Generic.IReadOnlyList[Windows.Devices.Radios.Radio]])
            $bluetooth = @($radios | Where-Object { $_.Kind -eq 'Bluetooth' })
            if ($bluetooth.Count -gt 0) {
                foreach ($radio in $bluetooth) { Await ($radio.SetStateAsync('Off')) ([Windows.Devices.Radios.RadioAccessStatus]) | Out-Null }
                break
            }
            Start-Sleep -Seconds 2
        }
        """;

    public static ITweak OffAtSignIn(TweakContext c) => new DelegateTweak(
        c, "bluetooth-off-at-sign-in", "Start with Bluetooth off",
        "Turns the Bluetooth radio off every time you sign in, through a scheduled task for your user. Bluetooth isn't disabled: turn it on from Quick Settings whenever you need it, and it goes back off on the next restart.",
        TweakCategory.Startup, RiskLevel.Low,
        status: () => TaskExists(c) switch
        {
            true => TweakStatus.Applied,
            false => TweakStatus.NotApplied,
            null => TweakStatus.Unknown,
        },
        apply: () =>
        {
            var result = c.Runner.RunPowerShell(RegisterScript());
            return result.Success
                ? TweakResult.Ok("Bluetooth will be turned off every time you sign in.")
                : TweakResult.Fail("Could not create the scheduled task: " + result.Text);
        },
        undo: () =>
        {
            var result = c.Runner.RunPowerShell($"Unregister-ScheduledTask -TaskName {Quote(TaskName)} -Confirm:$false");
            return result.Success
                ? TweakResult.Ok("Bluetooth will keep the state you leave it in again.")
                : TweakResult.Fail("Could not remove the scheduled task: " + result.Text);
        },
        available: () => c.Registry.GetSubKeyNames(RegHive.LocalMachine, BluetoothClass).Any(k => k.All(char.IsAsciiDigit)),
        preview: () => new[]
        {
            new PreviewLine("Bluetooth at sign-in", TaskExists(c) == true ? "off" : "as you left it", "off", $"Scheduled task \"{TaskName}\""),
        })
    {
        UnavailableReason = "This PC has no Bluetooth adapter.",
    };

    /// <summary>True when the task exists, null when PowerShell couldn't answer.</summary>
    private static bool? TaskExists(TweakContext c)
    {
        var result = c.Runner.RunPowerShell(
            $"if (Get-ScheduledTask -TaskName {Quote(TaskName)} -ErrorAction SilentlyContinue) {{ 'yes' }} else {{ 'no' }}");
        return result.Output.Trim() switch
        {
            "yes" => true,
            "no" => false,
            _ => null,
        };
    }

    private static string RegisterScript()
    {
        // conhost --headless runs PowerShell without flashing a console window at sign-in.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(RadioOffScript));
        var arguments = $"--headless powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}";
        return $$"""
            $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
            $action = New-ScheduledTaskAction -Execute 'conhost.exe' -Argument {{Quote(arguments)}}
            $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
            $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
            $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
            Register-ScheduledTask -TaskName {{Quote(TaskName)}} -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
            """;
    }

    private static string Quote(string text) => "'" + text.Replace("'", "''") + "'";
}
