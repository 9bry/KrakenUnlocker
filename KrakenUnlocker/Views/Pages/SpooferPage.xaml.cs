using System.Windows.Controls;
using KrakenUnlocker.ViewModels.Pages;

namespace KrakenUnlocker.Views.Pages
{
    public partial class SpooferPage : Page
    {
        public SpooferViewModel ViewModel { get; }

        public SpooferPage(SpooferViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }
    }
}
