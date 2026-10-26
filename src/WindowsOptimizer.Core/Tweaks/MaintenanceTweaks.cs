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
        c, "hibernation-off", "Desativar hibernação e inicialização rápida",
        "Remove o arquivo de hibernação (libera vários GB) e desliga a inicialização rápida. O PC passa a desligar por completo.",
        TweakCategory.Power, RiskLevel.Low,
        status: () => c.Registry.GetValue(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled")
            is { Kind: RegKind.DWord, Data: 0 } ? TweakStatus.Applied : TweakStatus.NotApplied,
        apply: () => FromCommand(c.Runner.Run("powercfg", "/hibernate off"), "Hibernação desativada."),
        undo: () => FromCommand(c.Runner.Run("powercfg", "/hibernate on"), "Hibernação reativada."),
        preview: () => new[] { new PreviewLine("Hibernação e inicialização rápida", "ativadas", "desativadas") });

    public static ITweak RestorePoint(TweakContext c) => new DelegateTweak(
        c, "restore-point", "Criar ponto de restauração",
        "Salva um ponto de restauração para você poder voltar atrás pelo próprio Windows. O Windows aceita um ponto a cada 24 horas.",
        TweakCategory.System, RiskLevel.Low,
        status: () => TweakStatus.Unknown,
        apply: () =>
        {
            var result = c.Runner.RunPowerShell("""
                Enable-ComputerRestore -Drive "$env:SystemDrive\"
                Checkpoint-Computer -Description 'Windows Optimizer' -RestorePointType 'MODIFY_SETTINGS'
                """);
            return FromCommand(result, "Ponto de restauração solicitado.");
        });

    public static ITweak TrimSystemDrive(TweakContext c) => new DelegateTweak(
        c, "trim-system-drive", "Executar TRIM no disco do sistema",
        "Avisa o SSD quais blocos estão livres. É seguro e pode levar alguns minutos.",
        TweakCategory.System, RiskLevel.Low,
        status: () => TweakStatus.Unknown,
        apply: () =>
        {
            var drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            return FromCommand(c.Runner.Run("defrag.exe", $"{drive} /L", timeoutMs: 600_000), "TRIM concluído.");
        });

    public static ITweak LargeFilesReport(TweakContext c) => new DelegateTweak(
        c, "report-large-files", "Listar arquivos com 500 MB ou mais",
        "Procura em Downloads, Área de Trabalho, Documentos e pastas temporárias e salva a lista em um arquivo CSV. Nada é apagado.",
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
                ? "Nenhum arquivo de 500 MB ou mais foi encontrado."
                : $"{found.Count} arquivo(s) encontrados. Lista salva em {report}");
        });

    public static ITweak ClearRecentHistory(TweakContext c) => new DelegateTweak(
        c, "clear-recent-history", "Limpar histórico de arquivos recentes",
        "Apaga os atalhos de arquivos recentes do Explorador, a lista do Windows Media Player e o histórico do diálogo Executar. Suas fotos e vídeos não são tocados.",
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
            return TweakResult.Ok($"Histórico limpo ({removed} itens removidos de Recentes).");
        })
    {
        Warning = "O histórico apagado não volta. Para parar de registrar novos itens, use o ajuste \"Não registrar arquivos recentes\".",
    };

    public static ITweak OneDriveRemove(TweakContext c) => new DelegateTweak(
        c, "onedrive-remove", "Desinstalar o OneDrive",
        "Fecha e desinstala o OneDrive e impede que ele sincronize de novo. Os arquivos das suas pastas não são apagados.",
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

            if (setup is null) return TweakResult.Ok("Instalador do OneDrive não encontrado; a sincronização foi bloqueada por política.");
            return FromCommand(c.Runner.Run(setup, "/uninstall", timeoutMs: 300_000), "OneDrive desinstalado.");
        })
    {
        Warning = "O app não reinstala o OneDrive. Para voltar, baixe-o no site da Microsoft e remova a política DisableFileSyncNGSC. Seus arquivos não são apagados.",
    };

    public static ITweak RacingGtEvoAutostart(TweakContext c)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RacingGTEVO");
        var script = Path.Combine(folder, "Start-RacingGTEVO.ps1");
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Racing GT EVO.lnk");

        return new DelegateTweak(
            c, "autostart-racing-gt-evo", "Iniciar o Racing GT EVO ao entrar no Windows",
            "Abre o jogo 30 segundos depois do login, sem janela de PowerShell. Aparece só se o jogo estiver instalado.",
            TweakCategory.Gaming, RiskLevel.Low,
            status: () => File.Exists(shortcut) ? TweakStatus.Applied : TweakStatus.NotApplied,
            apply: () =>
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(script, RacingStartupScript, new UTF8Encoding(true));

                var shellType = Type.GetTypeFromProgID("WScript.Shell")
                    ?? throw new InvalidOperationException("WScript.Shell não está disponível.");
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic link = shell.CreateShortcut(shortcut);
                link.TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
                link.Arguments = $"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{script}\"";
                link.WorkingDirectory = folder;
                link.Description = "Inicia o Racing GT EVO 30 segundos após o login";
                link.Save();
                return TweakResult.Ok("Inicialização automática configurada.");
            },
            undo: () =>
            {
                if (File.Exists(shortcut)) File.Delete(shortcut);
                if (File.Exists(script)) File.Delete(script);
                return TweakResult.Ok("Inicialização automática removida.");
            },
            available: () => File.Exists(RacingExe),
            preview: () => new[] { new PreviewLine("Atalho em Inicializar", File.Exists(shortcut) ? "existe" : "ausente", "existe", shortcut) });
    }

    private static TweakResult FromCommand(CommandResult result, string success) =>
        result.Success ? TweakResult.Ok(success) : TweakResult.Fail(result.Text);

    private const string RacingStartupScript = """
        $ErrorActionPreference = 'Continue'
        $LogPath = Join-Path $env:LOCALAPPDATA 'RacingGTEVO-Startup.log'
        $Exe = 'C:\Program Files (x86)\BIOSTAR\RACINGGTEVO\RCGTII.exe'
        $WorkDir = Split-Path $Exe -Parent
        "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Inicializador iniciado" | Out-File -FilePath $LogPath -Append -Encoding utf8
        Start-Sleep -Seconds 30
        if (Test-Path $Exe) {
            try {
                $alreadyRunning = Get-Process -Name 'RCGTII' -ErrorAction SilentlyContinue
                if (-not $alreadyRunning) { Start-Process -FilePath $Exe -WorkingDirectory $WorkDir -ErrorAction Stop }
                "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Tentativa de iniciar concluída" | Out-File -FilePath $LogPath -Append -Encoding utf8
            } catch { "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] ERRO: $($_.Exception.Message)" | Out-File -FilePath $LogPath -Append -Encoding utf8 }
        } else { "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] ERRO: executável não encontrado" | Out-File -FilePath $LogPath -Append -Encoding utf8 }
        """;
}
