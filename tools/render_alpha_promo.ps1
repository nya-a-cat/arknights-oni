param(
	[string]$ScreenshotRoot = "",
	[string]$OutputPath = ""
)

Add-Type -AssemblyName System.Drawing

function ConvertFrom-CodePoints([int[]]$Codes) {
	return -join ($Codes | ForEach-Object { [char]$_ })
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ScreenshotRoot)) {
	$ScreenshotRoot = Join-Path $repoRoot "docs\images\source\alpha-v0.3.2"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
	$OutputPath = Join-Path $repoRoot "docs\images\arknights-oni-alpha-v0.3.2-workshop.png"
}

$amiyaCn = ConvertFrom-CodePoints @(0x963F, 0x7C73, 0x5A05)
$texasCn = ConvertFrom-CodePoints @(0x5FB7, 0x514B, 0x8428, 0x65AF)
$kaltsitCn = ConvertFrom-CodePoints @(0x51EF, 0x5C14, 0x5E0C)
$exusiaiCn = ConvertFrom-CodePoints @(0x80FD, 0x5929, 0x4F7F)
$arknightsCn = ConvertFrom-CodePoints @(0x660E, 0x65E5, 0x65B9, 0x821F, 0x5E72, 0x5458)
$oniCn = ConvertFrom-CodePoints @(0x7F3A, 0x6C27)
$cards = @(
	@{ File = "20260716021045_1.jpg"; Label = "TEXAS / $texasCn"; Sha256 = "4F209411C98F439B6856D898259DAF5ADB3CEFC76ED04C45A8F10E78B70E97BF"; CropX = 248; CropY = 190; CropWidth = 175; CropHeight = 277 },
	@{ File = "20260716021045_1.jpg"; Label = "AMIYA / $amiyaCn"; Sha256 = "4F209411C98F439B6856D898259DAF5ADB3CEFC76ED04C45A8F10E78B70E97BF"; CropX = 503; CropY = 190; CropWidth = 175; CropHeight = 277 },
	@{ File = "20260716021045_1.jpg"; Label = "KAL'TSIT / $kaltsitCn"; Sha256 = "4F209411C98F439B6856D898259DAF5ADB3CEFC76ED04C45A8F10E78B70E97BF"; CropX = 720; CropY = 190; CropWidth = 175; CropHeight = 277 },
	@{ File = "20260716021045_1.jpg"; Label = "EXUSIAI / $exusiaiCn"; Sha256 = "4F209411C98F439B6856D898259DAF5ADB3CEFC76ED04C45A8F10E78B70E97BF"; CropX = 908; CropY = 190; CropWidth = 175; CropHeight = 277 }
)

$outputDirectory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$canvas = New-Object System.Drawing.Bitmap 1920, 1080
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

$background = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 15, 19, 27))
$header = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 25, 31, 43))
$accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240, 129, 46))
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$muted = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 187, 198, 214))
$footer = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(215, 11, 15, 22))
$border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 64, 76, 94)), 4
$accentPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 240, 129, 46)), 6
$titleFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 42, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$subtitleFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 28, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$featureFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 22, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$cardFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 25, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$badgeFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 22, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)

try {
	$graphics.FillRectangle($background, 0, 0, 1920, 1080)
	$graphics.FillRectangle($header, 0, 0, 1920, 205)
	$graphics.FillRectangle($accent, 0, 0, 18, 205)
	$graphics.DrawString("ARKNIGHTS OPERATORS X OXYGEN NOT INCLUDED", $titleFont, $white, 58, 38)
	$graphics.DrawString("$arknightsCn X $oniCn   /   449 OPERATORS   /   LIVE SWITCHING", $subtitleFont, $muted, 62, 108)
	$graphics.DrawString("AUTO-LOCALIZED OPTIONS: CN / EN   /   OPERATOR SEARCH: CN / EN / JP", $featureFont, $accent, 62, 151)
	$graphics.FillRectangle($accent, 1545, 54, 310, 78)
	$graphics.DrawString("v0.3.2-alpha.2", $badgeFont, $background, 1580, 78)

	for ($index = 0; $index -lt $cards.Count; $index++) {
		$card = $cards[$index]
		$sourcePath = Join-Path $ScreenshotRoot $card.File
		if (-not (Test-Path -LiteralPath $sourcePath)) {
			throw "Missing source screenshot: $sourcePath"
		}
		$actualHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
		if ($actualHash -ne $card.Sha256) {
			throw "Source screenshot hash mismatch: $sourcePath"
		}

		$image = [System.Drawing.Image]::FromFile($sourcePath)
		try {
			if ($image.Width -ne 1920 -or $image.Height -ne 1080) {
				throw "Unexpected source screenshot dimensions: $sourcePath"
			}
			$x = 55 + ($index * 457)
			$destination = New-Object System.Drawing.Rectangle $x, 245, 430, 680
			$source = New-Object System.Drawing.Rectangle $card.CropX, $card.CropY, $card.CropWidth, $card.CropHeight
			$graphics.DrawImage($image, $destination, $source, [System.Drawing.GraphicsUnit]::Pixel)
			$graphics.DrawRectangle($border, $destination)
			$graphics.DrawLine($accentPen, $x, 245, ($x + 430), 245)
			$graphics.FillRectangle($footer, $x, 925, 430, 70)
			$graphics.DrawString($card.Label, $cardFont, $white, ($x + 20), 943)
		} finally {
			$image.Dispose()
		}
	}

	$graphics.DrawString("Real in-game screenshots / On-demand or permanent cache / Ctrl+F8", $subtitleFont, $muted, 58, 1021)
	$canvas.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
	$graphics.Dispose()
	$canvas.Dispose()
	$background.Dispose()
	$header.Dispose()
	$accent.Dispose()
	$white.Dispose()
	$muted.Dispose()
	$footer.Dispose()
	$border.Dispose()
	$accentPen.Dispose()
	$titleFont.Dispose()
	$subtitleFont.Dispose()
	$featureFont.Dispose()
	$cardFont.Dispose()
	$badgeFont.Dispose()
}

Write-Output $OutputPath
