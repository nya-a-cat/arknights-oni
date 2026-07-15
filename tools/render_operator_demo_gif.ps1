param(
    [string]$ScreenshotRoot = (Join-Path $PSScriptRoot '..\docs\images\source\alpha-v0.3.2\gif'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\docs\images\arknights-operators-demo-v0.3.2.gif')
)

$ErrorActionPreference = 'Stop'

$frames = @(
    @{ Name = '01-four-operators.png'; Duration = 1.6; Sha256 = 'E29DA93A5BF06EB03D977FF96EB9525B81501B7658530CA32C544BB9C2489A12' },
    @{ Name = '02-individual-picker.png'; Duration = 2.0; Sha256 = '10CC16D82DD64B4C9BAE05BA1EA88DA3962B7C3397A1832B0A7878BC3BFAB3D1' },
    @{ Name = '03-action-wheel.png'; Duration = 2.0; Sha256 = 'EF3EBC043A600AEC4456B04D044EEE0980A23821CBEFA157512A394AC072F9F8' },
    @{ Name = '04-amiya-sleep.png'; Duration = 2.0; Sha256 = '0A3CDA480AF927F63D85CBF2DED1283C51C13AFE3FA9CBE7CF916687782A7E8B' },
    @{ Name = '05-automatic-restored.png'; Duration = 1.6; Sha256 = '7DB56A1D15B4E69D271BC587F8600FBCCE9E24BA4270ECEDE86504FE14CDA658' }
)

$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
$resolvedRoot = (Resolve-Path -LiteralPath $ScreenshotRoot).Path
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFullPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

foreach ($frame in $frames) {
    $path = Join-Path $resolvedRoot $frame.Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing GIF source frame: $path"
    }
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actualHash -ne $frame.Sha256) {
        throw "GIF source frame hash mismatch: $path"
    }
}

$totalDuration = ($frames | ForEach-Object { [double]$_['Duration'] } | Measure-Object -Sum).Sum
$totalDurationText = $totalDuration.ToString('0.###', [System.Globalization.CultureInfo]::InvariantCulture)

$concatPath = Join-Path ([System.IO.Path]::GetTempPath()) ("arknights-oni-gif-{0}.txt" -f [guid]::NewGuid())
try {
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($frame in $frames) {
        $path = (Join-Path $resolvedRoot $frame.Name).Replace('\', '/')
        $lines.Add("file '$path'")
        $lines.Add("duration $($frame.Duration)")
    }
    $lastPath = (Join-Path $resolvedRoot $frames[-1].Name).Replace('\', '/')
    $lines.Add("file '$lastPath'")
    [System.IO.File]::WriteAllLines($concatPath, $lines, [System.Text.UTF8Encoding]::new($false))

    $filter = '[0:v]fps=8,scale=960:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=192:stats_mode=diff[p];[s1][p]paletteuse=dither=sierra2_4a'
    & $ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i $concatPath -t $totalDurationText -filter_complex $filter -loop 0 $outputFullPath
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed with exit code $LASTEXITCODE"
    }
} finally {
    Remove-Item -LiteralPath $concatPath -Force -ErrorAction SilentlyContinue
}

$output = Get-Item -LiteralPath $outputFullPath
Write-Output ("Rendered {0} ({1} bytes)" -f $output.FullName, $output.Length)
