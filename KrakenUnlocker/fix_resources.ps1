$pages = @(
    "Views\Pages\GamesPage.xaml",
    "Views\Pages\MiscPage.xaml",
    "Views\Pages\AchievementsPage.xaml",
    "Views\Pages\StatsPage.xaml",
    "Views\Pages\Xbox360Page.xaml"
)
foreach ($p in $pages) {
    $full = "c:\Users\Admin\CascadeProjects\Xbox-Achievement-Unlocker-3\KrakenUnlocker\$p"
    $c = Get-Content $full -Raw
    # Remove TextBox.Resources blocks (the old ui:TextBox resource overrides)
    $c = $c -replace '(?s)\s*<TextBox\.Resources>.*?</TextBox\.Resources>', ''
    Set-Content $full $c
    Write-Host "Fixed: $p"
}
