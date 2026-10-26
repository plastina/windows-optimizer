namespace WindowsOptimizer.Core;

/// <summary>
/// English (United States) with a US keyboard. These settings only exist as PowerShell cmdlets,
/// so the work happens in Windows PowerShell; the previous values are saved first as JSON.
/// </summary>
public sealed class LanguageTweak : TweakBase
{
    private const string Layout = "0409:00000409";

    public LanguageTweak(TweakContext ctx)
        : base(ctx, "language-en-us", "Idioma e teclado em inglês (EUA)",
            "Define idioma, região e formato como English (United States) com teclado US. Idiomas e teclados anteriores voltam ao desfazer, mas cada idioma volta com o teclado padrão dele.",
            TweakCategory.Language, RiskLevel.Medium)
    {
    }

    /// <summary>The user's regional format, e.g. "en-US". Some installs only store the numeric Locale (00000409).</summary>
    private string? CurrentLocale()
    {
        const string key = @"Control Panel\International";
        if (Ctx.Registry.GetValue(RegHive.CurrentUser, key, "LocaleName") is { Kind: RegKind.String } name)
            return (string)name.Data;
        if (Ctx.Registry.GetValue(RegHive.CurrentUser, key, "Locale") is { Kind: RegKind.String } id)
            return (string)id.Data == "00000409" ? "en-US" : "0x" + (string)id.Data;
        return null;
    }

    public override TweakStatus GetStatus()
    {
        var locale = CurrentLocale();
        if (locale is null) return TweakStatus.Unknown;
        return locale == "en-US" ? TweakStatus.Applied : TweakStatus.NotApplied;
    }

    public override IReadOnlyList<PreviewLine> Preview()
    {
        return new[]
        {
            new PreviewLine("Região e formato", CurrentLocale() ?? "—", "en-US"),
            new PreviewLine("Idioma de exibição", "—", "en-US (baixa o pacote se faltar)"),
            new PreviewLine("Teclado", "—", "US (" + Layout + ")"),
        };
    }

    public override TweakResult Apply()
    {
        if (Ctx.State.Load(Id) is null)
        {
            var read = Ctx.Runner.RunPowerShell("""
                $o = [ordered]@{
                    langs   = @(Get-WinUserLanguageList | ForEach-Object { $_.LanguageTag })
                    culture = (Get-Culture).Name
                    locale  = (Get-WinSystemLocale).Name
                    ui      = (Get-WinUILanguageOverride).Name
                    ime     = [string](Get-WinDefaultInputMethodOverride)
                }
                $o | ConvertTo-Json -Compress
                """);
            if (!read.Success) return TweakResult.Fail("Não foi possível ler as configurações atuais de idioma: " + read.Text);

            var snapshot = new TweakSnapshot { TweakId = Id };
            snapshot.Data["state"] = read.Output.Trim();
            Ctx.State.Save(snapshot);
        }

        // Installing the language pack downloads from Windows Update and can fail offline; the rest still works.
        var result = Ctx.Runner.RunPowerShell($$"""
            $note = ''
            if (Get-Command Install-Language -ErrorAction SilentlyContinue) {
                try { Install-Language -Language en-US -CopyToSettings | Out-Null }
                catch { $note = ' O pacote de idioma não foi instalado: ' + $_.Exception.Message }
            }
            $list = New-WinUserLanguageList 'en-US'
            $list[0].InputMethodTips.Clear()
            [void]$list[0].InputMethodTips.Add('{{Layout}}')
            Set-WinUserLanguageList $list -Force
            Set-WinDefaultInputMethodOverride -InputTip '{{Layout}}'
            Set-WinUILanguageOverride -Language 'en-US'
            Set-Culture 'en-US'
            Set-WinSystemLocale 'en-US'
            Write-Output ('Idioma en-US e teclado US configurados.' + $note)
            """, timeoutMs: 900_000);

        return result.Success ? TweakResult.Ok(result.Output.Trim()) : TweakResult.Fail(result.Text);
    }

    public override TweakResult Undo()
    {
        var snapshot = Ctx.State.Load(Id);
        if (snapshot is null || !snapshot.Data.TryGetValue("state", out var state))
            return TweakResult.Fail("Não há backup deste ajuste; o app não o aplicou.");

        var json = state.Replace("'", "''");
        var result = Ctx.Runner.RunPowerShell($$"""
            $s = '{{json}}' | ConvertFrom-Json
            $langs = @($s.langs)
            if ($langs.Count -gt 0) {
                $list = New-WinUserLanguageList $langs[0]
                foreach ($tag in ($langs | Select-Object -Skip 1)) { $list.Add((New-WinUserLanguageList $tag)[0]) }
                Set-WinUserLanguageList $list -Force
            }
            if ($s.ime) { Set-WinDefaultInputMethodOverride -InputTip $s.ime }
            if ($s.ui) { Set-WinUILanguageOverride -Language $s.ui }
            elseif ($langs.Count -gt 0) { Set-WinUILanguageOverride -Language $langs[0] }
            Set-Culture $s.culture
            Set-WinSystemLocale $s.locale
            """);

        if (!result.Success) return TweakResult.Fail(result.Text);

        Ctx.State.Delete(Id);
        return TweakResult.Ok("Idioma e região anteriores restaurados.");
    }
}
