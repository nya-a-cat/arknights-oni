[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,
    [string]$LocalModsRoot = (Join-Path (
        [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
    ) 'Klei\OxygenNotIncluded\mods\Local')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$maximumPackageBytes = 16L * 1024L * 1024L
$maximumSidecarBytes = 1L * 1024L * 1024L
$maximumEntryBytes = 16L * 1024L * 1024L
$maximumExpandedBytes = 64L * 1024L * 1024L

function Assert-PathWithin {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Parent
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Testing install path escaped its allowed root: $fullPath"
    }
    return $fullPath
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($Bytes))).Replace('-', '')
    } finally {
        $sha256.Dispose()
    }
}

function Read-ZipEntryBytes {
    param([Parameter(Mandatory = $true)]$Entry)

    $input = $Entry.Open()
    $output = [System.IO.MemoryStream]::new()
    try {
        $input.CopyTo($output)
        [byte[]]$result = $output.ToArray()
        Write-Output -NoEnumerate $result
    } finally {
        $output.Dispose()
        $input.Dispose()
    }
}

function Convert-StrictUtf8ToString {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    return $utf8.GetString($Bytes)
}

function Assert-UniqueJsonProperties {
    param(
        [Parameter(Mandatory = $true)][string]$Json,
        [Parameter(Mandatory = $true)][string[]]$Properties,
        [Parameter(Mandatory = $true)][string]$Label
    )

    foreach ($property in $Properties) {
        $propertyPattern = '"' + [regex]::Escape($property) + '"\s*:'
        if ([regex]::Matches($Json, $propertyPattern).Count -ne 1) {
            throw "$Label must contain exactly one $property property."
        }
    }
}

function Get-ManagedAssemblyNameFromBytes {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $probePath = Join-Path ([System.IO.Path]::GetTempPath()) (
        'arknights-oni-assembly-probe-' + [Guid]::NewGuid().ToString('N') + '.dll'
    )
    try {
        [System.IO.File]::WriteAllBytes($probePath, $Bytes)
        return [System.Reflection.AssemblyName]::GetAssemblyName($probePath).Name
    } finally {
        [System.IO.File]::Delete($probePath)
    }
}

$package = [System.IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
    throw "Testing package does not exist: $package"
}
if ([System.IO.Path]::GetExtension($package) -ine '.zip') {
    throw 'Testing installation accepts a packaged ZIP only.'
}

$packageInfo = Get-Item -LiteralPath $package
if ($packageInfo.Length -gt $maximumPackageBytes) {
    throw "Testing ZIP exceeds the $maximumPackageBytes byte compressed-size limit."
}
[byte[]]$packageBytes = [System.IO.File]::ReadAllBytes($package)
if ($packageBytes.LongLength -gt $maximumPackageBytes) {
    throw "Testing ZIP changed while reading or exceeds the $maximumPackageBytes byte limit."
}
$packageHash = Get-Sha256Hex -Bytes $packageBytes

$sidecarName = [System.IO.Path]::GetFileNameWithoutExtension($package) + '.build-info.json'
$sidecarPath = Join-Path ([System.IO.Path]::GetDirectoryName($package)) $sidecarName
if (-not (Test-Path -LiteralPath $sidecarPath -PathType Leaf)) {
    throw "Testing package sidecar is missing: $sidecarPath"
}
$sidecarInfo = Get-Item -LiteralPath $sidecarPath
if ($sidecarInfo.Length -gt $maximumSidecarBytes) {
    throw "Testing build-info sidecar exceeds the $maximumSidecarBytes byte limit."
}
[byte[]]$sidecarBytes = [System.IO.File]::ReadAllBytes($sidecarPath)
if ($sidecarBytes.LongLength -gt $maximumSidecarBytes) {
    throw 'Testing build-info sidecar changed while reading or exceeds its size limit.'
}
$sidecarText = Convert-StrictUtf8ToString -Bytes $sidecarBytes
Assert-UniqueJsonProperties -Json $sidecarText -Label 'Testing build-info sidecar' -Properties @(
    'schemaVersion', 'version', 'channel', 'sourceSha', 'sourceShortSha',
    'sourceCommitTimeUtc', 'branch', 'dirty', 'localDirty', 'eligibleForUpload',
    'compiledInThisRun', 'modDirectory', 'assemblyName', 'assemblyFile', 'staticID',
    'dllSha256', 'zipFile', 'zipSha256', 'embeddedBuildInfoSha256', 'generatedAtUtc'
)
$sidecar = $sidecarText | ConvertFrom-Json
if ($sidecar.zipFile -cne [System.IO.Path]::GetFileName($package) -or
    $sidecar.zipSha256 -cne $packageHash) {
    throw 'Testing ZIP filename or SHA-256 does not match its build-info sidecar.'
}

