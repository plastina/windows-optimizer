<#
.SYNOPSIS
    Rewrites the Scoop and winget manifests for a published GitHub release.

.DESCRIPTION
    Downloads the release's .sha256 file and regenerates:
      - bucket/windows-optimizer.json                      (Scoop bucket, this repository)
      - packaging/winget/manifests/p/plastina/WindowsOptimizer/<version>/*.yaml  (to submit to microsoft/winget-pkgs)
    Run it after every release, then commit the result.

.EXAMPLE
    .\tools\Update-Packaging.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$Repo = 'plastina/windows-optimizer'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exeName = "WindowsOptimizer-v$Version-win-x64.exe"
$baseUrl = "https://github.com/$Repo/releases/download/v$Version"

Write-Host "Reading the checksum of v$Version..."
$content = (Invoke-WebRequest -Uri "$baseUrl/$exeName.sha256" -UseBasicParsing).Content
# Windows PowerShell 5.1 returns octet-stream bodies as bytes; PowerShell 7 returns text.
$line = if ($content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($content) } else { [string]$content }
$hash = ($line.Trim() -split '\s+')[0].ToLowerInvariant()
if ($hash -notmatch '^[0-9a-f]{64}$') { throw "Unexpected checksum file content: $line" }

$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/v$Version" -Headers @{ 'User-Agent' = 'Update-Packaging' }
$releaseDate = ([datetime]$release.published_at).ToString('yyyy-MM-dd')

# --- Scoop ---------------------------------------------------------------------------------------
$scoop = [ordered]@{
    version      = $Version
    description  = 'Applies Windows performance, gaming and privacy tweaks one at a time, with undo.'
    homepage     = "https://github.com/$Repo"
    license      = 'MIT'
    architecture = [ordered]@{
        '64bit' = [ordered]@{
            url  = "$baseUrl/$exeName#/WindowsOptimizer.exe"
            hash = $hash
        }
    }
    shortcuts    = @(, @('WindowsOptimizer.exe', 'Windows Optimizer'))
    checkver     = 'github'
    autoupdate   = [ordered]@{
        architecture = [ordered]@{
            '64bit' = [ordered]@{
                url  = "https://github.com/$Repo/releases/download/v`$version/WindowsOptimizer-v`$version-win-x64.exe#/WindowsOptimizer.exe"
                hash = [ordered]@{ url = '$baseurl.sha256' }
            }
        }
    }
}
$bucket = Join-Path $root 'bucket'
New-Item -ItemType Directory -Path $bucket -Force | Out-Null
$scoop | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $bucket 'windows-optimizer.json') -Encoding UTF8

# --- winget --------------------------------------------------------------------------------------
$id = 'plastina.WindowsOptimizer'
$dir = Join-Path $root "packaging\winget\manifests\p\plastina\WindowsOptimizer\$Version"
New-Item -ItemType Directory -Path $dir -Force | Out-Null

$schema = '1.6.0'
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@ | Set-Content (Join-Path $dir "$id.yaml") -Encoding UTF8

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
Platform:
- Windows.Desktop
MinimumOSVersion: 10.0.17763.0
InstallerType: portable
Commands:
- windows-optimizer
ReleaseDate: $releaseDate
Installers:
- Architecture: x64
  InstallerUrl: $baseUrl/$exeName
  InstallerSha256: $($hash.ToUpperInvariant())
ManifestType: installer
ManifestVersion: $schema
"@ | Set-Content (Join-Path $dir "$id.installer.yaml") -Encoding UTF8

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: plastina
PublisherUrl: https://github.com/plastina
PackageName: Windows Optimizer
PackageUrl: https://github.com/$Repo
License: MIT
LicenseUrl: https://github.com/$Repo/blob/main/LICENSE
ShortDescription: Applies Windows performance, gaming and privacy tweaks one at a time, with undo.
Description: A desktop app that applies Windows tweaks one at a time. Each tweak shows what it will change, keeps a backup and can be undone. Needs administrator rights.
Tags:
- windows
- optimization
- gaming
- privacy
- tweaks
ReleaseNotesUrl: https://github.com/$Repo/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@ | Set-Content (Join-Path $dir "$id.locale.en-US.yaml") -Encoding UTF8

Write-Host "Scoop manifest:  bucket\windows-optimizer.json"
Write-Host "winget manifest: $dir"
Write-Host "SHA-256: $hash"
