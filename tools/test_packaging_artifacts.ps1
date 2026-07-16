[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$builder = Join-Path $PSScriptRoot 'build_mod_release.ps1'
$installer = Join-Path $PSScriptRoot 'install_testing_mod.ps1'
$cacheBase = [System.IO.Path]::GetFullPath((Join-Path $repoRoot '.cache'))
$probeRoot = Join-Path $cacheBase ('packaging-artifact-probe-' + [Guid]::NewGuid().ToString('N'))

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
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

function Assert-FileHashes {
    param([Parameter(Mandatory = $true)][hashtable]$ExpectedHashes)

    foreach ($path in $ExpectedHashes.Keys) {
        Assert-True (Test-Path -LiteralPath $path -PathType Leaf) `
            "Sentinel file was removed: $path"
        Assert-True ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq
            $ExpectedHashes[$path]) "Sentinel file was changed: $path"
    }
}

function Invoke-GitText {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = @(& git -C $repoRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }
    return ($output -join "`n").Trim()
}

function Get-DirectoryFileHashes {
    param([Parameter(Mandatory = $true)][string]$Root)

    $fullRoot = [System.IO.Path]::GetFullPath($Root)
    $rootPrefix = $fullRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $hashes = [ordered]@{}
    foreach ($file in (Get-ChildItem -LiteralPath $fullRoot -Recurse -File | Sort-Object FullName)) {
        $relativePath = $file.FullName.Substring($rootPrefix.Length).Replace('\', '/')
        $hashes[$relativePath] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return $hashes
}

function Assert-DirectoryFileHashes {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$ExpectedHashes,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Assert-True (Test-Path -LiteralPath $Root -PathType Container) "$Label directory is missing."
    $actualHashes = Get-DirectoryFileHashes -Root $Root
    $difference = @(Compare-Object -ReferenceObject @($ExpectedHashes.Keys | Sort-Object) `
        -DifferenceObject @($actualHashes.Keys | Sort-Object) -CaseSensitive)
    Assert-True ($difference.Count -eq 0) "$Label file whitelist changed."
    foreach ($relativePath in $ExpectedHashes.Keys) {
        Assert-True ($actualHashes[$relativePath] -ceq $ExpectedHashes[$relativePath]) `
            "$Label file hash changed: $relativePath"
    }
}

function Read-ZipEntryBytes {
    param([Parameter(Mandatory = $true)]$Entry)

    $input = $Entry.Open()
    $output = [System.IO.MemoryStream]::new()
    try {
        $input.CopyTo($output)
        Write-Output -NoEnumerate ([byte[]]$output.ToArray())
    } finally {
        $output.Dispose()
        $input.Dispose()
    }
}

function Invoke-LocalPackageBuild {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('Stable', 'Dev', 'RC')][string]$Channel,
        [string]$Version,
        [switch]$Nightly
    )

    $parameters = @{
        Channel = $Channel
        SkipCompile = $true
        CacheRoot = $probeRoot
    }
    if ($Nightly) {
        $parameters.Nightly = $true
    } else {
        $parameters.Version = $Version
    }
    $output = @(& $builder @parameters)
    $zipLines = @($output | Where-Object { $_ -like 'Package ZIP:*' })
    $stageLines = @($output | Where-Object { $_ -like 'Package directory:*' })
    if ($zipLines.Count -ne 1 -or $stageLines.Count -ne 1) {
        throw "Could not resolve the $Channel package paths from builder output."
    }
    $zipPath = [System.IO.Path]::GetFullPath(($zipLines[0] -replace '^Package ZIP:\s*', ''))
    $stagePath = [System.IO.Path]::GetFullPath(($stageLines[0] -replace '^Package directory:\s*', ''))
    $probePrefix = [System.IO.Path]::GetFullPath($probeRoot).TrimEnd('\', '/') +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $zipPath.StartsWith($probePrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $stagePath.StartsWith($probePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Channel builder output escaped the unique artifact probe root."
    }
    return [pscustomobject][ordered]@{
        Channel = $Channel
        ZipPath = $zipPath
        StagePath = $stagePath
        Output = $output
    }
}

function Test-PackagedArtifact {
    param([Parameter(Mandatory = $true)]$Build)

    $identity = switch ($Build.Channel) {
        'Stable' {
            [pscustomobject]@{
                Root = 'ArknightsOperatorsMod'
                Assembly = 'ArknightsOperatorsMod'
                Dll = 'ArknightsOperatorsMod.dll'
                StaticId = 'local.arknights_amiya_duplicant'
                Suffix = ''
            }
        }
        'Dev' {
            [pscustomobject]@{
                Root = 'ArknightsOperatorsMod.Testing'
                Assembly = 'ArknightsOperatorsTesting'
                Dll = 'ArknightsOperatorsTesting.dll'
                StaticId = 'local.arknights_operators_testing'
                Suffix = '[DEV]'
            }
        }
        'RC' {
            [pscustomobject]@{
                Root = 'ArknightsOperatorsMod.Testing'
                Assembly = 'ArknightsOperatorsTesting'
                Dll = 'ArknightsOperatorsTesting.dll'
                StaticId = 'local.arknights_operators_testing'
                Suffix = '[RC]'
            }
        }
    }

    Assert-True (Test-Path -LiteralPath $Build.ZipPath -PathType Leaf) `
        "$($Build.Channel) ZIP is missing."
    $releaseRoot = [System.IO.Path]::GetDirectoryName($Build.ZipPath)
    $sidecarPath = Join-Path $releaseRoot (
        [System.IO.Path]::GetFileNameWithoutExtension($Build.ZipPath) + '.build-info.json'
    )
    Assert-True (Test-Path -LiteralPath $sidecarPath -PathType Leaf) `
        "$($Build.Channel) build-info sidecar is missing."
    $sidecar = Get-Content -Raw -Encoding UTF8 -LiteralPath $sidecarPath | ConvertFrom-Json

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Build.ZipPath)
    try {
        $expectedEntries = @(
            "$($identity.Root)/$($identity.Dll)",
            "$($identity.Root)/DATA_NOTICE.md",
            "$($identity.Root)/PLIB-LICENSE.txt",
            "$($identity.Root)/PLIB-SOURCE.txt",
            "$($identity.Root)/SPINE-RUNTIME-LICENSE.txt",
            "$($identity.Root)/THIRD_PARTY_NOTICES.md",
            "$($identity.Root)/assets/catalog/operator_appearances_20260604.json",
            "$($identity.Root)/build-info.json",
            "$($identity.Root)/mod.yaml",
            "$($identity.Root)/mod_info.yaml",
            "$($identity.Root)/PLib.dll",
            "$($identity.Root)/SPINE-RUNTIME-SOURCE.txt"
        ) | Sort-Object
        $fileEntries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
        Assert-True ($archive.Entries.Count -eq 12 -and $fileEntries.Count -eq 12) `
            "$($Build.Channel) ZIP must contain exactly 12 files and no directory entries."
        $difference = @(Compare-Object -ReferenceObject $expectedEntries `
            -DifferenceObject @($fileEntries.FullName | Sort-Object) -CaseSensitive)
        Assert-True ($difference.Count -eq 0) "$($Build.Channel) ZIP whitelist mismatch."

        $packageFileHashes = [ordered]@{}
        foreach ($entry in $fileEntries) {
            $relativePath = $entry.FullName.Substring($identity.Root.Length + 1)
            [byte[]]$entryBytes = Read-ZipEntryBytes -Entry $entry
            $packageFileHashes[$relativePath] = Get-Sha256Hex -Bytes $entryBytes
        }

        $dllEntry = $archive.GetEntry("$($identity.Root)/$($identity.Dll)")
        $embeddedEntry = $archive.GetEntry("$($identity.Root)/build-info.json")
        $modYamlEntry = $archive.GetEntry("$($identity.Root)/mod.yaml")
        $modInfoEntry = $archive.GetEntry("$($identity.Root)/mod_info.yaml")
        [byte[]]$dllBytes = Read-ZipEntryBytes -Entry $dllEntry
        [byte[]]$embeddedBytes = Read-ZipEntryBytes -Entry $embeddedEntry
        $embedded = ([System.Text.Encoding]::UTF8.GetString($embeddedBytes)) | ConvertFrom-Json
        $modYaml = [System.Text.Encoding]::UTF8.GetString((Read-ZipEntryBytes -Entry $modYamlEntry))
        $modInfo = [System.Text.Encoding]::UTF8.GetString((Read-ZipEntryBytes -Entry $modInfoEntry))

        Assert-True ($sidecar.schemaVersion -eq 1 -and $embedded.schemaVersion -eq 1) `
            "$($Build.Channel) build-info schema is invalid."
        foreach ($field in @('version', 'channel', 'sourceSha', 'sourceShortSha',
            'sourceCommitTimeUtc', 'branch', 'dirty', 'localDirty', 'eligibleForUpload',
            'compiledInThisRun', 'modDirectory', 'assemblyName', 'assemblyFile', 'staticID',
            'dllSha256', 'zipFile')) {
            Assert-True ($sidecar.$field -ceq $embedded.$field) `
                "$($Build.Channel) build-info records disagree on $field."
        }
        Assert-True ($sidecar.channel -ceq $Build.Channel) `
            "$($Build.Channel) sidecar channel is invalid."
        Assert-True ($sidecar.sourceSha -ceq $gitTruth.SourceSha -and
            $sidecar.sourceShortSha -ceq $gitTruth.SourceShortSha -and
            $sidecar.sourceCommitTimeUtc -ceq $gitTruth.SourceCommitTimeUtc -and
            $sidecar.branch -ceq $gitTruth.Branch -and
            $sidecar.dirty -eq $gitTruth.Dirty) `
            "$($Build.Channel) build-info does not match the current Git source state."
        Assert-True ($sidecar.modDirectory -ceq $identity.Root -and
            $sidecar.assemblyName -ceq $identity.Assembly -and
            $sidecar.assemblyFile -ceq $identity.Dll -and
            $sidecar.staticID -ceq $identity.StaticId) `
            "$($Build.Channel) sidecar identity is invalid."
        Assert-True ($sidecar.compiledInThisRun -eq $false -and
            $sidecar.eligibleForUpload -eq $false) `
            "$($Build.Channel) SkipCompile package was incorrectly marked as promotable."
        foreach ($booleanField in @('dirty', 'localDirty', 'eligibleForUpload', 'compiledInThisRun')) {
            Assert-True ($sidecar.$booleanField -is [bool] -and
                $embedded.$booleanField -is [bool]) `
                "$($Build.Channel) build-info field $booleanField is not Boolean."
        }
        Assert-True ($sidecar.dirty -eq $sidecar.localDirty) `
            "$($Build.Channel) dirty flags disagree."
        $hasLocalDirtyVersion = $sidecar.version -match '(?:^|[.-])local-dirty(?:$|\.)'
        Assert-True ($sidecar.dirty -eq $hasLocalDirtyVersion) `
            "$($Build.Channel) dirty state and version marker disagree."
        Assert-True ($sidecar.zipFile -ceq [System.IO.Path]::GetFileName($Build.ZipPath) -and
            $sidecar.zipSha256 -ceq (Get-FileHash -LiteralPath $Build.ZipPath -Algorithm SHA256).Hash) `
            "$($Build.Channel) ZIP hash contract is invalid."
        Assert-True ($sidecar.dllSha256 -ceq (Get-Sha256Hex -Bytes $dllBytes) -and
            $sidecar.embeddedBuildInfoSha256 -ceq (Get-Sha256Hex -Bytes $embeddedBytes)) `
            "$($Build.Channel) DLL or embedded build-info hash is invalid."
        Assert-True ($null -eq $embedded.zipSha256 -and
            $embedded.zipSha256Source -ceq [System.IO.Path]::GetFileName($sidecarPath)) `
            "$($Build.Channel) embedded ZIP hash reference is invalid."

        $staticIdMatches = [regex]::Matches($modYaml, '(?m)^staticID:\s*(\S+)\s*$')
        $titleMatches = [regex]::Matches($modYaml, '(?m)^title:\s*(.+?)\s*$')
        $versionMatches = [regex]::Matches($modInfo, '(?m)^version:\s*(\S+)\s*$')
        Assert-True ($staticIdMatches.Count -eq 1 -and
            $staticIdMatches[0].Groups[1].Value -ceq $identity.StaticId) `
            "$($Build.Channel) staged staticID is not unique and correct."
        Assert-True ($titleMatches.Count -eq 1 -and $versionMatches.Count -eq 1 -and
            $versionMatches[0].Groups[1].Value -ceq $sidecar.version) `
            "$($Build.Channel) staged title or version is not unique and correct."
        if ($Build.Channel -eq 'Stable') {
            Assert-True ($modYaml -notmatch '(?i)alpha') 'Stable manifest still contains Alpha text.'
        } else {
            Assert-True ($titleMatches[0].Groups[1].Value.EndsWith(
                " $($identity.Suffix)",
                [System.StringComparison]::Ordinal
            )) "$($Build.Channel) title suffix is missing."
        }

        $stagedDll = Join-Path $Build.StagePath $identity.Dll
        Assert-True (([System.Reflection.AssemblyName]::GetAssemblyName($stagedDll).Name) -ceq
            $identity.Assembly) "$($Build.Channel) managed assembly identity is invalid."
        Assert-True ((Get-FileHash -LiteralPath $stagedDll -Algorithm SHA256).Hash -ceq
            $sidecar.dllSha256) "$($Build.Channel) staged DLL differs from the ZIP DLL."
    } finally {
        $archive.Dispose()
    }

    return [pscustomobject][ordered]@{
        Build = $Build
        SidecarPath = $sidecarPath
        Sidecar = $sidecar
        FileHashes = $packageFileHashes
    }
}

