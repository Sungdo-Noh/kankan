# 칸칸 앱 아이콘(src\KanKan\Assets\kankan.ico)을 만든다.
# 진한 칸 + 연한 칸 두 개, 각 칸 위에 흰 제목 줄. 여러 크기의 PNG를 하나의 .ico로 묶는다.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'src\KanKan\Assets\kankan.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'

    $s = $size / 32.0
    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x4A, 0x90, 0xE2))
    $light = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x9F, 0xC7, 0xF3))
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    $g.FillPath($dark, (New-RoundedPath (1 * $s) (3 * $s) (14 * $s) (26 * $s) (3.5 * $s)))
    $g.FillPath($light, (New-RoundedPath (17 * $s) (3 * $s) (14 * $s) (26 * $s) (3.5 * $s)))
    $bar = [Math]::Max(1.0, 2.2 * $s)
    $g.FillPath($white, (New-RoundedPath (4 * $s) (7 * $s) (8 * $s) $bar ($bar / 2)))
    $g.FillPath($white, (New-RoundedPath (20 * $s) (7 * $s) (8 * $s) $bar ($bar / 2)))
    $g.Dispose()
    return $bmp
}

# 256px는 PNG로, 나머지는 어디서나 읽히는 전통 DIB(32비트 BGRA + AND 마스크)로 넣는다
function ConvertTo-IconEntry([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $ms.ToArray()
    }
    $bw = New-Object System.IO.BinaryWriter $ms
    # BITMAPINFOHEADER: 높이는 색 + 마스크 두 장이라 2배
    $bw.Write([UInt32]40); $bw.Write([Int32]$size); $bw.Write([Int32]($size * 2))
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
    $bw.Write([UInt32]0); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {          # 아래에서 위로
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([Byte]$c.B); $bw.Write([Byte]$c.G); $bw.Write([Byte]$c.R); $bw.Write([Byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)  # 알파를 쓰므로 마스크는 전부 0
    $bw.Write((New-Object Byte[] ($maskRow * $size)))
    $bw.Flush()
    return , $ms.ToArray()
}

# ICO = 헤더(6) + 디렉터리 항목(16 × n) + 이미지 데이터
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = $sizes | ForEach-Object { $b = New-IconBitmap $_; , (ConvertTo-IconEntry $b); $b.Dispose() }

$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([Byte]$dim); $w.Write([Byte]$dim); $w.Write([Byte]0); $w.Write([Byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Dispose()
Write-Host "아이콘 생성: $out"
