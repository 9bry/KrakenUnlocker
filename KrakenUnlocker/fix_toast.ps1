$path = "c:\Users\Admin\CascadeProjects\Xbox-Achievement-Unlocker-3\KrakenUnlocker\Services\KrakenToast.cs"
$c = Get-Content $path -Raw
$c = $c -replace '\bColor\.FromRgb\b', 'WpfColor.FromRgb'
$c = $c -replace '\(Color\)ColorConverter', '(WpfColor)WpfColorConverter'
$c = $c -replace 'new Point\(', 'new WpfPoint('
$c = $c -replace '\bBrushes\.White\b', 'WpfBrushes.White'
$c = $c -replace '\bBrushes\.Gray\b', 'WpfBrushes.Gray'
$c = $c -replace '\bBrushes\.LightGray\b', 'WpfBrushes.LightGray'
Set-Content $path $c
Write-Host "Done"
