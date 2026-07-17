using KrakenUnlocker.ViewModels.Pages;
using KrakenUnlocker.Xbox360;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KrakenUnlocker.Views.Pages
{
    public partial class Xbox360Page
    {
        public Xbox360ViewModel ViewModel { get; }

        public Xbox360Page()
        {
            InitializeComponent();
            ViewModel = App.GetService<Xbox360ViewModel>()!;
            DataContext = ViewModel;
        }

        private async void TreeItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem tvi && tvi.DataContext is Xbox360FileItem item)
                await ViewModel.ExpandFolderCommand.ExecuteAsync(item);
        }

        private async void DownloadSelected_Click(object sender, RoutedEventArgs e)
            => await ViewModel.DownloadFileCommand.ExecuteAsync(FileTree.SelectedItem as Xbox360FileItem);

        private async void UploadToSelected_Click(object sender, RoutedEventArgs e)
            => await ViewModel.UploadFileCommand.ExecuteAsync(FileTree.SelectedItem as Xbox360FileItem);

        private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var item = FileTree.SelectedItem as Xbox360FileItem;
            if (item == null) return;
            var r = System.Windows.MessageBox.Show($"Delete {item.Name} from console?",
                "Confirm Delete", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (r == System.Windows.MessageBoxResult.Yes)
                await ViewModel.DeleteFileCommand.ExecuteAsync(item);
        }

        private async void GoConsole_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Task.Run(() => XbdmBridge.DmGo());
                ViewModel.StatusText = "Console resumed.";
            }
            catch { ViewModel.StatusText = "Failed to resume console."; }
        }

        private async void StopConsole_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Task.Run(() => XbdmBridge.DmStop());
                ViewModel.StatusText = "Console frozen.";
            }
            catch { ViewModel.StatusText = "Failed to freeze console."; }
        }

        private async void SendRawCommand_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.SendCommandCommand.ExecuteAsync(RawCommandBox.Text);
            RawCommandBox.Text = "";
        }

        private async void RawCommand_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await ViewModel.SendCommandCommand.ExecuteAsync(RawCommandBox.Text);
                RawCommandBox.Text = "";
            }
        }

        private void BrowseSavePath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select local save folder",
                SelectedPath = ViewModel.LocalSavePath
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                ViewModel.LocalSavePath = dlg.SelectedPath;
        }
    }

    public class InverseBoolConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
            => value is bool b ? !b : value;
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c)
            => value is bool b ? !b : value;
    }

    public class InverseBoolToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
            => value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c)
            => value is Visibility v && v == Visibility.Collapsed;
    }

    public class NullToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c)
            => value;
    }
}
