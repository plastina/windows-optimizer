namespace WindowsOptimizer.Core;

/// <summary>Every tweak the app offers, in display order. Add a new one here.</summary>
public static class TweakCatalog
{
    private const string WindowsUpdatePolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string International = @"Control Panel\International";

    public static IReadOnlyList<ITweak> Create(TweakContext c) => new ITweak[]
    {
        // Atualizações
        new UpdatesPauseTweak(c)
        {
            NeedsRestart = true,
            Warning = "Sem atualizações automáticas o PC deixa de receber correções de segurança até você atualizar manualmente. Use Desfazer para voltar ao normal.",
        },
        new RegistryTweak(c, "updates-exclude-drivers", "Não instalar drivers pelo Windows Update",
            "Impede que o Windows Update troque drivers de vídeo e de periféricos sem você pedir. Atualize os drivers pelo site do fabricante.",
            TweakCategory.Updates, RiskLevel.Medium,
            Reg.LmDword(WindowsUpdatePolicy, "ExcludeWUDriversInQualityUpdate", 1),
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\DriverSearching", "SearchOrderConfig", 0),
            Reg.LmDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0)),

        // Energia
        new PowerPlanTweak(c),
        new RegistryTweak(c, "power-no-idle-lock", "Desativar protetor de tela e bloqueio por inatividade",
            "Evita que o PC bloqueie ou ative o protetor de tela sozinho durante uma corrida ou transmissão.",
            TweakCategory.Power, RiskLevel.Low,
            Reg.CuString(@"Control Panel\Desktop", "ScreenSaveActive", "0"),
            Reg.CuString(@"Control Panel\Desktop", "ScreenSaverIsSecure", "0"),
            Reg.LmDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "InactivityTimeoutSecs", 0)),
        new RegistryTweak(c, "power-throttling-off", "Desativar limitação de energia de processos",
            "Impede que o Windows reduza a velocidade de processos para economizar energia. Útil em PC de mesa.",
            TweakCategory.Power, RiskLevel.Low,
            Reg.LmDword(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1))
        {
            NeedsRestart = true,
        },
        MaintenanceTweaks.HibernationOff(c),

        // Jogos
        new RegistryTweak(c, "game-mode-on", "Ativar o Modo de Jogo",
            "Pede ao Windows que priorize o jogo em primeiro plano e adie tarefas de segundo plano.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.CuDword(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
            Reg.CuDword(@"Software\Microsoft\GameBar", "AllowAutoGameMode", 1)),
        new RegistryTweak(c, "game-dvr-off", "Desativar a gravação em segundo plano",
            "Desliga o Game DVR, que grava os últimos minutos do jogo e consome disco e processador.",
            TweakCategory.Gaming, RiskLevel.Low,
            Reg.CuDword(@"System\GameConfigStore", "GameDVR_Enabled", 0),
            Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0)),
        MaintenanceTweaks.RacingGtEvoAutostart(c),

        // Aparência
        new RegistryTweak(c, "dark-theme", "Tema escuro",
            "Usa o tema escuro nos aplicativos e no sistema.",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.CuDword(Personalize, "AppsUseLightTheme", 0),
            Reg.CuDword(Personalize, "SystemUsesLightTheme", 0)),
        new RegistryTweak(c, "visual-effects-reduced", "Reduzir efeitos visuais e transparência",
            "Escolhe o ajuste de melhor desempenho para os efeitos visuais e desliga animações da barra de tarefas e a transparência.",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.CuDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2),
            Reg.CuDword(ExplorerAdvanced, "TaskbarAnimations", 0),
            Reg.CuDword(Personalize, "EnableTransparency", 0))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "taskbar-chat-off", "Ocultar o Chat da barra de tarefas",
            "Remove o ícone de Chat (Teams) da barra de tarefas.",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.CuDword(ExplorerAdvanced, "TaskbarMn", 0)),
        new RegistryTweak(c, "datetime-iso-24h", "Data AAAA-MM-DD e relógio de 24 horas",
            "Mostra a data como 2017-04-05 e a hora como 14:30:00.",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.CuString(International, "sShortDate", "yyyy-MM-dd"),
            Reg.CuString(International, "sShortTime", "HH:mm"),
            Reg.CuString(International, "sTimeFormat", "HH:mm:ss"))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "start-pins-empty", "Esvaziar os itens fixados do Menu Iniciar",
            "Aplica uma política que deixa a lista de itens fixados do Menu Iniciar vazia.",
            TweakCategory.Appearance, RiskLevel.Medium,
            Reg.LmString(@"SOFTWARE\Policies\Microsoft\Windows\Explorer", "ConfigureStartPins", """{"pinnedList":[]}"""))
        {
            NeedsRestart = true,
        },
        new RegistryTweak(c, "lock-screen-off", "Pular a tela de bloqueio",
            "Vai direto para a tela de login, sem a imagem de bloqueio.",
            TweakCategory.Appearance, RiskLevel.Low,
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", 1)),

        // Idioma e região
        new LanguageTweak(c)
        {
            NeedsRestart = true,
            Warning = "Muda idioma, região e teclado do Windows. Se o pacote en-US não estiver instalado, o Windows o baixa e isso pode levar alguns minutos.",
        },

        // Rede
        new DnsTweak(c),

        // Limpeza e privacidade
        new RegistryTweak(c, "suggestions-off", "Desativar sugestões e apps promovidos",
            "Desliga dicas, anúncios no Menu Iniciar, apps promovidos e sugestões na caixa de pesquisa.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(ContentDelivery, "SystemPaneSuggestionsEnabled", 0),
            Reg.CuDword(ContentDelivery, "SubscribedContent-338389Enabled", 0),
            Reg.CuDword(ContentDelivery, "SubscribedContent-353694Enabled", 0),
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1),
            Reg.LmDword(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableSearchBoxSuggestions", 1)),
        new RegistryTweak(c, "recent-tracking-off", "Não registrar arquivos recentes",
            "Para de guardar a lista de arquivos recentes e frequentes no Explorador e no Menu Iniciar.",
            TweakCategory.Privacy, RiskLevel.Low,
            Reg.CuDword(ExplorerAdvanced, "Start_TrackDocs", 0),
            Reg.CuDword(ExplorerAdvanced, "ShowRecent", 0),
            Reg.CuDword(ExplorerAdvanced, "ShowFrequent", 0)),
        MaintenanceTweaks.ClearRecentHistory(c),
        MaintenanceTweaks.OneDriveRemove(c),

        // Sistema
        MaintenanceTweaks.RestorePoint(c),
        MaintenanceTweaks.TrimSystemDrive(c),
        MaintenanceTweaks.LargeFilesReport(c),
        new AutoLoginTweak(c),
    };
}

/// <summary>Reveals the "users must enter a password" checkbox and opens netplwiz so the user can set up auto sign-in.</summary>
public sealed class AutoLoginTweak : RegistryTweak
{
    public AutoLoginTweak(TweakContext ctx)
        : base(ctx, "autologin-netplwiz", "Configurar login automático (netplwiz)",
            "Abre o netplwiz para você escolher o usuário que entra sem senha. O app não guarda nenhuma senha.",
            TweakCategory.System, RiskLevel.Medium,
            Reg.LmDword(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device", "DevicePasswordLessBuildVersion", 0, onlyIfKeyExists: true))
    {
        Warning = "Entrar sem senha deixa qualquer pessoa com acesso físico ao PC usar a sua conta.";
    }

    protected override string? OnApplied(TweakSnapshot snapshot)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("netplwiz.exe") { UseShellExecute = true });
        return "netplwiz aberto. Desmarque \"Os usuários devem digitar...\" e confirme a conta.";
    }
}
