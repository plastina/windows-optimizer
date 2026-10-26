using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace WindowsOptimizer.Core;

/// <summary>Actions and file-based tweaks that don't fit the registry model.</summary>
public static class MaintenanceTweaks
{
    private const string RacingExe = @"C:\Program Files (x86)\BIOSTAR\RACINGGTEVO\RCGTII.exe";
    private const long LargeFileBytes = 500L * 1024 * 1024;

    public static ITweak HibernationOff(TweakContext c) => new DelegateTweak(
        c, "hibernation-off", "Turn off hibernation and Fast Startup",
        "Removes the hibernation file (frees several GB) and turns off Fast Startup. The PC shuts down completely.",
        TweakCategory.Power, RiskLevel.Low,
        status: () => c.Registry.GetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled")
            is { Kind: RegKind.DWord, Data: 0 } ? TweakStatus.Applied : TweakStatus.NotApplied,
        apply: () => FromCommand(c.Runner.Run("powercfg", "/hibernate off"), "Hibernation turned off."),
        undo: () => FromCommand(c.Runner.Run("powercfg", "/hibernate on"), "Hibernation turned back on."),
        available: PowerCapabilities.SupportsHibernate,
        preview: () => new[] { new PreviewLine("Hibernation and Fast Startup", "on", "off") },
        fingerprint: () => "hiberfil.sys exists: " +
            File.Exists(Path.Combine((Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + @"\", "hiberfil.sys")));

    public static ITweak RestorePoint(TweakContext c) => new DelegateTweak(
        c, "restore-point", "Create a restore point",
        "Saves a system restore point so you can roll back with Windows itself. Windows allows one restore point every 24 hours.",
        TweakCategory.System, RiskLevel.Low,
        status: () => TweakStatus.Unknown,
        apply: () =>
        {
            var result = c.Runner.RunPowerShell("""
                Enable-ComputerRestore -Drive "$env:SystemDrive\"
                Checkpoint-Computer -Description 'Windows Optimizer' -RestorePointType 'MODIFY_SETTINGS'
                """);
            return FromCommand(result, "Restore point requested.");
        });

    public static ITweak TrimSystemDrive(TweakContext c) => new DelegateTweak(
        c, "trim-system-drive", "Run TRIM on the system drive",
        "Tells the SSD which blocks are free. Safe, and can take a few minutes.",
        TweakCategory.System, RiskLevel.Low,
        status: () => TweakStatus.Unknown,
        apply: () =>
        {
            var drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            return FromCommand(c.Runner.Run("defrag.exe", $"{drive} /L", timeoutMs: 600_000), "TRIM finished.");
        });

    public static ITweak LargeFilesReport(TweakContext c) => new DelegateTweak(
        c, "report-large-files", "List files of 500 MB or more",
        "Scans Downloads, Desktop, Documents and temp folders and saves the list as a CSV file. Nothing is deleted.",
        TweakCategory.System, RiskLevel.Low,
        status: () => TweakStatus.Unknown,
        apply: () =>
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new[]
            {
                Path.Combine(profile, "Downloads"),
                Path.Combine(profile, "Desktop"),
                Path.Combine(profile, "Documents"),
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
            }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            var found = roots
                .SelectMany(root => new DirectoryInfo(root).EnumerateFiles("*", options))
                .Where(f => f.Length >= LargeFileBytes)
                .OrderByDescending(f => f.Length)
                .ToList();

            Directory.CreateDirectory(c.DataRoot);
            var report = Path.Combine(c.DataRoot, "LargeFiles-500MB.csv");
            var csv = new StringBuilder("FullName,SizeGB,LastWriteTime").AppendLine();
            foreach (var file in found)
            {
                var size = (file.Length / 1024d / 1024d / 1024d).ToString("0.00", CultureInfo.InvariantCulture);
                csv.AppendLine($"\"{file.FullName.Replace("\"", "\"\"")}\",{size},{file.LastWriteTime:s}");
            }
            File.WriteAllText(report, csv.ToString(), new UTF8Encoding(true));

            return TweakResult.Ok(found.Count == 0
                ? "No files of 500 MB or more were found."
                : $"{found.Count} file(s) found. List saved to {report}");
        });

    public static ITweak ClearRecentHistory(TweakContext c) => new DelegateTweak(
        c, "clear-recent-history", "Clear recent files history",
        "Deletes File Explorer's recent file shortcuts, the Windows Media Player list and the Run dialog history. Your photos and videos are not touched.",
        TweakCategory.Privacy, RiskLevel.Medium,
        status: () => TweakStatus.Unknown,
        apply: () =>
        {
            var recent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent");
            var removed = 0;
            if (Directory.Exists(recent))
            {
                foreach (var entry in new DirectoryInfo(recent).EnumerateFileSystemInfos())
                {
                    try
                    {
                        if (entry is DirectoryInfo dir) dir.Delete(recursive: true); else entry.Delete();
                        removed++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }

            c.Registry.DeleteKeyTree(RegHive.CurrentUser, @"Software\Microsoft\MediaPlayer\Player\RecentFileList");
            c.Registry.ClearValues(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU");
            return TweakResult.Ok($"History cleared ({removed} items removed from Recent).");
        })
    {
        Warning = "The deleted history doesn't come back. To stop recording new items, use the \"Stop tracking recent files\" tweak.",
    };

    public static ITweak OneDriveRemove(TweakContext c) => new DelegateTweak(
        c, "onedrive-remove", "Uninstall OneDrive",
        "Closes and uninstalls OneDrive and keeps it from syncing again. The files in your folders are not deleted.",
        TweakCategory.Privacy, RiskLevel.High,
        status: () => TweakStatus.Unknown,
        apply: () =>
        {
            foreach (var process in Process.GetProcessesByName("OneDrive"))
            {
                try { process.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                process.Dispose();
            }

            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var setup = new[]
            {
                Path.Combine(windows, @"SysWOW64\OneDriveSetup.exe"),
                Path.Combine(windows, @"System32\OneDriveSetup.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\OneDrive\Update\OneDriveSetup.exe"),
            }.FirstOrDefault(File.Exists);

            c.Registry.SetValue(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", RegValue.DWord(1));
            c.Registry.DeleteValue(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "OneDrive");

            // OneDriveSetup /uninstall fails (0x8004041B) when OneDrive was never installed for this user.
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var installed = new[]
            {
                Path.Combine(localApp, @"Microsoft\OneDrive\OneDrive.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft OneDrive\OneDrive.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft OneDrive\OneDrive.exe"),
            }.Any(File.Exists);

            if (setup is null || !installed)
                return TweakResult.Ok("OneDrive is not installed for this user; syncing was blocked by policy.");
            return FromCommand(c.Runner.Run(setup, "/uninstall", timeoutMs: 300_000), "OneDrive uninstalled.");
        })
    {
        Warning = "The app doesn't reinstall OneDrive. To go back, download it from Microsoft's site and remove the DisableFileSyncNGSC policy. Your files are not deleted.",
    };

    public static ITweak RacingGtEvoAutostart(TweakContext c)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RacingGTEVO");
        var script = Path.Combine(folder, "Start-RacingGTEVO.ps1");
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Racing GT EVO.lnk");

        return new DelegateTweak(
            c, "autostart-racing-gt-evo", "Start Racing GT EVO at sign-in",
            "Opens the game 30 seconds after sign-in, with no PowerShell window. Only shows up if the game is installed.",
            TweakCategory.Gaming, RiskLevel.Low,
            status: () => File.Exists(shortcut) ? TweakStatus.Applied : TweakStatus.NotApplied,
            apply: () =>
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(script, RacingStartupScript, new UTF8Encoding(true));

                var shellType = Type.GetTypeFromProgID("WScript.Shell")
                    ?? throw new InvalidOperationException("WScript.Shell is not available.");
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic link = shell.CreateShortcut(shortcut);
                link.TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
                link.Arguments = $"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{script}\"";
                link.WorkingDirectory = folder;
                link.Description = "Starts Racing GT EVO 30 seconds after sign-in";
                link.Save();
                return TweakResult.Ok("Automatic start set up.");
            },
            undo: () =>
            {
                if (File.Exists(shortcut)) File.Delete(shortcut);
                if (File.Exists(script)) File.Delete(script);
                return TweakResult.Ok("Automatic start removed.");
            },
            available: () => File.Exists(RacingExe),
            preview: () => new[] { new PreviewLine("Startup folder shortcut", File.Exists(shortcut) ? "exists" : "missing", "exists", shortcut) });
    }

    private static TweakResult FromCommand(CommandResult result, string success) =>
        result.Success
            ? TweakResult.Ok(success)
            : TweakResult.Fail(result.Text.Length > 0 ? result.Text : $"The command exited with code {result.ExitCode} and no message.");

    private const string RacingStartupScript = """
        $ErrorActionPreference = 'Continue'
        $LogPath = Join-Path $env:LOCALAPPDATA 'RacingGTEVO-Startup.log'
        $Exe = 'C:\Program Files (x86)\BIOSTAR\RACINGGTEVO\RCGTII.exe'
        $WorkDir = Split-Path $Exe -Parent
        "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Launcher started" | Out-File -FilePath $LogPath -Append -Encoding utf8
        Start-Sleep -Seconds 30
        if (Test-Path $Exe) {
            try {
                $alreadyRunning = Get-Process -Name 'RCGTII' -ErrorAction SilentlyContinue
                if (-not $alreadyRunning) { Start-Process -FilePath $Exe -WorkingDirectory $WorkDir -ErrorAction Stop }
                "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Launch attempt finished" | Out-File -FilePath $LogPath -Append -Encoding utf8
            } catch { "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] ERROR: $($_.Exception.Message)" | Out-File -FilePath $LogPath -Append -Encoding utf8 }
        } else { "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] ERROR: executable not found" | Out-File -FilePath $LogPath -Append -Encoding utf8 }
        """;
}
