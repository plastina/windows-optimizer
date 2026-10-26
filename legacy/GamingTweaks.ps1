#requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'

$Root = Join-Path $env:ProgramData 'GamingTweaks'
$Log = Join-Path $Root 'GamingTweaks.log'
$Backup = Join-Path $Root 'Backup.json'
New-Item -Path $Root -ItemType Directory -Force | Out-Null
try { Start-Transcript -Path $Log -Append | Out-Null } catch {}

function Set-RegValue {
    param([string]$Path,[string]$Name,[object]$Value,[Microsoft.Win32.RegistryValueKind]$Type = [Microsoft.Win32.RegistryValueKind]::DWord)
    if (-not (Test-Path $Path)) { New-Item -Path $Path -Force | Out-Null }
    New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType $Type -Force | Out-Null
}
function Write-Section { param([string]$Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Test-Admin {
    $p = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
if (-not (Test-Admin)) { throw 'Execute este script como Administrador.' }

Write-Section 'Backup das configurações'
$regTargets = @(
    @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate';Name='SetDisableUXWUAccess'},
    @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU';Name='NoAutoUpdate'},
    @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU';Name='AUOptions'},
    @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate';Name='ExcludeWUDriversInQualityUpdate'},
    @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\DriverSearching';Name='SearchOrderConfig'},
    @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching';Name='SearchOrderConfig'},
    @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent';Name='DisableWindowsConsumerFeatures'},
    @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications';Name='GlobalUserDisabled'},
    @{Path='HKCU:\Software\Microsoft\GameBar';Name='AutoGameModeEnabled'},
    @{Path='HKCU:\Software\Microsoft\GameBar';Name='AllowAutoGameMode'},
    @{Path='HKCU:\System\GameConfigStore';Name='GameDVR_Enabled'},
    @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR';Name='AppCaptureEnabled'},
    @{Path='HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling';Name='PowerThrottlingOff'},
    @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize';Name='AppsUseLightTheme'},
    @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize';Name='SystemUsesLightTheme'},
    @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects';Name='VisualFXSetting'},
    @{Path='HKCU:\Control Panel\International';Name='sShortDate'},
    @{Path='HKCU:\Control Panel\International';Name='sShortTime'},
    @{Path='HKCU:\Control Panel\International';Name='sTimeFormat'},
    @{Path='HKCU:\Control Panel\Desktop';Name='ScreenSaveActive'},
    @{Path='HKCU:\Control Panel\Desktop';Name='ScreenSaverIsSecure'},
    @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize';Name='EnableTransparency'},
    @{Path='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System';Name='InactivityTimeoutSecs'},
    @{Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced';Name='TaskbarMn'},
    @{Path='HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search';Name='DisableSearchBoxSuggestions'}
)
$backupData = [ordered]@{
    Date = (Get-Date).ToString('o')
    PowerScheme = ((powercfg /getactivescheme) -join ' ')
    Registry = @()
    Services = @()
    DNS = @()
    NetworkAdapters = @()
}
foreach ($item in $regTargets) {
    $exists = Test-Path $item.Path; $value = $null; $kind = $null
    if ($exists) {
        try {
            $key = Get-Item -Path $item.Path
            if ($key.GetValueNames() -contains $item.Name) { $value = $key.GetValue($item.Name); $kind = $key.GetValueKind($item.Name).ToString() }
        } catch {}
    }
    $backupData.Registry += [pscustomobject]@{Path=$item.Path;Name=$item.Name;KeyExisted=$exists;ValueExisted=($null -ne $kind);Value=$value;Kind=$kind}
}
foreach ($svcName in @('wuauserv','UsoSvc','BITS','DoSvc')) {
    $svc = Get-CimInstance Win32_Service -Filter "Name='$svcName'" -ErrorAction SilentlyContinue
    if ($svc) { $backupData.Services += [pscustomobject]@{Name=$svcName;StartMode=$svc.StartMode;State=$svc.State} }
}
try {
    foreach ($adapter in Get-NetAdapter -ErrorAction Stop) {
        $dns = Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ErrorAction SilentlyContinue
        $backupData.NetworkAdapters += [pscustomobject]@{Name=$adapter.Name;InterfaceIndex=$adapter.ifIndex;Status=$adapter.Status;MacAddress=$adapter.MacAddress}
        foreach ($entry in $dns) {
            $backupData.DNS += [pscustomobject]@{InterfaceIndex=$entry.InterfaceIndex;AddressFamily=$entry.AddressFamily.ToString();ServerAddresses=@($entry.ServerAddresses)}
        }
    }
} catch { Write-Warning "Não foi possível salvar todo o backup de DNS: $($_.Exception.Message)" }
$backupData | ConvertTo-Json -Depth 8 | Set-Content -Path $Backup -Encoding UTF8
Write-Host "Backup salvo em: $Backup" -ForegroundColor Green

Write-Section 'Windows Update e drivers'
Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' 'NoAutoUpdate' 1
Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' 'AUOptions' 2
Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' 'ExcludeWUDriversInQualityUpdate' 1
Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DriverSearching' 'SearchOrderConfig' 0
Set-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching' 'SearchOrderConfig' 0
Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent' 'DisableWindowsConsumerFeatures' 1
foreach ($svcName in @('wuauserv','UsoSvc','BITS','DoSvc')) {
    try { Set-Service -Name $svcName -StartupType Manual -ErrorAction Stop } catch { Write-Warning "Não foi possível ajustar ${svcName}: $($_.Exception.Message)" }
}
Write-Host 'Atualizações automáticas desativadas por política; drivers excluídos do Windows Update.'
Write-Host 'Para atualizar manualmente, use o script Permitir_Update_Manual.ps1 ou remova temporariamente a política NoAutoUpdate.' -ForegroundColor Yellow

Write-Section 'Plano de energia e desempenho'
powercfg /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 2>$null | Out-Null
$plans = powercfg /list
$ultimate = $plans | Select-String -Pattern 'Ultimate Performance|Desempenho Máximo'
$high = $plans | Select-String -Pattern 'High performance|Alto desempenho'
$chosen = $ultimate
if (-not $chosen) { $chosen = $high }
if ($chosen) {
    $guid = [regex]::Match($chosen[0].Line,'[0-9a-fA-F-]{36}').Value
    if ($guid) { powercfg /setactive $guid }
} else { powercfg /setactive SCHEME_MIN }
powercfg /change monitor-timeout-ac 0
powercfg /change standby-timeout-ac 0
powercfg /change hibernate-timeout-ac 0
powercfg /SETACVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0
powercfg /SETDCVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0
powercfg /SETACTIVE SCHEME_CURRENT
Set-RegValue 'HKCU:\Control Panel\Desktop' 'ScreenSaveActive' '0' ([Microsoft.Win32.RegistryValueKind]::String)
Set-RegValue 'HKCU:\Control Panel\Desktop' 'ScreenSaverIsSecure' '0' ([Microsoft.Win32.RegistryValueKind]::String)
Set-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' 'InactivityTimeoutSecs' 0
Set-RegValue 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling' 'PowerThrottlingOff' 1
Write-Host 'Plano de energia ativo:'
powercfg /getactivescheme
Write-Host 'Tela, suspensão e hibernação por tempo limite na tomada desativadas; bloqueio por protetor de tela e tempo de inatividade ajustados.' -ForegroundColor Green

Write-Section 'Modo de jogo e captura em segundo plano'
Set-RegValue 'HKCU:\Software\Microsoft\GameBar' 'AutoGameModeEnabled' 1
Set-RegValue 'HKCU:\Software\Microsoft\GameBar' 'AllowAutoGameMode' 1
Set-RegValue 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled' 0
Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' 'AppCaptureEnabled' 0
Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SystemPaneSuggestionsEnabled' 0
Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-338389Enabled' 0
Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-353694Enabled' 0
Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search' 'DisableSearchBoxSuggestions' 1
try {
    $startPinsPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer'
    Set-RegValue $startPinsPath 'ConfigureStartPins' '{"pinnedList":[]}' ([Microsoft.Win32.RegistryValueKind]::String)
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization' 'NoLockScreen' 1
} catch { Write-Warning "Não foi possível aplicar todas as políticas de menu Iniciar/tela de bloqueio: $($_.Exception.Message)" }

Write-Section 'Tema escuro, relógio e efeitos visuais'
Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'AppsUseLightTheme' 0
Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'SystemUsesLightTheme' 0
Set-RegValue 'HKCU:\Control Panel\International' 'sShortDate' 'yyyy-MM-dd' ([Microsoft.Win32.RegistryValueKind]::String)
Set-RegValue 'HKCU:\Control Panel\International' 'sShortTime' 'HH:mm' ([Microsoft.Win32.RegistryValueKind]::String)
Set-RegValue 'HKCU:\Control Panel\International' 'sTimeFormat' 'HH:mm:ss' ([Microsoft.Win32.RegistryValueKind]::String)
Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' 'VisualFXSetting' 2
Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'TaskbarMn' 0
Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'TaskbarAnimations' 0
Set-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'EnableTransparency' 0
Write-Host 'Tema escuro ativado; data curta em yyyy-MM-dd (ex.: 2017-04-05); relógio em 24 horas; efeitos e transparência reduzidos.'

Write-Section 'Idioma e teclado: English (United States), teclado US'
try {
    if (Get-Command Install-Language -ErrorAction SilentlyContinue) {
        Install-Language -Language en-US -CopyToSettings -ErrorAction Stop
    }
} catch { Write-Warning "O pacote de idioma pode precisar ser instalado pelo Windows Update: $($_.Exception.Message)" }
try {
    $langList = New-WinUserLanguageList 'en-US'
    $langList[0].InputMethodTips.Clear()
    [void]$langList[0].InputMethodTips.Add('0409:00000409')
    Set-WinUserLanguageList $langList -Force
    Set-WinDefaultInputMethodOverride -InputTip '0409:00000409'
    Set-WinUILanguageOverride -Language 'en-US'
    Set-Culture 'en-US'
    Set-WinSystemLocale 'en-US'
    Set-RegValue 'HKCU:\Control Panel\International' 'sShortDate' 'yyyy-MM-dd' ([Microsoft.Win32.RegistryValueKind]::String)
    Write-Host 'Preferência de idioma/teclado configurada para en-US / US.'
} catch { Write-Warning "Algumas opções de idioma exigem sair da sessão ou instalar o pacote en-US: $($_.Exception.Message)" }

Write-Section 'DNS Google (IPv4 e IPv6)'
$google4 = @('8.8.8.8','8.8.4.4')
$google6 = @('2001:4860:4860::8888','2001:4860:4860::8844')
try {
    $activeAdapters = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.HardwareInterface }
    foreach ($adapter in $activeAdapters) {
        try {
            Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses $google4 -ErrorAction Stop
            $ipv6Enabled = Get-NetAdapterBinding -Name $adapter.Name -ComponentID ms_tcpip6 -ErrorAction SilentlyContinue
            if ($ipv6Enabled -and $ipv6Enabled.Enabled) {
                Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses $google6 -ErrorAction Stop
            }
            Write-Host "DNS Google aplicado em $($adapter.Name)" -ForegroundColor Green
        } catch { Write-Warning "Falha no adaptador $($adapter.Name): $($_.Exception.Message)" }
    }
    Clear-DnsClientCache -ErrorAction SilentlyContinue
    Get-DnsClientServerAddress | Where-Object { $_.InterfaceAlias -and $_.ServerAddresses } | Format-Table InterfaceAlias,AddressFamily,ServerAddresses -AutoSize
} catch { Write-Warning "Não foi possível configurar DNS: $($_.Exception.Message)" }

Write-Section 'OneDrive: desinstalação sem apagar arquivos pessoais'
try {
    Stop-Process -Name OneDrive -Force -ErrorAction SilentlyContinue
    $oneDriveSetup = @(
        "$env:SystemRoot\SysWOW64\OneDriveSetup.exe",
        "$env:SystemRoot\System32\OneDriveSetup.exe",
        "$env:LOCALAPPDATA\Microsoft\OneDrive\Update\OneDriveSetup.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($oneDriveSetup) {
        Start-Process -FilePath $oneDriveSetup -ArgumentList '/uninstall' -Wait -ErrorAction SilentlyContinue
        Write-Host 'Comando de desinstalação do OneDrive executado.'
    } else { Write-Host 'Instalador do OneDrive não encontrado; desinstalação ignorada.' }
    Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive' 'DisableFileSyncNGSC' 1
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (Test-Path $runKey) { Remove-ItemProperty -Path $runKey -Name 'OneDrive' -ErrorAction SilentlyContinue }
    Write-Host 'Não foram apagados arquivos das pastas OneDrive/Desktop/Documents.' -ForegroundColor Yellow
} catch { Write-Warning "Ajuste do OneDrive incompleto: $($_.Exception.Message)" }

Write-Section 'Iniciar Racing GT EVO no login (script oculto)'
$gameExe = 'C:\Program Files (x86)\BIOSTAR\RACINGGTEVO\RCGTII.exe'
if (Test-Path $gameExe) {
    $gameFolder = Join-Path $env:LOCALAPPDATA 'RacingGTEVO'
    $gameScript = Join-Path $gameFolder 'Start-RacingGTEVO.ps1'
    $startupFolder = [Environment]::GetFolderPath('Startup')
    $gameShortcutPath = Join-Path $startupFolder 'Racing GT EVO.lnk'
    New-Item -ItemType Directory -Path $gameFolder -Force | Out-Null
    $gameScriptContent = @'
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
'@
    Set-Content -LiteralPath $gameScript -Value $gameScriptContent -Encoding UTF8
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($gameShortcutPath)
    $shortcut.TargetPath = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
    $shortcut.Arguments = "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$gameScript`""
    $shortcut.WorkingDirectory = $gameFolder
    $shortcut.Description = 'Inicia o Racing GT EVO 30 segundos após o login'
    $shortcut.Save()
    Write-Host 'Inicialização oculta do Racing GT EVO configurada; script salvo fora da pasta Inicializar.' -ForegroundColor Green
} else { Write-Host 'Racing GT EVO não encontrado no caminho esperado; inicialização automática ignorada.' -ForegroundColor Yellow }

Write-Section 'Diagnóstico de armazenamento e sistema'
try {
    Get-Volume | Where-Object DriveLetter | Select-Object DriveLetter,FileSystemLabel,FileSystem,HealthStatus,@{N='FreeGB';E={[math]::Round($_.SizeRemaining/1GB,2)}},@{N='SizeGB';E={[math]::Round($_.Size/1GB,2)}} | Format-Table -AutoSize
} catch { Write-Warning 'Não foi possível listar volumes.' }
try { Get-PhysicalDisk | Select-Object FriendlyName,MediaType,HealthStatus,OperationalStatus,Size | Format-Table -AutoSize } catch { Write-Host 'Get-PhysicalDisk indisponível neste sistema.' }
try { Optimize-Volume -DriveLetter C -ReTrim -Verbose -ErrorAction Stop } catch { Write-Host 'Não foi possível consultar/executar TRIM em C:; verifique o tipo de unidade e permissões.' -ForegroundColor Yellow }
Write-Host 'Comandos de inicialização registrados no relatório:'
Get-CimInstance Win32_StartupCommand -ErrorAction SilentlyContinue | Select-Object Name,Command,Location,User | Format-Table -Wrap -AutoSize

Write-Section 'Arquivos grandes (somente relatório; sem exclusão automática)'
$scanRoots = @(
    (Join-Path $env:USERPROFILE 'Downloads'),
    (Join-Path $env:USERPROFILE 'Desktop'),
    (Join-Path $env:USERPROFILE 'Documents'),
    $env:TEMP,
    (Join-Path $env:SystemRoot 'Temp')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
$largeFileReport = Join-Path $Root 'LargeFiles-500MB.csv'
$largeFiles = foreach ($scanRoot in $scanRoots) {
    Get-ChildItem -LiteralPath $scanRoot -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Length -ge 500MB } |
        Select-Object FullName,@{N='SizeGB';E={[math]::Round($_.Length/1GB,2)}},Length,LastWriteTime
}
$largeFiles | Sort-Object Length -Descending | Export-Csv -LiteralPath $largeFileReport -NoTypeInformation -Encoding UTF8
if ($largeFiles) {
    $largeFiles | Sort-Object Length -Descending | Format-Table FullName,SizeGB,LastWriteTime -AutoSize -Wrap
    Write-Host "Relatório de arquivos >= 500 MB: $largeFileReport" -ForegroundColor Yellow
    $deleteChoice = Read-Host 'Deseja revisar/excluir arquivos listados, um por vez? Digite S para iniciar; qualquer outra tecla mantém tudo'
    if ($deleteChoice -match '^(S|SIM|Y|YES)$') {
        foreach ($file in ($largeFiles | Sort-Object Length -Descending)) {
            $answer = Read-Host "Excluir este arquivo? $($file.FullName) [$($file.SizeGB) GB] (S/N)"
            if ($answer -match '^(S|SIM|Y|YES)$') {
                try { Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop; Write-Host 'Excluído.' -ForegroundColor Green }
                catch { Write-Warning "Não foi possível excluir: $($_.Exception.Message)" }
            }
        }
    }
} else { Write-Host 'Nenhum arquivo >= 500 MB encontrado nas pastas verificadas.' }


Write-Section 'Histórico de fotos e vídeos abertos'
# Limpa históricos de acesso recentes do Explorador e do Windows Media Player.
# Não apaga fotos/vídeos pessoais nem o histórico interno de aplicativos de terceiros.
try {
    $recentItems = Join-Path $env:APPDATA 'Microsoft\Windows\Recent'
    if (Test-Path $recentItems) {
        Get-ChildItem -LiteralPath $recentItems -Force -ErrorAction SilentlyContinue |
            Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
        Write-Host 'Atalhos de arquivos recentes do Windows limpos.' -ForegroundColor Green
    }

    $recentAutomatic = Join-Path $env:APPDATA 'Microsoft\Windows\Recent\AutomaticDestinations'
    $recentCustom = Join-Path $env:APPDATA 'Microsoft\Windows\Recent\CustomDestinations'
    foreach ($historyPath in @($recentAutomatic, $recentCustom)) {
        if (Test-Path $historyPath) {
            Get-ChildItem -LiteralPath $historyPath -File -Force -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
    }

    # Limpa a lista de arquivos recentes do Windows Media Player, se existir.
    $wmpRecent = 'HKCU:\Software\Microsoft\MediaPlayer\Player\RecentFileList'
    if (Test-Path $wmpRecent) {
        Remove-Item -LiteralPath $wmpRecent -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host 'Lista de arquivos recentes do Windows Media Player limpa.' -ForegroundColor Green
    }

    # Desativa o registro de itens recentemente abertos no perfil atual.
    $explorerAdvanced = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
    Set-RegValue $explorerAdvanced 'Start_TrackDocs' 0
    Set-RegValue $explorerAdvanced 'ShowRecent' 0
    Set-RegValue $explorerAdvanced 'ShowFrequent' 0

    # Esvazia o histórico de execução do diálogo Executar.
    $runMRU = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU'
    if (Test-Path $runMRU) {
        Get-ItemProperty -Path $runMRU -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty PSObject |
            Out-Null
        $runKey = Get-Item -LiteralPath $runMRU -ErrorAction SilentlyContinue
        if ($runKey) {
            foreach ($valueName in @($runKey.GetValueNames() | Where-Object { $_ -ne 'PSPath' })) {
                Remove-ItemProperty -LiteralPath $runMRU -Name $valueName -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Write-Host 'Histórico recente de arquivos e sugestões do Explorador ajustados. Talvez seja necessário reiniciar o Explorador ou sair e entrar na sessão.' -ForegroundColor Green
} catch {
    Write-Warning "Não foi possível limpar todo o histórico: $($_.Exception.Message)"
}

Write-Section 'Hibernação e Inicialização Rápida (opcional)'
$hibernationChoice = Read-Host 'Deseja desativar hibernação e Inicialização Rápida? Isso desativa hibernação. Digite S para confirmar'
if ($hibernationChoice -match '^(S|SIM|Y|YES)$') {
    powercfg /hibernate off
    Write-Host 'Hibernação desativada. A Inicialização Rápida também fica indisponível.' -ForegroundColor Yellow
} else { Write-Host 'Hibernação mantida sem alterações.' }

Write-Section 'Ponto de restauração e netplwiz'
try {
    Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
    Checkpoint-Computer -Description 'Antes do GamingTweaks' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
    Write-Host 'Ponto de restauração criado.' -ForegroundColor Green
} catch { Write-Warning "Ponto de restauração não criado (pode estar desativado ou já existir um recente): $($_.Exception.Message)" }
try {
    $pwdLessPath = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device'
    if (Test-Path $pwdLessPath) { Set-RegValue $pwdLessPath 'DevicePasswordLessBuildVersion' 0 }
} catch {}
Write-Host 'Abrindo netplwiz para você revisar o login automático; não será armazenada nenhuma senha.'
Start-Process 'netplwiz.exe'

Write-Section 'Concluído'
Write-Host "Log: $Log"
Write-Host "Backup: $Backup"
Write-Host "Relatório de arquivos grandes: $largeFileReport"
Write-Host 'Defender e Firewall do Windows foram mantidos ativos.'
Write-Host 'Reinicie o PC para aplicar idioma, tema, relógio e algumas políticas.'
Write-Host 'Nota: o DNS Google pode não reduzir ping; o bloqueio de atualizações pode exigir o script de atualização manual.'
try { Stop-Transcript | Out-Null } catch {}
