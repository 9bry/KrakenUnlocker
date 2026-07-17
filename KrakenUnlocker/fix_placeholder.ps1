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
    # Remove PlaceholderText="..." attribute entirely (single or double quoted)
    $c = $c -replace '\s*PlaceholderText="[^"]*"', ''
    # Also remove Icon="{ui:SymbolIcon ...}" on TextBox since plain TextBox has no Icon
    $c = $c -replace '\s*Icon="\{ui:SymbolIcon [^}]*\}"', ''
    # Remove ClearButtonEnabled attribute
    $c = $c -replace '\s*ClearButtonEnabled="[^"]*"', ''
    Set-Content $full $c
    Write-Host "Fixed: $p"
}
