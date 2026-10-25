<#
.SYNOPSIS
  Builds the TempoMap release zip: the same steps on a clone and in GitHub Actions (docs/RELEASE.md).

.DESCRIPTION
  Publishes the App self-contained for win-x64, takes out what a release must not carry (.pdb and .lib files and the
  ffmpeg libraries ppy.osu.Framework.NativeLibs brings along: unused, LGPL), adds README.txt (from
  tools/release/README.template.txt) and LICENSE.txt, checks the result, zips it as TempoMap-v<version>-win-x64.zip with
  the folder of that name inside, and writes the zip's SHA-256 beside it.

  The version is the App csproj's InformationalVersion (YYYYMMDD.n); Version must say the same date and number
  (2026.10.23.0 for 20261023.0). The release tag is v<InformationalVersion>.

.PARAMETER OutputDirectory
  Where the zip, its .sha256 and the staging folder go (default: artifacts beside the repository root, git-ignored).

.EXAMPLE
  pwsh tools/package-release.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..' 'artifacts')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src' 'TimingAnalyzer.App' 'TimingAnalyzer.App.csproj'

# ---- version ---------------------------------------------------------------------------------------------------
[xml] $xml = Get-Content -Raw -LiteralPath $project
$info = $xml.SelectSingleNode('//InformationalVersion').InnerText.Trim()
$version = $xml.SelectSingleNode('//Version').InnerText.Trim()
if ($info -notmatch '^(\d{4})(\d{2})(\d{2})\.(\d+)$') {
    throw "InformationalVersion '$info' is not YYYYMMDD.n"
}
$expected = '{0}.{1}.{2}.{3}' -f [int] $Matches[1], [int] $Matches[2], [int] $Matches[3], [int] $Matches[4]
if ($version -ne $expected) {
    throw "Version '$version' does not match InformationalVersion '$info' (it should be '$expected'); change both in $project"
}
$tag = "v$info"
$name = "TempoMap-$tag-win-x64"

# ---- publish -----------------------------------------------------------------------------------------------------
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$stagingRoot = Join-Path $OutputDirectory 'staging'
$staging = Join-Path $stagingRoot $name
$zip = Join-Path $OutputDirectory "$name.zip"
if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null

Write-Host "Publishing $name ..."
# The build goes to its own folder (BaseOutputPath), so a TempoMap running from src/.../bin cannot hold a file this build needs.
$build = (Join-Path $OutputDirectory 'build') + [System.IO.Path]::DirectorySeparatorChar
dotnet publish $project -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false "-p:BaseOutputPath=$build" -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# ---- take out what a release does not carry ---------------------------------------------------------------------
# av_libglesv2.dll is ANGLE's GL library, not ffmpeg: it stays.
$ffmpeg = 'avcodec-*.dll', 'avformat-*.dll', 'avutil-*.dll', 'swscale-*.dll', 'swresample-*.dll', 'avfilter-*.dll', 'avdevice-*.dll', 'postproc-*.dll'
foreach ($file in Get-ChildItem -LiteralPath $staging -Recurse -File) {
    $drop = $file.Extension -in '.pdb', '.lib'
    foreach ($pattern in $ffmpeg) { if ($file.Name -like $pattern) { $drop = $true } }
    if ($drop) { Remove-Item -LiteralPath $file.FullName -Force }
}

# ---- README and LICENSE ------------------------------------------------------------------------------------------
$readme = (Get-Content -Raw -LiteralPath (Join-Path $root 'tools' 'release' 'README.template.txt')).Replace('{version}', $tag)
# Notepad before Windows 10 1809 wants CRLF.
[System.IO.File]::WriteAllText((Join-Path $staging 'README.txt'), ($readme -replace "`r?`n", "`r`n"), [System.Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $staging 'LICENSE.txt')

# ---- check the folder before it is zipped -----------------------------------------------------------------------
$problems = @()
foreach ($required in 'TempoMap.exe', 'TempoMap.dll', 'README.txt', 'LICENSE.txt', 'bass.dll', 'bass_fx.dll', 'bassmix.dll',
                      'onnxruntime.dll', 'Microsoft.ML.OnnxRuntime.dll', (Join-Path 'Models' 'beatnet.onnx')) {
    if (-not (Test-Path -LiteralPath (Join-Path $staging $required))) { $problems += "missing $required" }
}
foreach ($file in Get-ChildItem -LiteralPath $staging -Recurse -File) {
    if ($file.Extension -in '.pdb', '.lib') { $problems += "unwanted $($file.Name)" }
    foreach ($pattern in $ffmpeg) { if ($file.Name -like $pattern) { $problems += "ffmpeg library $($file.Name)" } }
}
$exe = Join-Path $staging 'TempoMap.exe'
if (Test-Path -LiteralPath $exe) {
    $product = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
    if (-not $product.StartsWith($info)) { $problems += "TempoMap.exe says version '$product', expected '$info'" }
}
if ($problems.Count -gt 0) { throw ("The release folder is not right:`n  " + ($problems -join "`n  ")) }

# ---- zip and hash ------------------------------------------------------------------------------------------------
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
$sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zip.sha256" -Value "$sha256  $name.zip" -Encoding ascii

$entries = [System.IO.Compression.ZipFile]::OpenRead($zip).Entries.Count
$megabytes = [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "$zip"
Write-Host "  $entries entries, $megabytes MB, SHA-256 $sha256"

# ---- hand the result to GitHub Actions ---------------------------------------------------------------------------
if ($env:GITHUB_OUTPUT) {
    "tag=$tag", "name=$name", "zip=$zip", "sha256=$sha256", "sha256file=$zip.sha256" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
}
if ($env:GITHUB_STEP_SUMMARY) {
    "### $name`n`n$entries entries, $megabytes MB`n`nSHA-256 ``$sha256``" | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
}
