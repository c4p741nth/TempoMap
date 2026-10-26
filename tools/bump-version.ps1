<#
.SYNOPSIS
  Raises the App csproj's Version and InformationalVersion to the next release, vYYYYMMDD.n (docs/RELEASE.md).

.DESCRIPTION
  Takes today's date (or -Date) and the first n for which v<date>.<n> is not yet a tag or a release on this clone or on
  GitHub, and writes it to both <Version> (2026.10.26.0) and <InformationalVersion> (20261026.0) in
  src/TimingAnalyzer.App/TimingAnalyzer.App.csproj, so the two cannot drift apart.

  If the csproj already says that version and it has no tag or release yet, there is nothing to raise and nothing is
  written. It never goes backwards: a csproj that is already later than the date given is an error.

  What counts as taken: local tags, tags on origin (git ls-remote), and, if gh is installed and logged in, the releases
  GitHub lists (this is the only way to see a draft, which has no tag yet). Pass -KnownTags to use a list instead of asking.

.PARAMETER Date
  The release date as yyyyMMdd (default: today).

.PARAMETER KnownTags
  Tags to treat as taken (e.g. v20261026.0), instead of asking git and gh.

.PARAMETER Project
  The csproj to edit (default: the App's).

.EXAMPLE
  pwsh tools/bump-version.ps1

.EXAMPLE
  pwsh tools/bump-version.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Date = [datetime]::Now.ToString('yyyyMMdd', [cultureinfo]::InvariantCulture),
    [string[]] $KnownTags,
    [string] $Project = (Join-Path $PSScriptRoot '..' 'src' 'TimingAnalyzer.App' 'TimingAnalyzer.App.csproj')
)

$ErrorActionPreference = 'Stop'
if ($Date -notmatch '^(\d{4})(\d{2})(\d{2})$') { throw "Date '$Date' is not yyyyMMdd" }
$Project = (Resolve-Path -LiteralPath $Project).Path

$head = [byte[]]::new(3)
$stream = [System.IO.File]::OpenRead($Project)
try { $null = $stream.Read($head, 0, 3) } finally { $stream.Dispose() }
$bom = $head[0] -eq 0xEF -and $head[1] -eq 0xBB -and $head[2] -eq 0xBF   # write the file back the way it was
$text = [System.IO.File]::ReadAllText($Project)

if ($text -notmatch '<InformationalVersion>\s*(\d{8})\.(\d+)\s*</InformationalVersion>') {
    throw "No <InformationalVersion>YYYYMMDD.n</InformationalVersion> in $Project"
}
$currentDate = $Matches[1]
$current = "$($Matches[1]).$($Matches[2])"
if ($currentDate -gt $Date) { throw "$Project says $current, which is later than $Date; pass -Date if that is not a mistake" }

# ---- which tags are taken ---------------------------------------------------------------------------------------
if (-not $PSBoundParameters.ContainsKey('KnownTags')) {
    $KnownTags = @()
    $KnownTags += @(git tag --list "v$Date.*")
    $KnownTags += @(git ls-remote --tags origin "refs/tags/v$Date.*" | ForEach-Object { ($_ -split "`t")[1] -replace '^refs/tags/', '' -replace '\^\{\}$', '' })
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        $listed = @(gh release list --limit 200 --json tagName --jq '.[].tagName' 2>$null)
        if ($LASTEXITCODE -eq 0) { $KnownTags += $listed }
        else { Write-Warning 'gh is not logged in: a draft release of that date would not be seen.' }
        $global:LASTEXITCODE = 0
    }
}
$taken = @($KnownTags | Where-Object { $_ })

# ---- the next version ------------------------------------------------------------------------------------------
$n = 0
while ($taken -contains "v$Date.$n") { $n++ }
$info = "$Date.$n"
if ($info -eq $current) {
    Write-Host "Already at $info and v$info has no tag or release: nothing to raise."
    return
}
$version = '{0}.{1}.{2}.{3}' -f [int] $Date.Substring(0, 4), [int] $Date.Substring(4, 2), [int] $Date.Substring(6, 2), $n

$text = $text -replace '<Version>[^<]*</Version>', "<Version>$version</Version>"
$text = $text -replace '<InformationalVersion>[^<]*</InformationalVersion>', "<InformationalVersion>$info</InformationalVersion>"
if ($PSCmdlet.ShouldProcess($Project, "raise $current to $info (Version $version)")) {
    [System.IO.File]::WriteAllText($Project, $text, [System.Text.UTF8Encoding]::new($bom))
    Write-Host "Raised $current to $info (Version $version) in $Project"
    Write-Host "Commit it on develop, open the pull request into main; the release tag will be v$info."
}
