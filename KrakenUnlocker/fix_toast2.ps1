$path = "c:\Users\Admin\CascadeProjects\Xbox-Achievement-Unlocker-3\KrakenUnlocker\Services\KrakenToast.cs"
$c = Get-Content $path -Raw
$c = $c -replace 'HorizontalAlignment\.Right', 'WpfHA.Right'
$c = $c -replace 'HorizontalAlignment\.Left', 'WpfHA.Left'
$c = $c -replace 'new LinearEase\(\)', 'new SineEase()'
Set-Content $path $c
Write-Host "Done"
