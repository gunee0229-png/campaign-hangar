$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Split-Path -Parent $PSScriptRoot
$assets = @(
    @{Id='terran'; Menu='wol'; Main='icon-terran.png'; Background='#071523'; Light='#143e59'; Accent='#55b5f5'; Line='#6c9eb5'; Kind='command'},
    @{Id='zerg'; Menu='hots'; Main='icon-zerg.png'; Background='#160817'; Light='#3b0e37'; Accent='#cc5bbb'; Line='#9a4c88'; Kind='organic'},
    @{Id='protoss'; Menu='lotv'; Main='icon-protoss.png'; Background='#160f15'; Light='#563919'; Accent='#f3c16c'; Line='#98bfe3'; Kind='warp'},
    @{Id='nova'; Menu='nova'; Main='icon-nova.png'; Background='#031314'; Light='#092d33'; Accent='#50d5c4'; Line='#3fa38e'; Kind='covert'}
)
function Color([string]$value,[int]$alpha=255){
    $c=[System.Drawing.ColorTranslator]::FromHtml($value)
    return [System.Drawing.Color]::FromArgb($alpha,$c.R,$c.G,$c.B)
}
function SaveBadge($entry) {
    $inputPath=Join-Path $assetRoot $entry.Main
    if(-not(Test-Path -LiteralPath $inputPath)){throw "Missing original emblem: $inputPath"}
    $original=[System.Drawing.Image]::FromFile($inputPath)
    try {
        foreach($spec in @(@("logo-$($entry.Id).png",1024,760),
                          @("menu-$($entry.Menu).png",256,214))) {
            $size=[int]$spec[1]; $side=[int]$spec[2]
            $bitmap=[System.Drawing.Bitmap]::new($size,$size,
                [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $canvas=[System.Drawing.Graphics]::FromImage($bitmap)
            try{
                $canvas.Clear([System.Drawing.Color]::Transparent)
                $canvas.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $canvas.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $canvas.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $margin=[int](($size-$side)/2)
                $canvas.DrawImage($original,[System.Drawing.Rectangle]::new($margin,$margin,$side,$side))
                $bitmap.Save((Join-Path $PSScriptRoot $spec[0]),[System.Drawing.Imaging.ImageFormat]::Png)
            }finally{$canvas.Dispose();$bitmap.Dispose()}
        }
    }finally{$original.Dispose()}
}
function Background($entry) {
    $w=1920;$h=1080
    $bmp=[System.Drawing.Bitmap]::new($w,$h,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g=[System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.CompositingQuality=[System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    try{
        $r=[System.Drawing.Rectangle]::new(0,0,$w,$h)
        $gradient=[System.Drawing.Drawing2D.LinearGradientBrush]::new($r,(Color $entry.Background),(Color $entry.Light),35)
        try{$g.FillRectangle($gradient,$r)}finally{$gradient.Dispose()}
        # Fine technical grid and alignment beams, specific to each faction.
        foreach($x in 0..48) {
            $coord=$x*40
            $weight=if($x%5 -eq 0){2.2}else{0.7}
            $alpha=if($x%5 -eq 0){39}else{17}
            $p=[System.Drawing.Pen]::new((Color $entry.Line $alpha),$weight)
            try{$g.DrawLine($p,$coord,0,$coord,$h)}finally{$p.Dispose()}
        }
        foreach($y in 0..27) {
            $coord=$y*40
            $p=[System.Drawing.Pen]::new((Color $entry.Line $(if($y%5 -eq 0){42}else{16})),1)
            try{$g.DrawLine($p,0,$coord,$w,$coord)}finally{$p.Dispose()}
        }
        $bright=[System.Drawing.Pen]::new((Color $entry.Accent 115),3)
        $faint=[System.Drawing.Pen]::new((Color $entry.Line 48),1)
        try{
            switch ($entry.Kind){
              'command' {
                $g.DrawLine($bright,120,150,620,150)
                $g.DrawLine($faint,120,160,620,160)
                $g.DrawLine($bright,1300,150,1800,150)
                $g.DrawLine($faint,1300,160,1800,160)
                for($i=0;$i -lt 7;$i++){
                    $g.DrawRectangle($faint,70+$i*11,80+$i*8,1760-$i*22,670-$i*16)
                }
              }
              'organic' {
                for($i=0;$i -lt 9;$i++){
                    $radius=180+$i*34
                    $g.DrawArc($faint,960-$radius,410-$radius,$radius*2,$radius*2,-23,215)
                    $g.DrawArc($bright,960-$radius,410-$radius,$radius*2,$radius*2,154,22)
                }
              }
              'warp' {
                for($i=0;$i -lt 8;$i++){
                    $radius=210+$i*37
                    $g.DrawArc($faint,960-$radius,380-$radius,$radius*2,$radius*2,205,128)
                }
                $g.DrawLine($bright,960,0,960,710)
              }
              'covert' {
                for($i=0;$i -lt 6;$i++){
                    $radius=220+$i*40
                    $g.DrawEllipse($faint,960-$radius,405-$radius,$radius*2,$radius*2)
                }
                $g.DrawLine($bright,100,405,1820,405)
                $g.DrawLine($faint,960,44,960,720)
                for($i=0;$i -lt 26;$i++){$g.DrawLine($faint,115+$i*66,415,115+$i*66,425)}
              }
            }
            # Progress area is deliberately blank. Programmatically overlay stage,
            # measured bytes and percentage at runtime, not in a raster file.
            $panel=[System.Drawing.Rectangle]::new(405,745,1110,210)
            $brush=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(185,1,8,12))
            try{$g.FillRectangle($brush,$panel)}finally{$brush.Dispose()}
            $g.DrawRectangle($bright,$panel)
            $g.DrawLine($faint,420,755,1500,755)
            $g.DrawLine($faint,420,944,1500,944)
        }finally{$bright.Dispose();$faint.Dispose()}
        $src=Join-Path $assetRoot $entry.Main
        $crest=[System.Drawing.Image]::FromFile($src)
        try{
            $center=330
            $g.DrawImage($crest,[System.Drawing.Rectangle]::new(795,185,$center,$center))
        }finally{$crest.Dispose()}
        $out=Join-Path $PSScriptRoot ('loading-'+$entry.Id+'.png')
        $bmp.Save($out,[System.Drawing.Imaging.ImageFormat]::Png)
    } finally{$g.Dispose();$bmp.Dispose()}
}
foreach($entry in $assets){
    SaveBadge $entry
    Background $entry
    Write-Output ('BUILT|'+$entry.Id+'|'+$entry.Main)
}
