[CmdletBinding()]
param(
    [ValidateSet('Stable', 'Dev', 'RC')]
    [string]$Channel = 'Stable',
    [string]$Version,
    [switch]$Nightly,
    [switch]$SkipCompile,
    [switch]$IdentityProbe,
    [string]$CacheRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modRoot = Join-Path (Join-Path $repoRoot 'arknights_oni_mod_work') 'ArknightsOperatorsMod'
$baseModYaml = Join-Path $modRoot 'mod.yaml'
$baseModInfoYaml = Join-Path $modRoot 'mod_info.yaml'

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    [System.IO.File]::WriteAllText(
        $Path,
        $Content,
        [System.Text.UTF8Encoding]::new($false)
    )
}

function Assert-PathWithin {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Parent
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escaped its allowed parent: $fullPath"
    }
    return $fullPath
}

function Invoke-GitText {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = @(& git -C $repoRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }
    return ($output -join "`n").Trim()
}

function Get-BuildSourceState {
    $sourceRoot = 'arknights_oni_mod_work/ArknightsOperatorsMod'
    $listed = Invoke-GitText -Arguments @(
        'ls-files', '--', "$sourceRoot/src", "$sourceRoot/lib/spine-csharp-src"
    )
    $sourcePaths = @($listed -split "`n" | Where-Object { $_ -match '\.cs$' } | Sort-Object)
    if ($sourcePaths.Count -eq 0) {
        throw 'Git did not report any tracked C# sources for the Mod build.'
    }

    $fingerprintLines = foreach ($sourcePath in $sourcePaths) {
        if ($sourcePath.Contains("`r") -or $sourcePath.Contains("`n")) {
            throw 'A tracked C# source path contains a newline and cannot be packaged safely.'
        }
        $workingPath = Join-Path $repoRoot $sourcePath.Replace(
            '/',
            [System.IO.Path]::DirectorySeparatorChar
        )
        if (-not (Test-Path -LiteralPath $workingPath -PathType Leaf)) {
            throw "Tracked build source is missing from the working tree: $sourcePath"
        }
        $workingSha256 = (Get-FileHash -LiteralPath $workingPath -Algorithm SHA256).Hash
        "$sourcePath=$workingSha256"
    }

    $workingBlobs = @($sourcePaths |
        & git -C $repoRoot hash-object --no-filters --stdin-paths 2>&1)
    if ($LASTEXITCODE -ne 0 -or $workingBlobs.Count -ne $sourcePaths.Count) {
        throw "git hash-object failed for the tracked build source set: $($workingBlobs -join ' ')"
    }
    $headSpecs = @($sourcePaths | ForEach-Object { "HEAD:$_" })
    $headBlobs = @(& git -C $repoRoot rev-parse @headSpecs 2>&1)
    $matchesHead = $LASTEXITCODE -eq 0 -and $headBlobs.Count -eq $workingBlobs.Count
    if ($matchesHead) {
        for ($index = 0; $index -lt $workingBlobs.Count; $index++) {
            if (([string]$workingBlobs[$index]) -cne ([string]$headBlobs[$index])) {
                $matchesHead = $false
                break
            }
        }
    }
    return [pscustomobject][ordered]@{
        Count = $sourcePaths.Count
        MatchesHead = $matchesHead
        Fingerprint = ($fingerprintLines -join "`n")
    }
}

