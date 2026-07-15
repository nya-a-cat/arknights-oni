param(
    [string]$Version = 'v0.3.2-alpha.1'
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modRoot = Join-Path $repoRoot 'arknights_oni_mod_work\ArknightsOperatorsMod'
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".cache\release\$Version"))
$expectedReleaseParent = [System.IO.Path]::GetFullPath((Join-Path $repoRoot '.cache\release')) + [System.IO.Path]::DirectorySeparatorChar
if (-not $releaseRoot.StartsWith($expectedReleaseParent, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Release path escaped the repository cache: $releaseRoot"
}

$stage = Join-Path $releaseRoot 'ArknightsOperatorsMod'
$zipName = "arknights-oni-$Version.zip"
$zipPath = Join-Path $releaseRoot $zipName
$catalogStage = Join-Path $stage 'assets\catalog'

$files = @(
    (Join-Path $modRoot 'ArknightsOperatorsMod.dll'),
    (Join-Path $modRoot 'mod.yaml'),
    (Join-Path $modRoot 'mod_info.yaml'),
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

$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
    if ($entries.Count -ne 11) {
        throw "Release archive contains $($entries.Count) files; expected 11."
    }
    if ($entries.FullName -match 'AmiyaDuplicant|assets[\\/](spine|frames)|preview|cache') {
        throw 'Release archive contains a forbidden legacy, cached, or preview path.'
    }
} finally {
    $archive.Dispose()
}

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Write-Output "Release directory: $stage"
Write-Output "Release ZIP: $zipPath"
Write-Output "Files: 11"
Write-Output "SHA-256: $hash"
