# Generate passage / question PNGs from passage_src/data.json
# ASCII-only script body. All Japanese text comes from data.json (UTF-8).
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

$root    = "C:\UnityProjects\GazeHeadAOILogger2"
$srcJson = Join-Path $root "passage_src\data.json"
$outDir  = Join-Path $root "Assets\Textures"

$data = Get-Content -LiteralPath $srcJson -Encoding UTF8 -Raw | ConvertFrom-Json

# pick an installed Japanese font
$installed = ([System.Drawing.FontFamily]::Families | ForEach-Object { $_.Name })
$fontName = "MS Gothic"
foreach ($cand in @("Yu Gothic Medium", "Yu Gothic", "Yu Gothic UI", "Meiryo", "MS Gothic", "MS PGothic")) {
    if ($installed -contains $cand) { $fontName = $cand; break }
}
Write-Host "Font: $fontName"

$W = 1200; $H = 800; $margin = 70
$areaW = $W - 2 * $margin
$areaH = $H - 2 * $margin

function Wrap-Paragraph {
    param([string]$text, $font, $graphics, [double]$maxWidth)
    $lines = New-Object System.Collections.Generic.List[string]
    if ($text -eq "") { $lines.Add(""); return ,$lines }
    $current = ""
    foreach ($ch in $text.ToCharArray()) {
        $test = $current + $ch
        $w = $graphics.MeasureString($test, $font).Width
        if ($w -gt $maxWidth -and $current.Length -gt 0) {
            $lines.Add($current)
            $current = [string]$ch
        } else {
            $current = $test
        }
    }
    if ($current.Length -gt 0) { $lines.Add($current) }
    return ,$lines
}

function Build-Lines {
    param($paragraphs, $font, $graphics)
    $all = New-Object System.Collections.Generic.List[string]
    foreach ($p in $paragraphs) {
        $wrapped = Wrap-Paragraph $p $font $graphics $areaW
        foreach ($l in $wrapped) { $all.Add($l) }
    }
    return ,$all
}

function Render-Paragraphs {
    param($paragraphs, [string]$outPath, [double]$lineSpacing = 1.55)
    $bmp = New-Object System.Drawing.Bitmap($W, $H)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::White)

    $chosen = $null
    $chosenLines = $null
    foreach ($fs in 46, 42, 38, 34, 30, 26, 22) {
        $font = New-Object System.Drawing.Font($fontName, [single]$fs, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
        $lines = Build-Lines $paragraphs $font $g
        $lineH = $fs * $lineSpacing
        $totalH = $lines.Count * $lineH
        $font.Dispose()
        if ($totalH -le $areaH) { $chosen = $fs; $chosenLines = $lines; break }
    }
    if ($null -eq $chosenLines) {
        $chosen = 22
        $font = New-Object System.Drawing.Font($fontName, [single]$chosen, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
        $chosenLines = Build-Lines $paragraphs $font $g
        $font.Dispose()
    }

    $font = New-Object System.Drawing.Font($fontName, [single]$chosen, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $lineH = $chosen * $lineSpacing
    $y = [double]$margin
    foreach ($l in $chosenLines) {
        if ($l -ne "") {
            $g.DrawString($l, $font, [System.Drawing.Brushes]::Black, [single]$margin, [single]$y)
        }
        $y += $lineH
    }
    $font.Dispose()
    $g.Dispose()
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# "[問題]" header built from unicode code points (keeps this file ASCII-only)
$header = -join @([char]0x3010, [char]0x554F, [char]0x984C, [char]0x3011)

$n = 0
foreach ($item in $data) {
    $n++
    $pPath = Join-Path $outDir ("passage{0}.png" -f $n)
    Render-Paragraphs @([string]$item.passage) $pPath

    $qParas = @(
        $header,
        [string]$item.question,
        "",
        ("a)  " + [string]$item.a),
        ("b)  " + [string]$item.b),
        ("c)  " + [string]$item.c),
        ("d)  " + [string]$item.d)
    )
    $qPath = Join-Path $outDir ("question{0}.png" -f $n)
    Render-Paragraphs $qParas $qPath
    Write-Host ("Generated passage{0}.png / question{0}.png" -f $n)
}
Write-Host ("Done: {0} sets" -f $n)
