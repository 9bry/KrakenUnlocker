using System;
using System.Threading.Tasks;
using WpfApp = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KrakenUnlocker.ViewModels.Windows;
using KrakenUnlocker.Views.Pages;
using KrakenUnlocker.ViewModels.Pages;
using Wpf.Ui.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace KrakenUnlocker.Views.Windows;

public partial class MainWindow
{
    public MainWindowViewModel ViewModel { get; }
    public NavigationView NavigationView => null; // We're using our custom navigation now!

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        // Wire up SnackbarService so notifications work throughout the app
        var snackbarService = App.GetService<Wpf.Ui.Contracts.ISnackbarService>();
        snackbarService?.SetSnackbarPresenter(RootSnackbarPresenter);

        // Wire up custom KrakenToast
        KrakenUnlocker.Services.KrakenToast.SetHost(ToastHost);

        
        // Start with HomePage
        var homePage = App.GetService<HomePage>();
        if (homePage != null)
        {
            MainContentFrame.Navigate(homePage);
        }
        
        // Start particle animation
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Animation removed for stability
    }

    private void NavigateToPage(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton button)
        {
            string pageTag = button.Tag?.ToString() ?? "HomePage";
            NavigateToPageInternal(pageTag);
        }
    }

    private void NavigateToPageInternal(string pageTag)
    {
        Page? page = pageTag switch
        {
            "HomePage" => App.GetService<HomePage>(),
            "GamesPage" => App.GetService<GamesPage>(),
            "AchievementsPage" => App.GetService<AchievementsPage>(),
            "Xbox360Page" => App.GetService<Xbox360Page>(),
            "StatsPage" => App.GetService<StatsPage>(),
            "MiscPage" => App.GetService<MiscPage>(),
            "SpooferPage" => App.GetService<SpooferPage>(),
            "QueuePage" => App.GetService<QueuePage>(),
            _ => App.GetService<HomePage>()
        };

        if (page != null)
        {
            MainContentFrame.Navigate(page);
            // Fire OnNavigatedToAsync so ViewModels initialize properly
            _ = pageTag switch
            {
                "GamesPage" => App.GetService<GamesViewModel>()?.OnNavigatedToAsync(),
                "AchievementsPage" => App.GetService<AchievementsViewModel>()?.OnNavigatedToAsync(),
                "HomePage" => App.GetService<HomeViewModel>()?.OnNavigatedToAsync(),
                "StatsPage" => App.GetService<StatsViewModel>()?.OnNavigatedToAsync(),
                "MiscPage" => App.GetService<MiscViewModel>()?.OnNavigatedToAsync(),
                "SpooferPage" => App.GetService<SpooferViewModel>()?.OnNavigatedToAsync(),
                "QueuePage" => App.GetService<QueueViewModel>()?.OnNavigatedToAsync(),
                _ => Task.CompletedTask
            };
        }
    }

    private void NavigateToSettings(object sender, RoutedEventArgs e)
    {
        var page = App.GetService<SettingsPage>();
        if (page != null) MainContentFrame.Navigate(page);
    }

    private void NavigateToInfo(object sender, RoutedEventArgs e)
    {
        var page = App.GetService<InfoPage>();
        if (page == null) return;
        MainContentFrame.Navigate(page);
        _ = App.GetService<InfoViewModel>()?.OnNavigatedToAsync();
    }

    private void ShowLoadingAnimation()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        LoadingOverlay.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void HideLoadingAnimation()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (s, e) => LoadingOverlay.Visibility = Visibility.Collapsed;
        LoadingOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }

    // Window Control Events
    private void MinimizeWindow(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindow(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseWindow(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    // Theme Color Support
    public void UpdateThemeColor(string colorName)
    {
        // Update animated background
        // AnimatedBg?.SetThemeColor(colorName);
        
        // Update border gradient
        var (primaryColor, secondaryColor, glowColor) = GetThemeColors(colorName);
        
        if (MainBorder?.BorderBrush is LinearGradientBrush borderBrush)
        {
            borderBrush.GradientStops[0].Color = primaryColor;
            borderBrush.GradientStops[1].Color = secondaryColor;
            borderBrush.GradientStops[2].Color = primaryColor;
        }
        
        if (MainBorder?.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
        {
            shadow.Color = glowColor;
        }
        
        // Update resource dictionary colors
        UpdateResourceColors(colorName);
    }
    
    private (System.Windows.Media.Color primary, System.Windows.Media.Color secondary, System.Windows.Media.Color glow) GetThemeColors(string colorName)
    {
        return colorName switch
        {
            "Red" => (System.Windows.Media.Color.FromRgb(255, 0, 0), System.Windows.Media.Color.FromRgb(204, 0, 0), System.Windows.Media.Color.FromRgb(255, 0, 0)),
            "Blue" => (System.Windows.Media.Color.FromRgb(0, 120, 255), System.Windows.Media.Color.FromRgb(0, 90, 215), System.Windows.Media.Color.FromRgb(0, 120, 255)),
            "Purple" => (System.Windows.Media.Color.FromRgb(170, 0, 255), System.Windows.Media.Color.FromRgb(136, 23, 152), System.Windows.Media.Color.FromRgb(170, 0, 255)),
            "Green" => (System.Windows.Media.Color.FromRgb(0, 200, 83), System.Windows.Media.Color.FromRgb(16, 137, 62), System.Windows.Media.Color.FromRgb(0, 255, 100)),
            "Cyan" => (System.Windows.Media.Color.FromRgb(0, 230, 255), System.Windows.Media.Color.FromRgb(0, 183, 195), System.Windows.Media.Color.FromRgb(0, 230, 255)),
            "Orange" => (System.Windows.Media.Color.FromRgb(255, 159, 10), System.Windows.Media.Color.FromRgb(255, 140, 0), System.Windows.Media.Color.FromRgb(255, 159, 10)),
            "Pink" => (System.Windows.Media.Color.FromRgb(255, 0, 170), System.Windows.Media.Color.FromRgb(232, 17, 135), System.Windows.Media.Color.FromRgb(255, 0, 170)),
            "Yellow" => (System.Windows.Media.Color.FromRgb(255, 200, 0), System.Windows.Media.Color.FromRgb(255, 185, 0), System.Windows.Media.Color.FromRgb(255, 200, 0)),
            _ => (System.Windows.Media.Color.FromRgb(255, 0, 0), System.Windows.Media.Color.FromRgb(204, 0, 0), System.Windows.Media.Color.FromRgb(255, 0, 0))
        };
    }
    
    private void UpdateResourceColors(string colorName)
    {
        var (primary, secondary, glow) = GetThemeColors(colorName);
        
        // Update application resources for dynamic theme colors
        if (System.Windows.Application.Current.Resources.Contains("KrakenAccentColor"))
        {
            System.Windows.Application.Current.Resources["KrakenAccentColor"] = primary;
        }
        else
        {
            System.Windows.Application.Current.Resources.Add("KrakenAccentColor", primary);
        }
        
        if (System.Windows.Application.Current.Resources.Contains("KrakenAccentSecondary"))
        {
            System.Windows.Application.Current.Resources["KrakenAccentSecondary"] = secondary;
        }
        else
        {
            System.Windows.Application.Current.Resources.Add("KrakenAccentSecondary", secondary);
        }
        
        if (System.Windows.Application.Current.Resources.Contains("KrakenGlowColor"))
        {
            System.Windows.Application.Current.Resources["KrakenGlowColor"] = glow;
        }
        else
        {
            System.Windows.Application.Current.Resources.Add("KrakenGlowColor", glow);
        }
    }
}
