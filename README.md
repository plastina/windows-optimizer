# Windows Optimizer

A desktop app for Windows 10/11 that applies performance, gaming and privacy tweaks **one at a time**. Every tweak shows what it will change before you apply it, and can be undone.

## What it does

- **Switches and badges**: each tweak is a switch (on, off, or partly on). Badges say what to expect before you flip it: **Recommended**, **Admin** (needs administrator rights), **Restarts Explorer**, **Restart PC**, and the risk level. A tweak that was already on before the app touched it is shown with a lock: there is no backup, so the app won't switch it off for you. Tweaks that don't apply to your PC (an app that isn't installed, hardware you don't have, a setting Windows protects) are listed at the bottom with the reason.
- **AI and Copilot**: turn off Recall's screenshots, Click to Do, the AI features of Paint, Notepad and Settings, and the Edge sidebar; remove the Recall component; and remove AI apps installed for your account (Microsoft Copilot, Microsoft 365 Copilot), which you can install again from the Store.
- **File Explorer, taskbar and Start**: file extensions, hidden files, the classic right-click menu, "End task" in the taskbar menu, taskbar alignment and search, Start without Recommended, compact Explorer, and many small ones.
- **Tweaks by area**: updates, power, gaming (Game Mode, Game Bar, GPU scheduling, Nagle's algorithm), appearance, language, network (DNS), cleanup and privacy, system.
- **Startup programs**: lists what starts with Windows (Run keys and Startup folders) and turns entries off the same way Task Manager does. Nothing is deleted, so turning one back on is exact.
- **Cleanup with a preview**: temporary files and the Windows Update download cache show how much space they would free before anything is deleted.
- **Light and dark theme**: follows Windows by default; the **Theme** button cycles System, Light and Dark.
- **PC health check** (read-only): finds what costs the most performance and no tweak can fix: a monitor below its maximum refresh rate, memory running under its rated speed (XMP/EXPO off), an old graphics driver, hard disks, a nearly full drive, a very old uptime.
- **Your games**: add a game's `.exe` and choose, one setting at a time, to run it on the high-performance GPU, turn off fullscreen optimizations for it, or exclude its folder from Defender scanning (Medium risk, with a warning; Defender stays on).
- **Before and after**: take a snapshot of the idle CPU load, memory in use, process count, startup programs and last boot time, apply tweaks, take another and compare. It measures what tweaks change, not frame rate (use CapFrameX or PresentMon for that).
- **Security trade-offs, clearly marked**: turning off Memory integrity (HVCI) and the Windows hypervisor can give back performance in some games, but weaken protection or stop WSL, Docker and Windows Sandbox. They are High risk and warn before they run.

## How it stays safe

- **See what changes**: each tweak lists the current value and the value that will be written, as a table of items.
- **Backup per tweak**: before changing anything, the app saves the previous state in `%ProgramData%\WindowsOptimizer\state`. **Undo** restores exactly that state, including deleting values that did not exist.
- **Restore point**: by default the app creates a system restore point before the first tweak of each session.
- **Log**: everything the app does is written to `%ProgramData%\WindowsOptimizer\windows-optimizer.log`.
- **Warnings where they matter**: high-risk tweaks and tweaks that can't be undone ask for confirmation.
- **Tested for real**: every tweak is applied and undone inside a disposable Windows Sandbox, and the self-test checks what Windows reports, not only what the app believes (see below).

The app needs to run as administrator (the Release build asks for that by itself). Windows Defender and the Firewall are never touched.

## Install

**Installer**: get `WindowsOptimizer-win-Setup.exe` from the [Releases](../../releases) page and run it. A wizard lets you choose the install folder (default `%LocalAppData%\plastina.WindowsOptimizer`); it installs for your account only, needs no admin rights and no .NET, and adds Start menu and desktop shortcuts. `/SILENT` and `/DIR="path"` work for unattended installs. Uninstall it from Settings > Apps. Check the `.sha256` file next to it if you want to verify the download.

**Updates install themselves**: each time the app opens it checks GitHub, downloads a newer version in the background and installs it when you close the app (or right away with **Restart now** on the banner). Only the files that changed are downloaded.

**Portable**: `WindowsOptimizer-win-Portable.zip` runs from any folder without installing, and updates itself the same way.

If you have the old standalone `WindowsOptimizer-vX.Y.Z-win-x64.exe` (0.2.x), it can't update itself: install with the Setup once, then delete the old exe.

**Scoop**:

```bash
scoop bucket add plastina https://github.com/plastina/windows-optimizer
scoop install windows-optimizer
```

**winget**: the manifests are ready in [`packaging/winget`](packaging/winget) but have not been submitted to the winget community repository yet, so `winget install` will work only after that.

> The installer isn't code-signed yet, so SmartScreen may show a warning the first time. Click "More info", then "Run anyway".

## Privacy

The app sends nothing about you or your PC anywhere. The one network request it makes is optional: when it starts, it asks GitHub for the latest release of this project (anonymous requests to `github.com` and `api.github.com`) and, if there is a newer version, downloads it from the same place. Turn it off with **Check for updates on start** in the sidebar. The setting is saved in `%LocalAppData%\WindowsOptimizer\settings.json`.

Some tweaks run Windows tools that use the network on their own (for example, installing a language pack goes through Windows Update); the app tells you when that can happen.

## Code signing policy

Releases are not signed yet. The release workflow is prepared for free code signing from the [SignPath Foundation](https://signpath.org); once the project is accepted, this section will carry their attribution and the list of people who commit, review and approve releases. How to set it up: [`packaging/SIGNING.md`](packaging/SIGNING.md).

## Development

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build WindowsOptimizer.sln
dotnet test WindowsOptimizer.sln
dotnet run --project src/WindowsOptimizer.App
```

The Debug build doesn't ask for administrator rights, so you can start it from the IDE; tweaks that write to HKLM will fail without elevation.

To render an image of the window without showing it on screen (`--theme light|dark` forces a theme without saving it, `--demo-update` shows the update banner):

```bash
dotnet run --project src/WindowsOptimizer.App -- --screenshot out.png --select 2 --expand --theme dark
```

### Testing the tweaks for real

The unit tests use a fake registry. To apply and undo every tweak for real **without touching your own PC**, use Windows Sandbox (Windows 10/11 Pro or Enterprise, feature `Containers-DisposableClientVM`):

```bash
powershell -ExecutionPolicy Bypass -File .\tools\Test-InSandbox.ps1
```

The script publishes the app, opens a disposable sandbox, runs `WindowsOptimizer.exe --selftest` inside it and prints a pass/fail report. For tweaks that can report what Windows says (services, power plan, DNS, language, hibernation, startup flags) the self-test also checks that applying changed the real state and that undoing brought it back. Never run `--selftest` on a machine you care about: it applies every tweak.

### Layout

| Folder | Contents |
|---|---|
| `src/WindowsOptimizer.Core` | Tweaks, backup, engine, self-test. No UI dependency. |
| `src/WindowsOptimizer.App` | WPF user interface, theme, update check. |
| `tests/WindowsOptimizer.Core.Tests` | Tests with a fake registry and fake commands. |
| `tools/` | Sandbox self-test and `Update-Packaging.ps1`. |
| `bucket/` | Scoop manifest (this repository is also a Scoop bucket). |
| `packaging/` | winget manifests and the code signing setup. |

### Adding a tweak

Open [`TweakCatalog.cs`](src/WindowsOptimizer.Core/TweakCatalog.cs) and add an entry.

- Registry values only: use `RegistryTweak` with `Reg.CuDword`, `Reg.LmDword`, `Reg.CuString` or `Reg.LmString`. Backup, undo and the "what changes" table come for free.
- An action, or something that touches files and commands: use `DelegateTweak` (see `MaintenanceTweaks.cs` and `CleanupTweaks.cs`).
- Anything more complex: derive from `TweakBase` or `RegistryTweak` (see `PowerPlanTweak`, `DnsTweak`, `UpdatesPauseTweak`).
- If Windows can report the real result (a service, a command), override `Fingerprint()` so the self-test can verify it.

High-risk tweaks need a `Warning`; a test enforces that.

## Releases

Releases are automatic. Every push to `main` runs the [`ci.yml`](.github/workflows/ci.yml) pipeline, which builds and tests, and, if the tests pass, publishes a GitHub release with the installer (`WindowsOptimizer-win-Setup.exe`), a portable zip, their checksums and the [Velopack](https://velopack.io) packages (`*.nupkg`, `releases.win.json`) that installed copies update from. Don't delete those packages from a release: installed copies would stop finding the update. Publishing a release by hand on GitHub builds the same files from its tag. Pull requests only build and test. You can also run it by hand from the Actions tab.

- **Version**: `<version.txt>.<run number>`, for example `0.2.7`. Edit [`version.txt`](version.txt) (it holds `MAJOR.MINOR`) to move to 0.3, 1.0 and so on; the last number keeps counting on its own.
- **No release for documentation**: pushes that only touch Markdown files, `LICENSE`, `bucket/` or `packaging/winget/` skip the pipeline.
- **Package manifests**: after each release the pipeline regenerates the Scoop and winget manifests with [`tools/Update-Packaging.ps1`](tools/Update-Packaging.ps1) and commits them to `main` (marked `[skip ci]`, so it can't start another release). Pull the latest `main` before you push.
- **Automatic updates**: every release counts as "latest", so installed copies with **Check for updates on start** on update themselves after each one. Pre-releases are skipped.

## License

[MIT](LICENSE)
