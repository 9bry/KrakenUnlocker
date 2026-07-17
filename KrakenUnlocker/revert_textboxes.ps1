$pages = @(
    "Views\Pages\GamesPage.xaml",
    "Views\Pages\AchievementsPage.xaml",
    "Views\Pages\MiscPage.xaml",
    "Views\Pages\StatsPage.xaml",
    "Views\Pages\Xbox360Page.xaml",
    "Views\Pages\SettingsPage.xaml"
)
foreach ($p in $pages) {
    $full = "c:\Users\Admin\CascadeProjects\Xbox-Achievement-Unlocker-3\KrakenUnlocker\$p"
    $c = Get-Content $full -Raw
    $c = $c -replace '<TextBox ', '<ui:TextBox '
    $c = $c -replace '</TextBox>', '</ui:TextBox>'
    Set-Content $full $c
    Write-Host "Reverted: $p"
}
