# Windows Optimizer

A desktop app for Windows 10/11 that applies performance, gaming and privacy tweaks **one at a time**. Every tweak shows what it will change before you apply it, and can be undone.

## How it works

- **See what changes**: each tweak lists the current value and the value that will be written, as a table of items.
- **Backup per tweak**: before changing anything, the app saves the previous state in `%ProgramData%\WindowsOptimizer\state`. **Undo** restores exactly that state, including deleting values that did not exist.
- **Restore point**: by default the app creates a system restore point before the first tweak of each session.
- **Log**: everything the app does is written to `%ProgramData%\WindowsOptimizer\windows-optimizer.log`.
- **Warnings where they matter**: high-risk tweaks and tweaks that can't be undone ask for confirmation.

The app needs to run as administrator (the Release build asks for that by itself). Windows Defender and the Firewall are never touched.

## Download

Download `WindowsOptimizer-vX.Y.Z-win-x64.exe` from the [Releases](../../releases) page. There is nothing to install and no .NET needed. Check the `.sha256` file next to it if you want to verify the download.

> The executable isn't code-signed yet, so SmartScreen may show a warning the first time. Click "More info", then "Run anyway".

## Development

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build WindowsOptimizer.sln
dotnet test WindowsOptimizer.sln
dotnet run --project src/WindowsOptimizer.App
```

The Debug build doesn't ask for administrator rights, so you can start it from the IDE; tweaks that write to HKLM will fail without elevation.

To render an image of the window without showing it on screen:

```bash
dotnet run --project src/WindowsOptimizer.App -- --screenshot out.png --select 2 --expand
```

### Testing the tweaks for real

The unit tests use a fake registry. To apply and undo every tweak for real **without touching your own PC**, use Windows Sandbox (Windows 10/11 Pro or Enterprise, feature `Containers-DisposableClientVM`):

```bash
powershell -ExecutionPolicy Bypass -File .\tools\Test-InSandbox.ps1
```

The script publishes the app, opens a disposable sandbox, runs `WindowsOptimizer.exe --selftest` inside it and prints a pass/fail report. Never run `--selftest` on a machine you care about: it applies every tweak.

### Layout

| Folder | Contents |
|---|---|
| `src/WindowsOptimizer.Core` | Tweaks, backup, engine. No UI dependency. |
| `src/WindowsOptimizer.App` | WPF user interface. |
| `tests/WindowsOptimizer.Core.Tests` | Tests with a fake registry and fake commands. |
| `tools/` | Windows Sandbox self-test scripts. |

### Adding a tweak

Open [`TweakCatalog.cs`](src/WindowsOptimizer.Core/TweakCatalog.cs) and add an entry.

- Registry values only: use `RegistryTweak` with `Reg.CuDword`, `Reg.LmDword`, `Reg.CuString` or `Reg.LmString`. Backup, undo and the "what changes" table come for free.
- An action, or something that touches files and commands: use `DelegateTweak` (see `MaintenanceTweaks.cs`).
- Anything more complex: derive from `TweakBase` or `RegistryTweak` (see `PowerPlanTweak`, `DnsTweak`, `UpdatesPauseTweak`).

High-risk tweaks need a `Warning`; a test enforces that.

## Publishing a release

```bash
git tag v0.1.0
git push origin v0.1.0
```

The [`release.yml`](.github/workflows/release.yml) workflow runs the tests, publishes a single self-contained win-x64 `.exe` and attaches it to the release with its checksum. Tags with a hyphen (for example `v0.1.0-rc1`) are published as pre-releases. The [`ci.yml`](.github/workflows/ci.yml) workflow builds and tests on every push and pull request.
