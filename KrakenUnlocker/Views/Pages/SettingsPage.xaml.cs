using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;
using KrakenUnlocker.ViewModels.Pages;

namespace KrakenUnlocker.Views.Pages
{
    public partial class SettingsPage 
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }

        private void TimeFormat_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ViewModel.SaveSettings();
        }
    }
}
