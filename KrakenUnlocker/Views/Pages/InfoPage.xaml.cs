using KrakenUnlocker.ViewModels.Pages;

namespace KrakenUnlocker.Views.Pages
{
    public partial class InfoPage
    {
        public InfoViewModel ViewModel { get; }
        public LicenseViewModel LicenseVM { get; }

        public InfoPage(InfoViewModel viewModel, LicenseViewModel licenseViewModel)
        {
            ViewModel = viewModel;
            LicenseVM = licenseViewModel;
            DataContext = this;
            InitializeComponent();
        }
    }
}
