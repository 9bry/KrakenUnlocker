using Wpf.Ui.Controls;
using Wpf.Ui.Contracts;

namespace KrakenUnlocker.Services
{
    public class FallbackContentDialogService : IContentDialogService
    {
        private System.Windows.Controls.ContentPresenter? _presenter;

        public void SetContentPresenter(System.Windows.Controls.ContentPresenter contentPresenter)
            => _presenter = contentPresenter;

        public System.Windows.Controls.ContentPresenter GetContentPresenter()
            => _presenter ?? new System.Windows.Controls.ContentPresenter();

        public Task<ContentDialogResult> ShowSimpleDialogAsync(
            SimpleContentDialogCreateOptions options,
            CancellationToken cancellationToken = default)
        {
            var result = ContentDialogResult.None;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var win = new System.Windows.Window
                {
                    Title = options.Title ?? "",
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x0D, 0x0D, 0x0D)),
                    Foreground = System.Windows.Media.Brushes.White,
                    Width = 520,
                    MinHeight = 180,
                    SizeToContent = System.Windows.SizeToContent.Height,
                    MaxHeight = 700,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    Owner = System.Windows.Application.Current.MainWindow,
                    ResizeMode = System.Windows.ResizeMode.NoResize,
                    WindowStyle = System.Windows.WindowStyle.ToolWindow
                };

                var root = new System.Windows.Controls.Grid
                {
                    Margin = new System.Windows.Thickness(24)
                };
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
                    { Height = System.Windows.GridLength.Auto });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
                    { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
                    { Height = System.Windows.GridLength.Auto });

                var titleBlock = new System.Windows.Controls.TextBlock
                {
                    Text = options.Title ?? "",
                    FontSize = 18,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new System.Windows.Thickness(0, 0, 0, 14)
                };
                System.Windows.Controls.Grid.SetRow(titleBlock, 0);
                root.Children.Add(titleBlock);

                if (options.Content is System.Windows.UIElement uiContent)
                {
                    System.Windows.Controls.Grid.SetRow(uiContent, 1);
                    root.Children.Add(uiContent);
                }
                else
                {
                    var text = new System.Windows.Controls.TextBlock
                    {
                        Text = options.Content?.ToString() ?? "",
                        TextWrapping = System.Windows.TextWrapping.Wrap,
                        Foreground = System.Windows.Media.Brushes.LightGray,
                        FontSize = 13
                    };
                    System.Windows.Controls.Grid.SetRow(text, 1);
                    root.Children.Add(text);
                }

                var btnPanel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Margin = new System.Windows.Thickness(0, 14, 0, 0)
                };
                System.Windows.Controls.Grid.SetRow(btnPanel, 2);

                if (!string.IsNullOrEmpty(options.PrimaryButtonText))
                {
                    var btn = new System.Windows.Controls.Button
                    {
                        Content = options.PrimaryButtonText,
                        Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0xCC, 0x00, 0x00)),
                        Foreground = System.Windows.Media.Brushes.White,
                        BorderThickness = new System.Windows.Thickness(0),
                        Padding = new System.Windows.Thickness(20, 8, 20, 8),
                        FontWeight = System.Windows.FontWeights.SemiBold,
                        Margin = new System.Windows.Thickness(0, 0, 8, 0)
                    };
                    btn.Click += (s, e) => { result = ContentDialogResult.Primary; win.Close(); };
                    btnPanel.Children.Add(btn);
                }

                if (!string.IsNullOrEmpty(options.SecondaryButtonText))
                {
                    var btn = new System.Windows.Controls.Button
                    {
                        Content = options.SecondaryButtonText,
                        Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A)),
                        Foreground = System.Windows.Media.Brushes.White,
                        BorderThickness = new System.Windows.Thickness(0),
                        Padding = new System.Windows.Thickness(20, 8, 20, 8),
                        Margin = new System.Windows.Thickness(0, 0, 8, 0)
                    };
                    btn.Click += (s, e) => { result = ContentDialogResult.Secondary; win.Close(); };
                    btnPanel.Children.Add(btn);
                }

                if (!string.IsNullOrEmpty(options.CloseButtonText))
                {
                    var btn = new System.Windows.Controls.Button
                    {
                        Content = options.CloseButtonText,
                        Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A)),
                        Foreground = System.Windows.Media.Brushes.Gray,
                        BorderThickness = new System.Windows.Thickness(0),
                        Padding = new System.Windows.Thickness(20, 8, 20, 8)
                    };
                    btn.Click += (s, e) => { result = ContentDialogResult.None; win.Close(); };
                    btnPanel.Children.Add(btn);
                }

                root.Children.Add(btnPanel);
                win.Content = root;
                win.ShowDialog();
            });

            return Task.FromResult(result);
        }

        public Task<ContentDialogResult> ShowAlertAsync(
            string title, string message, string closeButtonText,
            CancellationToken cancellationToken = default)
        {
            System.Windows.MessageBox.Show(message, title,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return Task.FromResult(ContentDialogResult.None);
        }
    }
}
