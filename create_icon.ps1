Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path $PSScriptRoot "src\SkinForge.UI\Assets"
if (-not (Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
}

$icoPath = Join-Path $assetsDir "icon.ico"
if (-not (Test-Path $icoPath)) {
    $bmp = New-Object System.Drawing.Bitmap(64, 64)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    # Background circle (Charcoal #0B0D10)
    $brushBg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 11, 13, 16))
    $g.FillEllipse($brushBg, 2, 2, 60, 60)

    # Gold border (Hextech Gold #C8AA6E)
    $penGold = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 200, 170, 110), 3)
    $g.DrawEllipse($penGold, 3, 3, 58, 58)

    # Inner Emblem (SF)
    $font = New-Object System.Drawing.Font("Segoe UI", 20, [System.Drawing.FontStyle]::Bold)
    $brushGold = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 200, 170, 110))
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF(0, 0, 64, 64)
    $g.DrawString("SF", $font, $brushGold, $rect, $format)

    $hIcon = $bmp.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($hIcon)
    $fs = [System.IO.File]::OpenWrite($icoPath)
    $icon.Save($fs)
    $fs.Close()
    $g.Dispose()
    $bmp.Dispose()
    Write-Host "[+] Branded Icon generated: $icoPath" -ForegroundColor Green
}
