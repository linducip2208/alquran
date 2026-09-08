param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\QuranDesktop\QuranDesktop.csproj"
$root = Split-Path -Parent (Split-Path -Parent $project)
$publish = Join-Path $root "artifacts\publish-$Runtime"
$zip = Join-Path $root "artifacts\QuranDesktop-$Runtime.zip"

if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
New-Item -ItemType Directory -Path (Split-Path $zip) -Force | Out-Null

$args = @(
    "publish", $project,
    "--configuration", $Configuration,
    "--runtime", $Runtime,
    "--self-contained", "true",
    "--output", $publish,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)
if ($Version) { $args += "-p:Version=$Version" }

dotnet @args
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
Set-Content -LiteralPath ($zip + ".sha256") -Value "$hash  $(Split-Path $zip -Leaf)"
Write-Host "Published: $zip"
Write-Host "SHA256: $hash"
