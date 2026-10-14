Add-Type -AssemblyName System.Drawing

$src = 'c:\Users\Lenovo\Downloads\Kerja\RISA-bot-1\RISAlogo\Risalogo-removebg-preview.png'
$dst = 'c:\Users\Lenovo\Downloads\Kerja\RISA-bot-1\RisaBotApp\risa.ico'

$png = [System.Drawing.Image]::FromFile($src)
$sizes = @(16,32,48,64,128,256)
$images = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($png, $s, $s)
    $imgMs = New-Object System.IO.MemoryStream
    $bmp.Save($imgMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += @(,$imgMs.ToArray())
    $bmp.Dispose()
}
$png.Dispose()

$fs = [System.IO.File]::OpenWrite($dst)
$w = New-Object System.IO.BinaryWriter($fs)

# ICO header
$w.Write([uint16]0)
$w.Write([uint16]1)
$w.Write([uint16]$sizes.Count)

# directory entries
$offset = [uint32](6 + $sizes.Count * 16)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -eq 256) { [byte]0 } else { [byte]$s }
    $w.Write($dim)
    $w.Write($dim)
    $w.Write([byte]0)
    $w.Write([byte]0)
    $w.Write([uint16]1)
    $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length)
    $w.Write($offset)
    $offset += [uint32]$images[$i].Length
}

foreach ($img in $images) {
    $w.Write($img)
}

$w.Close()
$fs.Close()

Write-Host "ICO written: $dst ($((Get-Item $dst).Length) bytes)"
