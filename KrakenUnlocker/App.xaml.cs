using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;
using Wpf.Ui.Services;

using KrakenUnlocker.Services;
using KrakenUnlocker.ViewModels.Pages;
using KrakenUnlocker.ViewModels.Windows;
using KrakenUnlocker.Views.Pages;
using KrakenUnlocker.Views.Windows;
using System.Windows;

namespace KrakenUnlocker;

public partial class App
{
    private static readonly IHost Host = Microsoft.Extensions.Hosting.Host
        .CreateDefaultBuilder()
        .ConfigureServices((_, services) =>
        {
            services.AddHostedService<ApplicationHostService>();

            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<ISnackbarService, KrakenSnackbarService>();
            services.AddSingleton<IContentDialogService, FallbackContentDialogService>();

            services.AddSingleton<HomePage>();
            services.AddSingleton<HomeViewModel>();
            services.AddSingleton<SettingsPage>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<GamesPage>();
            services.AddSingleton<GamesViewModel>();
            services.AddSingleton<AchievementsPage>();
            services.AddSingleton<AchievementsViewModel>();
            services.AddSingleton<QueuePage>();
            services.AddSingleton<QueueViewModel>();
            services.AddSingleton<Xbox360Page>();
            services.AddSingleton<Xbox360ViewModel>();
            services.AddSingleton<PlaceholderPage>();
            services.AddSingleton<StatsPage>();
            services.AddSingleton<StatsViewModel>();
            services.AddSingleton<MiscPage>();
            services.AddSingleton<MiscViewModel>();
            services.AddSingleton<InfoPage>();
            services.AddSingleton<InfoViewModel>();
            services.AddSingleton<LicenseViewModel>();
            services.AddSingleton<DebugPage>();
            services.AddSingleton<DebugViewModel>();
        }).Build();

    public static T? GetService<T>() where T : class
    {
        return Host.Services.GetService(typeof(T)) as T;
    }

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        await Host.StartAsync();
        SetupExceptionHandling();

        // ── Anti-Debugger Check ───────────────────────────────────────────────
        if (System.Diagnostics.Debugger.IsAttached)
        {
            System.Windows.Application.Current.Shutdown();
            return;
        }

        // ── Integrity check ───────────────────────────────────────────────────
        await CheckIntegrityAsync();

        // ── Update check ──────────────────────────────────────────────────────
        await CheckForUpdateAsync();

        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    private static async Task CheckIntegrityAsync()
    {
        try
        {
            var result = await IntegrityService.CheckAsync();
            if (result == IntegrityService.IntegrityResult.Tampered)
            {
                System.Windows.MessageBox.Show(
                    "This copy of KrakenXboxUnlocker has been modified and cannot be trusted.\n\nPlease download the official version from github.com/9bry/KrakenUnlocker",
                    "Integrity Check Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                System.Windows.Application.Current.Shutdown();
            }
            // Unverifiable = offline or hash file missing — allow but don't block
        }
        catch { }
    }

    private static async Task CheckForUpdateAsync()
    {
        try
        {
            var result = await UpdateService.CheckForUpdateAsync();

            if (result.Severity == UpdateService.UpdateSeverity.None)
                return;

            if (result.Severity == UpdateService.UpdateSeverity.Hard)
            {
                // 2+ versions behind — hard block ALL features, no way past
                UpdateBlocker.IsHardBlocked = true;
                UpdateBlocker.IsUpdatePending = true;

                var updateWindow = new UpdateWindow(result.LatestRelease!, hardBlock: true);
                updateWindow.ShowDialog();

                // If somehow window closes (it shouldn't), shut down completely
                System.Windows.Application.Current.Shutdown();
                return;
            }

            // Soft block — 1 version behind, premium locked only
            UpdateBlocker.IsUpdatePending = true;
            var softWindow = new UpdateWindow(result.LatestRelease!, hardBlock: false);
            softWindow.ShowDialog();
        }
        catch
        {
            // Failsafe: if the update check infrastructure crashes entirely, lock the app down.
            System.Windows.MessageBox.Show(
                "A critical error occurred while verifying the application version. Please ensure you have an active internet connection.\n\nThe application will now close.",
                "Verification Failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            System.Windows.Application.Current.Shutdown();
        }
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        // Kill session on close — user must re-login next time
        LicenseService.Logout();
        await Host.StopAsync();
        Host.Dispose();
    }

    private void SetupExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            ReportException((Exception)e.ExceptionObject);
        };

        DispatcherUnhandledException += (_, e) =>
        {
            ReportException(e.Exception);
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ReportException(e.Exception);
            e.SetObserved();
        };
    }
    private static void ReportException(Exception exception)
    {
        var mainWindowViewModel = GetService<MainWindowViewModel>();
        mainWindowViewModel?.ShowErrorDialog(exception);
    }
}
