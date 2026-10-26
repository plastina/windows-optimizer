# Windows Optimizer

Aplicativo de desktop para Windows 10/11 que aplica ajustes de desempenho, jogos e privacidade **um a um**. Cada ajuste mostra o que muda antes de ser aplicado e pode ser desfeito.

O ponto de partida foi o script [`legacy/GamingTweaks.ps1`](legacy/GamingTweaks.ps1), que fazia tudo de uma vez. Aqui cada seção virou um ajuste independente.

## Como funciona

- **Ver o que muda**: cada ajuste lista o valor atual e o valor que será gravado, em uma tabela de itens.
- **Backup por ajuste**: antes de mudar algo, o app salva o estado anterior em `%ProgramData%\WindowsOptimizer\state`. **Desfazer** restaura exatamente esse estado, inclusive apagando valores que não existiam.
- **Ponto de restauração**: por padrão o app cria um ponto de restauração antes do primeiro ajuste de cada sessão.
- **Registro**: tudo o que o app faz fica em `%ProgramData%\WindowsOptimizer\windows-optimizer.log`.
- **Aviso antes do que importa**: ajustes de risco alto ou que não podem ser desfeitos pedem confirmação.

O app precisa rodar como administrador (a versão Release pede isso sozinha). Windows Defender e Firewall nunca são alterados.

## Baixar

Baixe o `WindowsOptimizer-vX.Y.Z-win-x64.exe` na página de [Releases](../../releases). Não precisa instalar nem ter o .NET. Confira o arquivo `.sha256` ao lado, se quiser.

> O executável ainda não é assinado digitalmente, então o SmartScreen pode mostrar um aviso na primeira vez. Clique em "Mais informações" e depois em "Executar assim mesmo".

## Desenvolvimento

Requer o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build WindowsOptimizer.sln
dotnet test WindowsOptimizer.sln
dotnet run --project src/WindowsOptimizer.App
```

A build Debug não pede administrador, então dá para abrir pela IDE; os ajustes que gravam em HKLM vão falhar sem elevação.

Para gerar uma imagem da janela sem abri-la na tela:

```bash
dotnet run --project src/WindowsOptimizer.App -- --screenshot out.png --select 2 --expand
```

### Estrutura

| Pasta | Conteúdo |
|---|---|
| `src/WindowsOptimizer.Core` | Ajustes, backup, motor. Sem dependência de interface. |
| `src/WindowsOptimizer.App` | Interface WPF. |
| `tests/WindowsOptimizer.Core.Tests` | Testes com registro e comandos simulados. |
| `legacy/` | O script original, só como referência. |

### Adicionar um ajuste

Abra [`TweakCatalog.cs`](src/WindowsOptimizer.Core/TweakCatalog.cs) e acrescente uma entrada.

- Só valores de registro: use `RegistryTweak` com `Reg.CuDword`, `Reg.LmDword`, `Reg.CuString` ou `Reg.LmString`. Backup, desfazer e a tabela "o que muda" vêm de graça.
- Uma ação, ou algo que mexe em arquivos e comandos: use `DelegateTweak` (veja `MaintenanceTweaks.cs`).
- Mais complexo: herde de `TweakBase` ou `RegistryTweak` (veja `PowerPlanTweak`, `DnsTweak`, `UpdatesPauseTweak`).

Ajustes de risco alto precisam de `Warning`; um teste garante isso.

## Publicar uma versão

```bash
git tag v0.1.0
git push origin v0.1.0
```

O workflow [`release.yml`](.github/workflows/release.yml) roda os testes, publica um `.exe` único (self-contained, win-x64) e o anexa à release com o checksum. O workflow [`ci.yml`](.github/workflows/ci.yml) compila e testa a cada push e pull request.
