[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$builder = Join-Path $PSScriptRoot 'build_mod_release.ps1'
$cases = @(
    [pscustomobject]@{
        Channel = 'Stable'
        Version = 'v0.3.3'
        ModDirectory = 'ArknightsOperatorsMod'
        AssemblyName = 'ArknightsOperatorsMod'
        AssemblyFile = 'ArknightsOperatorsMod.dll'
        StaticId = 'local.arknights_amiya_duplicant'
        TitleSuffix = ''
    },
    [pscustomobject]@{
        Channel = 'Dev'
        Version = 'v0.3.3-dev.20260716.abcdef0'
        ModDirectory = 'ArknightsOperatorsMod.Testing'
        AssemblyName = 'ArknightsOperatorsTesting'
        AssemblyFile = 'ArknightsOperatorsTesting.dll'
        StaticId = 'local.arknights_operators_testing'
        TitleSuffix = '[DEV]'
    },
    [pscustomobject]@{
        Channel = 'RC'
        Version = 'v0.3.3-rc.1'
        ModDirectory = 'ArknightsOperatorsMod.Testing'
        AssemblyName = 'ArknightsOperatorsTesting'
        AssemblyFile = 'ArknightsOperatorsTesting.dll'
        StaticId = 'local.arknights_operators_testing'
        TitleSuffix = '[RC]'
    }
)

$passed = 0
$stableTitle = $null
foreach ($case in $cases) {
    $json = & $builder -Channel $case.Channel -Version $case.Version -IdentityProbe
    $actual = $json | ConvertFrom-Json
    $expectedVersion = $case.Version.TrimStart('v')
    $checks = [ordered]@{
        channel = $case.Channel
        version = $expectedVersion
        mod_directory = $case.ModDirectory
        assembly_name = $case.AssemblyName
        assembly_file = $case.AssemblyFile
        static_id = $case.StaticId
        title_suffix = $case.TitleSuffix
    }
    foreach ($property in $checks.Keys) {
        if ($actual.$property -cne $checks[$property]) {
            throw "$($case.Channel) identity $property was '$($actual.$property)'; expected '$($checks[$property])'."
        }
    }
    $staticIdMatches = [regex]::Matches($actual.mod_yaml, '(?m)^staticID:\s*(\S+)\s*$')
    if ($staticIdMatches.Count -ne 1 -or
        $staticIdMatches[0].Groups[1].Value -cne $case.StaticId) {
        throw "$($case.Channel) probe generated the wrong mod.yaml staticID."
    }
    $versionMatches = [regex]::Matches($actual.mod_info_yaml, '(?m)^version:\s*(\S+)\s*$')
    if ($versionMatches.Count -ne 1 -or
        $versionMatches[0].Groups[1].Value -cne $expectedVersion) {
        throw "$($case.Channel) probe generated the wrong mod_info.yaml version."
    }
    $titleMatch = [regex]::Match($actual.mod_yaml, '(?m)^title:\s*(.+?)\s*$')
    if (-not $titleMatch.Success) {
        throw "$($case.Channel) probe generated mod.yaml without a title."
    }
    if ($case.Channel -eq 'Stable') {
        $stableTitle = $titleMatch.Groups[1].Value
    } elseif ($titleMatch.Groups[1].Value -cne "$stableTitle $($case.TitleSuffix)") {
        throw "$($case.Channel) title did not preserve the Stable title plus its channel suffix."
    }
    $passed++
}

$dirtyProbeJson = & $builder -Channel Dev `
    -Version 'v0.3.3-dev.20260716.abcdef0.local-dirty' -IdentityProbe
$dirtyProbe = $dirtyProbeJson | ConvertFrom-Json
if ($dirtyProbe.version -cne '0.3.3-dev.20260716.abcdef0.local-dirty') {
    throw 'The generated local-dirty Nightly version is not accepted by the Dev channel.'
}

$nightlyRejected = $false
try {
    & $builder -Channel RC -Nightly -IdentityProbe 2>$null | Out-Null
} catch {
    if ($_.Exception.Message -ne 'Nightly builds are only valid for the Dev channel.') {
        throw
    }
    $nightlyRejected = $true
}
if (-not $nightlyRejected) {
    throw 'RC incorrectly accepted the Nightly switch.'
}

Write-Output "PackagingChannelProbe: $passed channels passed; Nightly rules passed"