function Get-CompleteNightlyDirectories {
    param([Parameter(Mandatory = $true)][string]$NightlyRoot)

    if (-not (Test-Path -LiteralPath $NightlyRoot -PathType Container)) {
        return
    }
    foreach ($directory in (Get-ChildItem -LiteralPath $NightlyRoot -Directory)) {
        $nameMatch = [regex]::Match(
            $directory.Name,
            '^v(?<version>\d+\.\d+\.\d+-dev\.\d{8}\.[0-9a-f]{7}(?:\.local-dirty)?)$'
        )
        if (-not $nameMatch.Success) {
            continue
        }
        $zipName = "arknights-oni-$($directory.Name).zip"
        $sidecarName = "arknights-oni-$($directory.Name).build-info.json"
        $zipPath = Join-Path $directory.FullName $zipName
        $sidecarPath = Join-Path $directory.FullName $sidecarName
        $embeddedPath = Join-Path (Join-Path $directory.FullName 'ArknightsOperatorsMod.Testing') `
            'build-info.json'
        if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $sidecarPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $embeddedPath -PathType Leaf)) {
            continue
        }
        try {
            $metadata = Get-Content -Raw -Encoding UTF8 -LiteralPath $sidecarPath | ConvertFrom-Json
            if ($metadata.channel -cne 'Dev' -or
                $metadata.version -cne $nameMatch.Groups['version'].Value -or
                $metadata.zipFile -cne $zipName -or
                $metadata.zipSha256 -notmatch '^[0-9A-F]{64}$' -or
                (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash -cne $metadata.zipSha256 -or
                (Get-FileHash -LiteralPath $embeddedPath -Algorithm SHA256).Hash -cne
                    $metadata.embeddedBuildInfoSha256) {
                continue
            }
        } catch {
            continue
        }
        Write-Output $directory
    }
}

function Get-ChannelIdentity {
    param([Parameter(Mandatory = $true)][string]$ChannelName)

    switch ($ChannelName) {
        'Stable' {
            return [pscustomobject][ordered]@{
                Channel = 'Stable'
                ModDirectory = 'ArknightsOperatorsMod'
                AssemblyName = 'ArknightsOperatorsMod'
                AssemblyFile = 'ArknightsOperatorsMod.dll'
                StaticId = 'local.arknights_amiya_duplicant'
                TitleSuffix = ''
            }
        }
        'Dev' {
            return [pscustomobject][ordered]@{
                Channel = 'Dev'
                ModDirectory = 'ArknightsOperatorsMod.Testing'
                AssemblyName = 'ArknightsOperatorsTesting'
                AssemblyFile = 'ArknightsOperatorsTesting.dll'
                StaticId = 'local.arknights_operators_testing'
                TitleSuffix = '[DEV]'
            }
        }
        'RC' {
            return [pscustomobject][ordered]@{
                Channel = 'RC'
                ModDirectory = 'ArknightsOperatorsMod.Testing'
                AssemblyName = 'ArknightsOperatorsTesting'
                AssemblyFile = 'ArknightsOperatorsTesting.dll'
                StaticId = 'local.arknights_operators_testing'
                TitleSuffix = '[RC]'
            }
        }
        default { throw "Unsupported channel: $ChannelName" }
    }
}

function Get-ManifestVersion {
    param(
        [Parameter(Mandatory = $true)][string]$RequestedVersion,
        [Parameter(Mandatory = $true)][string]$ChannelName
    )

    $normalized = $RequestedVersion.Trim()
    if ($normalized.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring(1)
    }
    if ($normalized -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
        throw "Version is not a safe semantic version: $RequestedVersion"
    }

    switch ($ChannelName) {
        'Stable' {
            if ($normalized -notmatch '^\d+\.\d+\.\d+$') {
                throw "Stable versions cannot contain a prerelease suffix: $RequestedVersion"
            }
        }
        'Dev' {
            if ($normalized -notmatch '^\d+\.\d+\.\d+-dev(?:\.[0-9A-Za-z-]+)+$') {
                throw "Dev versions must use a -dev.* suffix: $RequestedVersion"
            }
        }
        'RC' {
            if ($normalized -notmatch '^\d+\.\d+\.\d+-rc\.\d+$') {
                throw "RC versions must use a -rc.N suffix: $RequestedVersion"
            }
        }
    }
    return $normalized
}

function New-ModYaml {
    param(
        [Parameter(Mandatory = $true)]$Identity,
        [Parameter(Mandatory = $true)][string]$BasePath
    )

    if ($Identity.Channel -eq 'Stable') {
        [string]$content = [System.IO.File]::ReadAllText($BasePath, [System.Text.Encoding]::UTF8)
        $staticIdMatches = [System.Text.RegularExpressions.Regex]::Matches(
            $content,
            '(?m)^staticID:\s*(\S+)\s*$'
        )
        if ($staticIdMatches.Count -ne 1 -or
            $staticIdMatches[0].Groups[1].Value -cne 'local.arknights_amiya_duplicant') {
            throw 'Stable mod.yaml must contain exactly one compatibility staticID.'
        }
        $stableTitleMatch = [regex]::Match($content, '(?m)^title:\s*(.+?)\s*$')
        $stableDescriptionMatch = [regex]::Match($content, '(?m)^description:\s*"(.*)"\s*$')
        if (-not $stableTitleMatch.Success -or -not $stableDescriptionMatch.Success) {
            throw 'Stable mod.yaml must contain one title and one quoted description.'
        }
        $stableDescription = $stableDescriptionMatch.Groups[1].Value
        $stableDescription = $stableDescription -replace '\s*Alpha release\.\s*', ' '
        $stableDescription = $stableDescription -replace '\s*Alpha\s+\u6d4b\u8bd5\u7248\u672c\u3002\s*', ''
        return @"
title: $($stableTitleMatch.Groups[1].Value)
description: "$stableDescription"
staticID: $($Identity.StaticId)
"@
    }

    [string]$baseContent = [System.IO.File]::ReadAllText($BasePath, [System.Text.Encoding]::UTF8)
    $titleMatch = [System.Text.RegularExpressions.Regex]::Match(
        $baseContent,
        '(?m)^title:\s*(.+?)\s*$'
    )
    if (-not $titleMatch.Success) {
        throw 'Base mod.yaml does not contain a usable title.'
    }
    $testingTitle = $titleMatch.Groups[1].Value + ' ' + $Identity.TitleSuffix
    $description = if ($Identity.Channel -eq 'Dev') {
        'Development testing channel for Arknights Operators. Use a copied save and enable only one channel.'
    } else {
        'Release-candidate testing channel for Arknights Operators. Use a copied save and enable only one channel.'
    }
    return @"
title: $testingTitle
description: "$description"
staticID: $($Identity.StaticId)
"@
}

function New-ModInfoYaml {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestVersion,
        [Parameter(Mandatory = $true)][string]$BasePath
    )

    [string]$content = [System.IO.File]::ReadAllText($BasePath, [System.Text.Encoding]::UTF8)
    $versionMatches = [System.Text.RegularExpressions.Regex]::Matches(
        $content,
        '(?m)^version:\s*\S+\s*$'
    )
    if ($versionMatches.Count -ne 1) {
        throw 'Base mod_info.yaml must contain exactly one usable version field.'
    }
    return [System.Text.RegularExpressions.Regex]::Replace(
        $content,
        '(?m)^version:\s*\S+\s*$',
        "version: $ManifestVersion"
    )
}

foreach ($requiredInput in @($baseModYaml, $baseModInfoYaml)) {
    if (-not (Test-Path -LiteralPath $requiredInput -PathType Leaf)) {
        throw "Missing manifest input: $requiredInput"
    }
}

$Channel = switch ($Channel.ToLowerInvariant()) {
    'stable' { 'Stable' }
    'dev' { 'Dev' }
    'rc' { 'RC' }
}
$identity = Get-ChannelIdentity -ChannelName $Channel

if ($Nightly -and $Channel -ne 'Dev') {
    throw 'Nightly builds are only valid for the Dev channel.'
}
if ($Nightly -and -not [string]::IsNullOrWhiteSpace($Version)) {
    throw 'Nightly versions are generated from the date and source SHA; omit -Version.'
}

if ($IdentityProbe) {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = switch ($Channel) {
            'Stable' { 'v0.3.3' }
            'Dev' { 'v0.3.3-dev.local.0000000' }
            'RC' { 'v0.3.3-rc.1' }
        }
    }
    $probeVersion = Get-ManifestVersion -RequestedVersion $Version -ChannelName $Channel
    $probe = [pscustomobject][ordered]@{
        channel = $identity.Channel
        version = $probeVersion
        mod_directory = $identity.ModDirectory
        assembly_name = $identity.AssemblyName
        assembly_file = $identity.AssemblyFile
        static_id = $identity.StaticId
        title_suffix = $identity.TitleSuffix
        mod_yaml = New-ModYaml -Identity $identity -BasePath $baseModYaml
        mod_info_yaml = New-ModInfoYaml -ManifestVersion $probeVersion -BasePath $baseModInfoYaml
    }
    $probe | ConvertTo-Json -Depth 3
    return
}

$sourceSha = Invoke-GitText -Arguments @('rev-parse', 'HEAD')
$sourceShortSha = Invoke-GitText -Arguments @('rev-parse', '--short=7', 'HEAD')
$sourceCommitTimeText = Invoke-GitText -Arguments @('show', '-s', '--format=%cI', 'HEAD')
$sourceCommitTimeUtc = [System.DateTimeOffset]::Parse(
    $sourceCommitTimeText,
    [System.Globalization.CultureInfo]::InvariantCulture
).ToUniversalTime().ToString('o')
$branch = Invoke-GitText -Arguments @('branch', '--show-current')
if ([string]::IsNullOrWhiteSpace($branch)) {
    $branch = '(detached)'
}
$status = Invoke-GitText -Arguments @('status', '--porcelain=v1', '--untracked-files=normal')
$buildSourceState = if ($SkipCompile) { $null } else { Get-BuildSourceState }
$dirty = -not [string]::IsNullOrWhiteSpace($status) -or
    ($null -ne $buildSourceState -and -not $buildSourceState.MatchesHead)

if ($Nightly) {
    $Version = "0.3.3-dev.$((Get-Date).ToString('yyyyMMdd')).$sourceShortSha"
    if (-not $dirty -and -not $SkipCompile -and $branch -ne 'develop') {
        throw "Formal Nightly builds require the develop branch; current branch is $branch."
    }
} elseif ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = switch ($Channel) {
        'Stable' { 'v0.3.3' }
        'Dev' { "v0.3.3-dev.local.$sourceShortSha" }
        'RC' { 'v0.3.3-rc.1' }
    }
}
$requestedManifestVersion = Get-ManifestVersion -RequestedVersion $Version -ChannelName $Channel
$manifestVersion = $requestedManifestVersion
$requestedLocalDirty = $manifestVersion -match '(?:^|[.-])local-dirty(?:$|\.)'
if (-not $dirty -and $requestedLocalDirty) {
    throw 'The local-dirty version marker is reserved for builds from a dirty working tree.'
}
if ($dirty -and $manifestVersion -notmatch '(?:^|[.-])local-dirty(?:$|\.)') {
    $manifestVersion += if ($Channel -eq 'Stable') { '-local-dirty' } else { '.local-dirty' }
}
if (-not $dirty -and -not $SkipCompile -and $Channel -eq 'Stable' -and $branch -ne 'main') {
    throw "Stable packages require the main branch; current branch is $branch."
}
if (-not $dirty -and -not $SkipCompile -and $Channel -eq 'RC' -and $branch -ne 'develop') {
    throw "RC packages require the develop branch; current branch is $branch."
}
$artifactVersion = "v$manifestVersion"

$eligibleForUpload = (-not $dirty) -and (-not $SkipCompile) -and (
    ($Channel -eq 'Dev' -and $Nightly -and $branch -eq 'develop') -or
    ($Channel -eq 'RC' -and $branch -eq 'develop') -or
    ($Channel -eq 'Stable' -and $branch -eq 'main')
)

if (-not $SkipCompile) {
    $wsl = Get-Command wsl.exe -ErrorAction SilentlyContinue
    if ($null -eq $wsl) {
        throw 'wsl.exe is required to compile the Mod; use -SkipCompile only with a verified channel DLL.'
    }
    $wslModRootOutput = @(& wsl.exe --exec wslpath -a -- $modRoot 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "wslpath failed: $($wslModRootOutput -join [Environment]::NewLine)"
    }
    $wslModRoot = ($wslModRootOutput -join "`n").Trim()
    & wsl.exe --exec bash "$wslModRoot/build.sh" $Channel
    if ($LASTEXITCODE -ne 0) {
        throw "Channel compilation failed for $Channel."
    }
}

$finalSourceSha = Invoke-GitText -Arguments @('rev-parse', 'HEAD')
$finalBranch = Invoke-GitText -Arguments @('branch', '--show-current')
if ([string]::IsNullOrWhiteSpace($finalBranch)) {
    $finalBranch = '(detached)'
}
$finalStatus = Invoke-GitText -Arguments @('status', '--porcelain=v1', '--untracked-files=normal')
$finalBuildSourceState = if ($SkipCompile) { $null } else { Get-BuildSourceState }
if ($finalSourceSha -cne $sourceSha -or $finalBranch -cne $branch -or
    $finalStatus -cne $status -or ($null -ne $buildSourceState -and
    $finalBuildSourceState.Fingerprint -cne $buildSourceState.Fingerprint)) {
    throw 'Git HEAD, branch, working-tree status, or tracked build sources changed during compilation.'
}

$dllPath = Join-Path $modRoot $identity.AssemblyFile
if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
    throw "Missing $Channel DLL: $dllPath"
}
try {
    $actualAssemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($dllPath).Name
} catch {
    throw "Could not read the managed assembly identity from $dllPath`: $($_.Exception.Message)"
}
if ($actualAssemblyName -cne $identity.AssemblyName) {
    throw "DLL assembly identity is '$actualAssemblyName'; expected '$($identity.AssemblyName)'."
}

$repositoryCacheRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot '.cache'))
$effectiveCacheRoot = if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    $repositoryCacheRoot
} else {
    Assert-PathWithin -Path $CacheRoot -Parent $repositoryCacheRoot
}
$cacheKind = if ($Nightly) { 'nightly' } else { 'release' }
$cacheParent = [System.IO.Path]::GetFullPath((Join-Path $effectiveCacheRoot $cacheKind))
$releaseRoot = Assert-PathWithin -Path (Join-Path $cacheParent $artifactVersion) -Parent $cacheParent
$stage = Join-Path $releaseRoot $identity.ModDirectory
$zipName = "arknights-oni-$artifactVersion.zip"
$zipPath = Join-Path $releaseRoot $zipName
$catalogStage = Join-Path $stage 'assets\catalog'

$files = @(
    $dllPath,
    (Join-Path $modRoot 'PLIB-LICENSE.txt'),
    (Join-Path $modRoot 'PLIB-SOURCE.txt'),
    (Join-Path $modRoot 'SPINE-RUNTIME-LICENSE.txt'),
    (Join-Path $modRoot 'lib\SPINE-RUNTIME-SOURCE.txt'),
    (Join-Path $modRoot 'lib\PLib.dll'),
    (Join-Path $repoRoot 'DATA_NOTICE.md'),
    (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md')
)
$catalog = Join-Path $modRoot 'assets\catalog\operator_appearances_20260604.json'

foreach ($file in ($files + $catalog)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Missing release input: $file"
    }
}

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $catalogStage -Force | Out-Null
Copy-Item -LiteralPath $files -Destination $stage
Copy-Item -LiteralPath $catalog -Destination $catalogStage
Write-Utf8NoBom -Path (Join-Path $stage 'mod.yaml') -Content (
    New-ModYaml -Identity $identity -BasePath $baseModYaml
)
Write-Utf8NoBom -Path (Join-Path $stage 'mod_info.yaml') -Content (
    New-ModInfoYaml -ManifestVersion $manifestVersion -BasePath $baseModInfoYaml
)

