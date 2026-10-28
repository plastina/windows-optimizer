; Wizard (welcome, install folder, summary, progress, finish) that wraps Velopack's Setup.exe.
; Velopack still does the real install and owns the update mechanism and the Settings > Apps uninstall entry,
; so this script installs nothing itself and registers no uninstaller of its own (Uninstallable=no).
; The pipeline compiles it with:
;   ISCC /DAppVersion=1.2.3 /DVelopackSetup=<path to Velopack's Setup.exe> /DIconFile=<path to app.ico> /DOutDir=<folder> WindowsOptimizer.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef VelopackSetup
  #error VelopackSetup must point at the Setup.exe that vpk pack produced.
#endif
#ifndef OutDir
  #define OutDir "."
#endif
#ifndef IconFile
  #error IconFile must point at the app's .ico.
#endif

[Setup]
AppName=Windows Optimizer
AppVersion={#AppVersion}
AppPublisher=plastina
AppPublisherURL=https://github.com/plastina/windows-optimizer
; Same folder the Velopack-only installer used, so people upgrading from it land in the same place.
DefaultDirName={localappdata}\plastina.WindowsOptimizer
DisableProgramGroupPage=yes
DisableDirPage=no
DisableReadyPage=no
; Per user, no UAC prompt; the app itself asks for administrator rights when it runs.
PrivilegesRequired=lowest
Uninstallable=no
CreateUninstallRegKey=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutDir}
OutputBaseFilename=WindowsOptimizer-win-Setup
SetupIconFile={#IconFile}
WizardStyle=classic
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#VelopackSetup}"; DestDir: "{tmp}"; DestName: "VelopackSetup.exe"; Flags: deleteafterinstall

[Run]
; Velopack creates the Start menu and desktop shortcuts and the Settings > Apps entry.
Filename: "{tmp}\VelopackSetup.exe"; Parameters: "--silent --installto ""{app}"""; \
  StatusMsg: "{cm:Installing}"; Flags: waituntilterminated
; The "Run Windows Optimizer" checkbox on the last page.
Filename: "{app}\current\WindowsOptimizer.exe"; Description: "{cm:LaunchProgram,Windows Optimizer}"; \
  Flags: nowait postinstall skipifsilent

[CustomMessages]
brazilianportuguese.Installing=Instalando o Windows Optimizer...
english.Installing=Installing Windows Optimizer...