$localRoot = [System.IO.Path]::GetFullPath($LocalModsRoot)
$testingRootName = 'ArknightsOperatorsMod.Testing'
$testingPrefix = "$testingRootName/"
$destination = Assert-PathWithin -Path (Join-Path $localRoot $testingRootName) -Parent $localRoot

Add-Type -AssemblyName System.IO.Compression
$zipStream = [System.IO.MemoryStream]::new($packageBytes, $false)
$archive = [System.IO.Compression.ZipArchive]::new(
    $zipStream,
    [System.IO.Compression.ZipArchiveMode]::Read,
    $false
)
try {
    $fileEntries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
    if ($fileEntries.Count -eq 0) {
        throw 'Testing package is empty.'
    }
    if ($archive.Entries.Count -ne $fileEntries.Count) {
        throw 'Testing package must not contain directory entries.'
    }

    [long]$expandedBytes = 0
    foreach ($entry in $archive.Entries) {
        $entryPath = $entry.FullName.Replace('\', '/')
        if ($entryPath -match '^[\/]' -or $entryPath.Contains(':') -or
            @($entryPath.Split('/') | Where-Object { $_ -eq '..' -or $_ -eq '.' }).Count -ne 0) {
            throw "Testing package contains an unsafe archive path: $($entry.FullName)"
        }
        if (-not $entryPath.StartsWith($testingPrefix, [System.StringComparison]::Ordinal)) {
            throw "Testing package contains a path outside $testingRootName`: $($entry.FullName)"
        }
        if ($entry.Length -gt $maximumEntryBytes) {
            throw "Testing package entry exceeds the $maximumEntryBytes byte limit: $entryPath"
        }
        $expandedBytes += $entry.Length
        if ($expandedBytes -gt $maximumExpandedBytes) {
            throw "Testing package exceeds the $maximumExpandedBytes byte expanded-size limit."
        }
    }

    $expectedEntries = @(
        "${testingPrefix}ArknightsOperatorsTesting.dll",
        "${testingPrefix}DATA_NOTICE.md",
        "${testingPrefix}PLIB-LICENSE.txt",
        "${testingPrefix}PLIB-SOURCE.txt",
        "${testingPrefix}SPINE-RUNTIME-LICENSE.txt",
        "${testingPrefix}THIRD_PARTY_NOTICES.md",
        "${testingPrefix}assets/catalog/operator_appearances_20260604.json",
        "${testingPrefix}build-info.json",
        "${testingPrefix}mod.yaml",
        "${testingPrefix}mod_info.yaml",
        "${testingPrefix}PLib.dll",
        "${testingPrefix}SPINE-RUNTIME-SOURCE.txt"
    ) | Sort-Object
    $actualEntries = @($fileEntries.FullName | Sort-Object)
    $entryDifference = @(Compare-Object -ReferenceObject $expectedEntries -DifferenceObject $actualEntries)
    if ($entryDifference.Count -ne 0) {
        throw "Testing package file whitelist mismatch: $($entryDifference | Out-String)"
    }

    $dllEntry = $archive.GetEntry("${testingPrefix}ArknightsOperatorsTesting.dll")
    $modYamlEntry = $archive.GetEntry("${testingPrefix}mod.yaml")
    $modInfoEntry = $archive.GetEntry("${testingPrefix}mod_info.yaml")
    $buildInfoEntry = $archive.GetEntry("${testingPrefix}build-info.json")
    if ($null -eq $dllEntry -or $null -eq $modYamlEntry -or
        $null -eq $modInfoEntry -or $null -eq $buildInfoEntry) {
        throw 'Testing package is missing a required identity file.'
    }

    [byte[]]$dllBytes = Read-ZipEntryBytes -Entry $dllEntry
    [byte[]]$modYamlBytes = Read-ZipEntryBytes -Entry $modYamlEntry
    [byte[]]$modInfoBytes = Read-ZipEntryBytes -Entry $modInfoEntry
    [byte[]]$embeddedBuildInfoBytes = Read-ZipEntryBytes -Entry $buildInfoEntry
    $dllHash = Get-Sha256Hex -Bytes $dllBytes
    $embeddedBuildInfoHash = Get-Sha256Hex -Bytes $embeddedBuildInfoBytes
    $modYaml = Convert-StrictUtf8ToString -Bytes $modYamlBytes
    $modInfoYaml = Convert-StrictUtf8ToString -Bytes $modInfoBytes
    $embeddedBuildInfoText = Convert-StrictUtf8ToString -Bytes $embeddedBuildInfoBytes
    Assert-UniqueJsonProperties -Json $embeddedBuildInfoText -Label 'Embedded build-info' -Properties @(
        'schemaVersion', 'version', 'channel', 'sourceSha', 'sourceShortSha',
        'sourceCommitTimeUtc', 'branch', 'dirty', 'localDirty', 'eligibleForUpload',
        'compiledInThisRun', 'modDirectory', 'assemblyName', 'assemblyFile', 'staticID',
        'dllSha256', 'zipFile', 'zipSha256', 'zipSha256Source'
    )
    $embedded = $embeddedBuildInfoText | ConvertFrom-Json

    $staticIdMatches = [regex]::Matches($modYaml, '(?m)^staticID:\s*(\S+)\s*$')
    $titleMatches = [regex]::Matches($modYaml, '(?m)^title:\s*(.+?)\s*$')
    $versionMatches = [regex]::Matches($modInfoYaml, '(?m)^version:\s*(\S+)\s*$')
    if ($staticIdMatches.Count -ne 1 -or
        $staticIdMatches[0].Groups[1].Value -cne 'local.arknights_operators_testing') {
        throw 'Testing package must contain exactly one Testing staticID.'
    }
    if ($titleMatches.Count -ne 1 -or
        -not $titleMatches[0].Groups[1].Value.StartsWith('Arknights Operators', [StringComparison]::Ordinal)) {
        throw 'Testing package must contain exactly one Arknights Operators title.'
    }
    if ($versionMatches.Count -ne 1) {
        throw 'Testing package must contain exactly one version.'
    }

    $channel = [string]$embedded.channel
    $expectedSuffix = switch ($channel) {
        'Dev' { '[DEV]' }
        'RC' { '[RC]' }
        default { throw "Testing package has an invalid channel: $channel" }
    }
    $manifestVersion = $versionMatches[0].Groups[1].Value
    if (-not $titleMatches[0].Groups[1].Value.EndsWith(" $expectedSuffix", [StringComparison]::Ordinal) -or
        $embedded.version -cne $manifestVersion) {
        throw 'Testing title, channel, and manifest version do not agree.'
    }
    if (($channel -eq 'Dev' -and
        $manifestVersion -notmatch '^\d+\.\d+\.\d+-dev(?:\.[0-9A-Za-z-]+)+$') -or
        ($channel -eq 'RC' -and
        $manifestVersion -notmatch '^\d+\.\d+\.\d+-rc\.\d+(?:\.local-dirty)?$')) {
        throw 'Testing package version does not match its Dev or RC channel.'
    }

    foreach ($property in @('schemaVersion', 'version', 'channel', 'sourceSha', 'sourceShortSha',
        'sourceCommitTimeUtc', 'branch', 'dirty', 'localDirty', 'eligibleForUpload',
        'compiledInThisRun', 'modDirectory', 'assemblyName', 'assemblyFile', 'staticID',
        'dllSha256', 'zipFile')) {
        if ($sidecar.$property -cne $embedded.$property) {
            throw "Testing sidecar and embedded build-info disagree on $property."
        }
    }
    if ($embedded.modDirectory -cne $testingRootName -or
        $embedded.schemaVersion -ne 1 -or
        $embedded.assemblyName -cne 'ArknightsOperatorsTesting' -or
        $embedded.assemblyFile -cne 'ArknightsOperatorsTesting.dll' -or
        $embedded.staticID -cne 'local.arknights_operators_testing' -or
        $embedded.dllSha256 -cne $dllHash -or
        $null -ne $embedded.zipSha256 -or
        $embedded.zipSha256Source -cne $sidecarName -or
        $sidecar.embeddedBuildInfoSha256 -cne $embeddedBuildInfoHash) {
        throw 'Testing build-info identity or hash contract is invalid.'
    }
    foreach ($booleanProperty in @('dirty', 'localDirty', 'eligibleForUpload', 'compiledInThisRun')) {
        if ($embedded.$booleanProperty -isnot [bool]) {
            throw "Testing build-info property $booleanProperty must be a Boolean."
        }
    }
    if ($embedded.dirty -ne $embedded.localDirty) {
        throw 'Testing build-info dirty and localDirty flags must agree.'
    }
    $hasLocalDirtyVersion = $manifestVersion -match '(?:^|[.-])local-dirty(?:$|\.)'
    if ($embedded.dirty -ne $hasLocalDirtyVersion) {
        throw 'Testing package dirty state must agree with its local-dirty version marker.'
    }
    if ($embedded.eligibleForUpload -and
        ($embedded.dirty -or -not $embedded.compiledInThisRun)) {
        throw 'An upload-eligible Testing package must be clean and compiled in this run.'
    }

    if ((Get-ManagedAssemblyNameFromBytes -Bytes $dllBytes) -cne 'ArknightsOperatorsTesting') {
        throw 'Testing DLL has the wrong managed assembly identity.'
    }

    if (-not $PSCmdlet.ShouldProcess($destination, "Install $testingRootName from $package")) {
        Write-Output "Validated Testing package bytes and assembly identity for: $destination"
        return
    }

    New-Item -ItemType Directory -Path $localRoot -Force | Out-Null
    $orphanBackups = @(Get-ChildItem -LiteralPath $localRoot -Directory -Force |
        Where-Object { $_.Name -like ".$testingRootName.previous.*" })
    if ($orphanBackups.Count -gt 0) {
        if ($orphanBackups.Count -eq 1 -and -not (Test-Path -LiteralPath $destination)) {
            $safeOrphan = Assert-PathWithin -Path $orphanBackups[0].FullName -Parent $localRoot
            Move-Item -LiteralPath $safeOrphan -Destination $destination
            Write-Output "Recovered the previous Testing Mod before installing the new package."
        } else {
            throw 'Testing install found ambiguous previous backups; resolve them before installing.'
        }
    }
    $operationId = [Guid]::NewGuid().ToString('N')
    $stagingContainer = Assert-PathWithin -Path (
        Join-Path $localRoot ".$testingRootName.staging.$operationId"
    ) -Parent $localRoot
    $stagedPayload = Join-Path $stagingContainer $testingRootName
    $backup = Assert-PathWithin -Path (
        Join-Path $localRoot ".$testingRootName.previous.$operationId"
    ) -Parent $localRoot
    $previousMoved = $false

    try {
        New-Item -ItemType Directory -Path $stagedPayload -Force | Out-Null
        foreach ($entry in $fileEntries) {
            $relativePath = $entry.FullName.Substring($testingPrefix.Length).Replace(
                '/',
                [System.IO.Path]::DirectorySeparatorChar
            )
            $stagedFile = Assert-PathWithin -Path (Join-Path $stagedPayload $relativePath) `
                -Parent $stagedPayload
            $stagedParent = [System.IO.Path]::GetDirectoryName($stagedFile)
            New-Item -ItemType Directory -Path $stagedParent -Force | Out-Null
            $input = $entry.Open()
            $output = [System.IO.File]::Open(
                $stagedFile,
                [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::None
            )
            try {
                $input.CopyTo($output)
            } finally {
                $output.Dispose()
                $input.Dispose()
            }
        }

        $stagedDll = Join-Path $stagedPayload 'ArknightsOperatorsTesting.dll'
        $stagedAssemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($stagedDll).Name
        if ($stagedAssemblyName -cne 'ArknightsOperatorsTesting' -or
            (Get-FileHash -LiteralPath $stagedDll -Algorithm SHA256).Hash -cne $dllHash) {
            throw 'Extracted Testing DLL identity or SHA-256 changed during extraction.'
        }

        if (Test-Path -LiteralPath $destination) {
            Move-Item -LiteralPath $destination -Destination $backup
            $previousMoved = $true
        }
        Move-Item -LiteralPath $stagedPayload -Destination $destination
    } catch {
        if ($previousMoved -and -not (Test-Path -LiteralPath $destination) -and
            (Test-Path -LiteralPath $backup)) {
            Move-Item -LiteralPath $backup -Destination $destination
            $previousMoved = $false
        }
        throw
    } finally {
        if (Test-Path -LiteralPath $stagingContainer) {
            try {
                Remove-Item -LiteralPath $stagingContainer -Recurse -Force
            } catch {
                Write-Warning "Testing staging cleanup failed at $stagingContainer"
            }
        }
    }

    if ($previousMoved -and (Test-Path -LiteralPath $backup)) {
        try {
            Remove-Item -LiteralPath $backup -Recurse -Force
        } catch {
            Write-Warning "Testing Mod was installed, but the previous backup remains at $backup"
        }
    }

    Write-Output "Installed Testing Mod to: $destination"
} finally {
    $archive.Dispose()
    $zipStream.Dispose()
}