$stagedModYaml = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $stage 'mod.yaml')
$stagedModInfoYaml = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $stage 'mod_info.yaml')
$stagedStaticIdMatches = [regex]::Matches($stagedModYaml, '(?m)^staticID:\s*(\S+)\s*$')
if ($stagedStaticIdMatches.Count -ne 1 -or
    $stagedStaticIdMatches[0].Groups[1].Value -cne $identity.StaticId) {
    throw 'Staged mod.yaml must contain exactly one correct staticID.'
}
$stagedVersionMatches = [regex]::Matches($stagedModInfoYaml, '(?m)^version:\s*(\S+)\s*$')
if ($stagedVersionMatches.Count -ne 1 -or
    $stagedVersionMatches[0].Groups[1].Value -cne $manifestVersion) {
    throw 'Staged mod_info.yaml must contain exactly one correct version.'
}

$dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
$generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
$buildInfoSidecarName = [System.IO.Path]::GetFileNameWithoutExtension($zipName) + '.build-info.json'
$embeddedBuildInfoPath = Join-Path $stage 'build-info.json'
$embeddedBuildInfo = [pscustomobject][ordered]@{
    schemaVersion = 1
    version = $manifestVersion
    channel = $Channel
    sourceSha = $sourceSha
    sourceShortSha = $sourceShortSha
    sourceCommitTimeUtc = $sourceCommitTimeUtc
    branch = $branch
    dirty = $dirty
    localDirty = [bool]$dirty
    eligibleForUpload = $eligibleForUpload
    compiledInThisRun = [bool](-not $SkipCompile)
    modDirectory = $identity.ModDirectory
    assemblyName = $identity.AssemblyName
    assemblyFile = $identity.AssemblyFile
    staticID = $identity.StaticId
    dllSha256 = $dllHash
    zipFile = $zipName
    zipSha256 = $null
    zipSha256Source = $buildInfoSidecarName
}
Write-Utf8NoBom -Path $embeddedBuildInfoPath -Content ($embeddedBuildInfo | ConvertTo-Json -Depth 4)
$embeddedBuildInfoHash = (Get-FileHash -LiteralPath $embeddedBuildInfoPath -Algorithm SHA256).Hash

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipStream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::CreateNew)
try {
    $zip = [System.IO.Compression.ZipArchive]::new(
        $zipStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false
    )
    try {
        $fixedTimestamp = [System.DateTimeOffset]::new(2026, 7, 15, 0, 0, 0, [System.TimeSpan]::Zero)
        foreach ($file in (Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName)) {
            $entryName = $file.FullName.Substring($releaseRoot.Length).TrimStart('\', '/').Replace('\', '/')
            $entry = $zip.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedTimestamp
            $sourceStream = [System.IO.File]::OpenRead($file.FullName)
            $entryStream = $entry.Open()
            try {
                $sourceStream.CopyTo($entryStream)
            } finally {
                $entryStream.Dispose()
                $sourceStream.Dispose()
            }
        }
    } finally {
        $zip.Dispose()
    }
} finally {
    $zipStream.Dispose()
}

$expectedEntries = @(
    "$($identity.ModDirectory)/$($identity.AssemblyFile)",
    "$($identity.ModDirectory)/DATA_NOTICE.md",
    "$($identity.ModDirectory)/PLIB-LICENSE.txt",
    "$($identity.ModDirectory)/PLIB-SOURCE.txt",
    "$($identity.ModDirectory)/SPINE-RUNTIME-LICENSE.txt",
    "$($identity.ModDirectory)/THIRD_PARTY_NOTICES.md",
    "$($identity.ModDirectory)/assets/catalog/operator_appearances_20260604.json",
    "$($identity.ModDirectory)/build-info.json",
    "$($identity.ModDirectory)/mod.yaml",
    "$($identity.ModDirectory)/mod_info.yaml",
    "$($identity.ModDirectory)/PLib.dll",
    "$($identity.ModDirectory)/SPINE-RUNTIME-SOURCE.txt"
) | Sort-Object

$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
    $actualEntries = @($entries.FullName | Sort-Object)
    $entryDifference = @(Compare-Object -ReferenceObject $expectedEntries -DifferenceObject $actualEntries)
    if ($entryDifference.Count -ne 0) {
        throw "Release archive identity/file whitelist mismatch: $($entryDifference | Out-String)"
    }
    if ($entries.FullName -match 'AmiyaDuplicant|assets[\\/](spine|frames)|preview|cache') {
        throw 'Release archive contains a forbidden legacy, cached, or preview path.'
    }
    $mainAssemblies = @($entries | Where-Object {
        $_.FullName -match '/ArknightsOperators(?:Mod|Testing)\.dll$'
    })
    if ($mainAssemblies.Count -ne 1 -or
        $mainAssemblies[0].FullName -cne "$($identity.ModDirectory)/$($identity.AssemblyFile)") {
        throw 'Release archive does not contain exactly one correctly named main Mod assembly.'
    }
} finally {
    $archive.Dispose()
}

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$buildInfoPath = Join-Path $releaseRoot $buildInfoSidecarName
$buildInfo = [pscustomobject][ordered]@{
    schemaVersion = 1
    version = $manifestVersion
    channel = $Channel
    sourceSha = $sourceSha
    sourceShortSha = $sourceShortSha
    sourceCommitTimeUtc = $sourceCommitTimeUtc
    branch = $branch
    dirty = $dirty
    localDirty = [bool]$dirty
    eligibleForUpload = $eligibleForUpload
    compiledInThisRun = [bool](-not $SkipCompile)
    modDirectory = $identity.ModDirectory
    assemblyName = $identity.AssemblyName
    assemblyFile = $identity.AssemblyFile
    staticID = $identity.StaticId
    dllSha256 = $dllHash
    zipFile = $zipName
    zipSha256 = $zipHash
    embeddedBuildInfoSha256 = $embeddedBuildInfoHash
    generatedAtUtc = $generatedAtUtc
}
Write-Utf8NoBom -Path $buildInfoPath -Content ($buildInfo | ConvertTo-Json -Depth 4)

if ($Nightly) {
    $nightlyRoot = [System.IO.Path]::GetFullPath((Join-Path $effectiveCacheRoot 'nightly'))
    $nightlyDirectories = @(Get-CompleteNightlyDirectories -NightlyRoot $nightlyRoot |
        Sort-Object -Property @(
        @{ Expression = 'LastWriteTimeUtc'; Descending = $true },
        @{ Expression = 'Name'; Descending = $true }
    ))
    foreach ($oldDirectory in @($nightlyDirectories | Select-Object -Skip 3)) {
        $safeOldDirectory = Assert-PathWithin -Path $oldDirectory.FullName -Parent $nightlyRoot
        Remove-Item -LiteralPath $safeOldDirectory -Recurse -Force
    }
}

Write-Output "Channel: $Channel"
Write-Output "Version: $manifestVersion"
Write-Output "Package directory: $stage"
Write-Output "Package ZIP: $zipPath"
Write-Output "Build info: $buildInfoPath"
Write-Output "Files: $($expectedEntries.Count)"
Write-Output "DLL SHA-256: $dllHash"
Write-Output "ZIP SHA-256: $zipHash"
Write-Output "Eligible for upload: $eligibleForUpload"
