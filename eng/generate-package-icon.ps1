param(
    [string]$OutputPath = 'assets/nodal-package-icon.png'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $repositoryRoot $OutputPath
$destinationDirectory = Split-Path -Parent $destination
New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new(256, 256)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#0b1220'))

$edgePen = [System.Drawing.Pen]::new(
    [System.Drawing.ColorTranslator]::FromHtml('#4e688a'),
    8
)
$edgePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$edgePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$nodeBrush = [System.Drawing.SolidBrush]::new(
    [System.Drawing.ColorTranslator]::FromHtml('#62e6bd')
)
$accentBrush = [System.Drawing.SolidBrush]::new(
    [System.Drawing.ColorTranslator]::FromHtml('#6d86ff')
)

try {
    $points = @(
        [System.Drawing.Point]::new(52, 58),
        [System.Drawing.Point]::new(128, 94),
        [System.Drawing.Point]::new(204, 52),
        [System.Drawing.Point]::new(128, 166),
        [System.Drawing.Point]::new(204, 204),
        [System.Drawing.Point]::new(52, 202)
    )

    foreach ($connection in @(
        @(0, 1), @(1, 2), @(1, 3), @(3, 4), @(3, 5), @(0, 5), @(2, 4)
    )) {
        $graphics.DrawLine($edgePen, $points[$connection[0]], $points[$connection[1]])
    }

    for ($index = 0; $index -lt $points.Count; $index++) {
        $radius = if ($index -in @(1, 3)) { 20 } else { 14 }
        $brush = if ($index -in @(2, 5)) { $accentBrush } else { $nodeBrush }
        $graphics.FillEllipse(
            $brush,
            $points[$index].X - $radius,
            $points[$index].Y - $radius,
            $radius * 2,
            $radius * 2
        )
    }

    $bitmap.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $accentBrush.Dispose()
    $nodeBrush.Dispose()
    $edgePen.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Host "Package icon generated at '$destination'."
