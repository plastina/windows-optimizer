<#
.SYNOPSIS
    Applies and undoes every tweak inside Windows Sandbox, so nothing touches this PC.

.DESCRIPTION
    Publishes the app, starts a disposable Windows Sandbox with it mapped read-only, runs
    `WindowsOptimizer.exe --selftest` there and prints the report. Needs the Windows Sandbox
    feature (Windows 10/11 Pro or Enterprise) and the .NET SDK on this PC.

.EXAMPLE
    .\tools\Test-InSandbox.ps1
#>
[CmdletBinding()]
param(
    [string]$WorkDir = (Join-Path $env:TEMP 'WindowsOptimizer-sandbox'),
    [int]$TimeoutMinutes = 25
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

$sandbox = Get-Command WindowsSandbox.exe -ErrorAction SilentlyContinue
if (-not $sandbox) {
    throw 'Windows Sandbox is not installed. Enable it (as administrator) with: Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -NoRestart, then restart.'
}

$pkg = Join-Path $WorkDir 'pkg'
$out = Join-Path $WorkDir 'out'
Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $pkg, $out -Force | Out-Null

Write-Host 'Publishing the app (Release, win-x64, single file)...'
dotnet publish (Join-Path $repo 'src\WindowsOptimizer.App') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o $pkg --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Copy-Item (Join-Path $PSScriptRoot 'sandbox\run-selftest.ps1') $pkg

$wsb = Join-Path $WorkDir 'selftest.wsb'
@"
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>Enable</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$pkg</HostFolder>
      <SandboxFolder>C:\pkg</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$out</HostFolder>
      <SandboxFolder>C:\out</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\pkg\run-selftest.ps1</Command>
  </LogonCommand>
</Configuration>
"@ | Set-Content -Path $wsb -Encoding UTF8

Write-Host 'Opening Windows Sandbox (a window will appear; you do not need to do anything in it)...'
Start-Process -FilePath $wsb

$done = Join-Path $out 'done.txt'
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while (-not (Test-Path $done)) {
    if ((Get-Date) -gt $deadline) { throw "The self-test did not finish within $TimeoutMinutes minutes. See $out." }
    Start-Sleep -Seconds 5
}

Write-Host ''
Get-Content (Join-Path $out 'report.md') -Encoding UTF8 -ErrorAction SilentlyContinue
Write-Host ''
Write-Host "App exit code: $(Get-Content (Join-Path $out 'exitcode.txt'))"
Write-Host "Files in: $out"
Write-Host 'Close the Windows Sandbox window whenever you like; everything inside it is discarded.'
