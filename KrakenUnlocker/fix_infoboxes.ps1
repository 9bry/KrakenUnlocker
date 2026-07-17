$path = "c:\Users\Admin\CascadeProjects\Xbox-Achievement-Unlocker-3\KrakenUnlocker\Views\Pages\InfoPage.xaml"
$c = Get-Content $path -Raw
$c = $c -replace 'Padding="12,10" Margin="0,0,0,10"', 'Padding="10,7" Margin="0,0,0,8"'
Set-Content $path $c
Write-Host "Done"