function Assert-TestingInstallFlow {
    param(
        [Parameter(Mandatory = $true)]$TestingArtifact,
        [Parameter(Mandatory = $true)]$RcArtifact,
        [Parameter(Mandatory = $true)]$StableArtifact
    )

    $localModsRoot = Join-Path $probeRoot 'local-mods'
    $target = Join-Path $localModsRoot 'ArknightsOperatorsMod.Testing'
    $sentinelHashes = @{}
    foreach ($sentinel in @(
        @{ Directory = 'ArknightsOperatorsMod'; File = 'stable.keep'; Text = 'stable' },
        @{ Directory = 'AmiyaDuplicantMod'; File = 'amiya.keep'; Text = 'amiya' },
        @{ Directory = 'UnrelatedLocalMod'; File = 'other.keep'; Text = 'other' }
    )) {
        $sentinelDirectory = Join-Path $localModsRoot $sentinel.Directory
        New-Item -ItemType Directory -Path $sentinelDirectory -Force | Out-Null
        $sentinelPath = Join-Path $sentinelDirectory $sentinel.File
        [System.IO.File]::WriteAllText(
            $sentinelPath,
            $sentinel.Text,
            [System.Text.UTF8Encoding]::new($false)
        )
        $sentinelHashes[$sentinelPath] = (
            Get-FileHash -LiteralPath $sentinelPath -Algorithm SHA256
        ).Hash
    }
    & $installer -PackagePath $TestingArtifact.Build.ZipPath `
        -LocalModsRoot $localModsRoot -WhatIf | Out-Null
    Assert-True (-not (Test-Path -LiteralPath $target)) `
        'Testing installer -WhatIf created the Testing target.'
    Assert-FileHashes -ExpectedHashes $sentinelHashes
    & $installer -PackagePath $RcArtifact.Build.ZipPath `
        -LocalModsRoot $localModsRoot -WhatIf | Out-Null
    Assert-True (-not (Test-Path -LiteralPath $target)) `
        'RC installer -WhatIf created the Testing target.'
    Assert-FileHashes -ExpectedHashes $sentinelHashes

    & $installer -PackagePath $TestingArtifact.Build.ZipPath `
        -LocalModsRoot $localModsRoot | Out-Null
    Assert-DirectoryFileHashes -Root $target -ExpectedHashes $TestingArtifact.FileHashes `
        -Label 'Fresh Testing install'
    Assert-FileHashes -ExpectedHashes $sentinelHashes

    $installedSnapshot = Get-DirectoryFileHashes -Root $target
    & $installer -PackagePath $TestingArtifact.Build.ZipPath `
        -LocalModsRoot $localModsRoot -WhatIf | Out-Null
    Assert-DirectoryFileHashes -Root $target -ExpectedHashes $installedSnapshot `
        -Label 'Existing Testing install after Dev WhatIf'
    & $installer -PackagePath $RcArtifact.Build.ZipPath `
        -LocalModsRoot $localModsRoot -WhatIf | Out-Null
    Assert-DirectoryFileHashes -Root $target -ExpectedHashes $installedSnapshot `
        -Label 'Existing Testing install after RC WhatIf'
    Assert-FileHashes -ExpectedHashes $sentinelHashes

    & $installer -PackagePath $TestingArtifact.Build.ZipPath `
        -LocalModsRoot $localModsRoot | Out-Null
    Assert-DirectoryFileHashes -Root $target -ExpectedHashes $TestingArtifact.FileHashes `
        -Label 'Replacement Testing install'
    Assert-FileHashes -ExpectedHashes $sentinelHashes
    $orphan = Join-Path $localModsRoot (
        '.ArknightsOperatorsMod.Testing.previous.' + [Guid]::NewGuid().ToString('N')
    )
    Move-Item -LiteralPath $target -Destination $orphan
    & $installer -PackagePath $TestingArtifact.Build.ZipPath `
        -LocalModsRoot $localModsRoot | Out-Null
    Assert-True (Test-Path -LiteralPath $target -PathType Container) `
        'Testing installer did not recover and replace an orphan backup.'
    Assert-DirectoryFileHashes -Root $target -ExpectedHashes $TestingArtifact.FileHashes `
        -Label 'Recovered Testing install'
    $backups = @(Get-ChildItem -LiteralPath $localModsRoot -Directory -Force |
        Where-Object { $_.Name -like '.ArknightsOperatorsMod.Testing.previous.*' })
    Assert-True ($backups.Count -eq 0) 'Testing installer left a previous backup after success.'
    Assert-FileHashes -ExpectedHashes $sentinelHashes

    $stableRejected = $false
    try {
        & $installer -PackagePath $StableArtifact.Build.ZipPath `
            -LocalModsRoot $localModsRoot -WhatIf | Out-Null
    } catch {
        $stableRejected = $_.Exception.Message -like '*outside ArknightsOperatorsMod.Testing*'
    }
    Assert-True $stableRejected 'Testing installer did not reject a Stable package.'
    Assert-DirectoryFileHashes -Root $target -ExpectedHashes $TestingArtifact.FileHashes `
        -Label 'Testing install after Stable rejection'
    Assert-FileHashes -ExpectedHashes $sentinelHashes
}

function Assert-NightlyRetention {
    param([Parameter(Mandatory = $true)][string]$SourceShortSha)

    $nightlyRoot = Join-Path $probeRoot 'nightly'
    New-Item -ItemType Directory -Path $nightlyRoot -Force | Out-Null
    $seedDirectories = @()
    for ($index = 1; $index -le 4; $index++) {
        $date = '2026010' + $index
        $seed = Invoke-LocalPackageBuild -Channel Dev `
            -Version "v0.3.3-dev.$date.$SourceShortSha"
        $seedRoot = [System.IO.Path]::GetDirectoryName($seed.StagePath)
        $destination = Join-Path $nightlyRoot ([System.IO.Path]::GetFileName($seedRoot))
        Move-Item -LiteralPath $seedRoot -Destination $destination
        (Get-Item -LiteralPath $destination).LastWriteTimeUtc = [DateTime]::new(
            2020,
            1,
            $index,
            0,
            0,
            0,
            [DateTimeKind]::Utc
        )
        $seedDirectories += $destination
    }

    $invalidNightly = Join-Path $nightlyRoot "v0.3.3-dev.20250101.$SourceShortSha"
    New-Item -ItemType Directory -Path $invalidNightly | Out-Null
    Set-Content -LiteralPath (Join-Path $invalidNightly 'keep.txt') -Value 'incomplete sentinel'
    $unrelated = Join-Path $nightlyRoot 'do-not-remove'
    New-Item -ItemType Directory -Path $unrelated | Out-Null
    Set-Content -LiteralPath (Join-Path $unrelated 'keep.txt') -Value 'unrelated sentinel'
    $outsideSentinel = Join-Path $probeRoot 'outside-nightly.keep'
    Set-Content -LiteralPath $outsideSentinel -Value 'outside sentinel'
    $outsideDirectory = Join-Path $probeRoot 'outside-nightly-directory'
    New-Item -ItemType Directory -Path $outsideDirectory | Out-Null
    $outsideDirectorySentinel = Join-Path $outsideDirectory 'keep.txt'
    Set-Content -LiteralPath $outsideDirectorySentinel -Value 'outside directory sentinel'
    $outsideDirectoryHash = (Get-FileHash -LiteralPath $outsideDirectorySentinel `
        -Algorithm SHA256).Hash
    $releaseSibling = Join-Path (Join-Path $probeRoot 'release') 'retention-sibling.keep'
    Set-Content -LiteralPath $releaseSibling -Value 'release sibling sentinel'
    $releaseSiblingHash = (Get-FileHash -LiteralPath $releaseSibling -Algorithm SHA256).Hash

    $current = Invoke-LocalPackageBuild -Channel Dev -Nightly
    $currentRoot = [System.IO.Path]::GetDirectoryName($current.StagePath)
    $expectedKept = @($seedDirectories[2], $seedDirectories[3], $currentRoot)
    $expectedRemoved = @($seedDirectories[0], $seedDirectories[1])
    foreach ($path in $expectedKept) {
        Assert-True (Test-Path -LiteralPath $path -PathType Container) `
            "Nightly retention removed a package that should remain: $path"
    }
    foreach ($path in $expectedRemoved) {
        Assert-True (-not (Test-Path -LiteralPath $path)) `
            "Nightly retention kept a package beyond the three-package limit: $path"
    }
    Assert-True (Test-Path -LiteralPath $invalidNightly -PathType Container) `
        'Nightly retention removed an incomplete sentinel directory.'
    Assert-True (Test-Path -LiteralPath $unrelated -PathType Container) `
        'Nightly retention removed an unrelated directory.'
    Assert-True (Test-Path -LiteralPath $outsideSentinel -PathType Leaf) `
        'Nightly retention changed a file outside the Nightly root.'
    Assert-True (Test-Path -LiteralPath $outsideDirectorySentinel -PathType Leaf) `
        'Nightly retention removed a sibling directory.'
    Assert-True ((Get-FileHash -LiteralPath $outsideDirectorySentinel -Algorithm SHA256).Hash `
        -ceq $outsideDirectoryHash) 'Nightly retention changed a sibling directory.'
    Assert-True (Test-Path -LiteralPath $releaseSibling -PathType Leaf) `
        'Nightly retention removed the release sibling.'
    Assert-True ((Get-FileHash -LiteralPath $releaseSibling -Algorithm SHA256).Hash `
        -ceq $releaseSiblingHash) 'Nightly retention changed the release sibling.'

    $completeNightlies = @()
    foreach ($directory in (Get-ChildItem -LiteralPath $nightlyRoot -Directory)) {
        if ($directory.Name -notmatch '^v\d+\.\d+\.\d+-dev\.\d{8}\.[0-9a-f]{7}(?:\.local-dirty)?$') {
            continue
        }
        $zipName = "arknights-oni-$($directory.Name).zip"
        $sidecarName = "arknights-oni-$($directory.Name).build-info.json"
        $zipPath = Join-Path $directory.FullName $zipName
        $sidecarPath = Join-Path $directory.FullName $sidecarName
        $embeddedPath = Join-Path (Join-Path $directory.FullName `
            'ArknightsOperatorsMod.Testing') 'build-info.json'
        if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $sidecarPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $embeddedPath -PathType Leaf)) {
            continue
        }
        try {
            $metadata = Get-Content -Raw -Encoding UTF8 -LiteralPath $sidecarPath | ConvertFrom-Json
            if ($metadata.channel -ceq 'Dev' -and
                $metadata.zipFile -ceq $zipName -and
                $metadata.zipSha256 -ceq (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash -and
                $metadata.embeddedBuildInfoSha256 -ceq
                    (Get-FileHash -LiteralPath $embeddedPath -Algorithm SHA256).Hash) {
                $completeNightlies += $directory.FullName
            }
        } catch {
            continue
        }
    }
    Assert-True ($completeNightlies.Count -eq 3) `
        "Nightly retention left $($completeNightlies.Count) complete packages instead of 3."
}

$cachePrefix = $cacheBase.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
if (-not $probeRoot.StartsWith($cachePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Packaging artifact probe root escaped the repository cache.'
}

New-Item -ItemType Directory -Path $probeRoot -Force | Out-Null
$sourceCommitTime = Invoke-GitText -Arguments @('show', '-s', '--format=%cI', 'HEAD')
$branch = Invoke-GitText -Arguments @('branch', '--show-current')
if ([string]::IsNullOrWhiteSpace($branch)) {
    $branch = '(detached)'
}
$gitStatus = Invoke-GitText -Arguments @('status', '--porcelain=v1', '--untracked-files=normal')
$gitTruth = [pscustomobject][ordered]@{
    SourceSha = Invoke-GitText -Arguments @('rev-parse', 'HEAD')
    SourceShortSha = Invoke-GitText -Arguments @('rev-parse', '--short=7', 'HEAD')
    SourceCommitTimeUtc = [System.DateTimeOffset]::Parse(
        $sourceCommitTime,
        [System.Globalization.CultureInfo]::InvariantCulture
    ).ToUniversalTime().ToString('o')
    Branch = $branch
    Dirty = -not [string]::IsNullOrWhiteSpace($gitStatus)
}
try {
    $stableBuild = Invoke-LocalPackageBuild -Channel Stable -Version 'v0.3.3'
    $stable = Test-PackagedArtifact -Build $stableBuild
    $devBuild = Invoke-LocalPackageBuild -Channel Dev `
        -Version 'v0.3.3-dev.20260716.abcdef0'
    $dev = Test-PackagedArtifact -Build $devBuild
    $rcBuild = Invoke-LocalPackageBuild -Channel RC -Version 'v0.3.3-rc.1'
    $rc = Test-PackagedArtifact -Build $rcBuild

    Assert-TestingInstallFlow -TestingArtifact $dev -RcArtifact $rc -StableArtifact $stable
    Assert-NightlyRetention -SourceShortSha $dev.Sidecar.sourceShortSha

    Write-Output 'PackagingArtifactTest: Stable/Dev/RC artifacts, isolated Testing install, and Nightly retention passed.'
} finally {
    if (Test-Path -LiteralPath $probeRoot) {
        Remove-Item -LiteralPath $probeRoot -Recurse -Force
    }
}
