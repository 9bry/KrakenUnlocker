using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfPoint = System.Windows.Point;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfHA = System.Windows.HorizontalAlignment;

namespace KrakenUnlocker.Services
{
    public enum ToastType { Success, Warning, Error, Info }

    public static class KrakenToast
    {
        private static System.Windows.Controls.Panel? _host;

        public static void SetHost(System.Windows.Controls.Panel host) => _host = host;

        public static void Show(string title, string message, ToastType type = ToastType.Info, int durationMs = 3500)
        {
            if (_host == null) return;
            _host.Dispatcher.Invoke(() => ShowInternal(title, message, type, durationMs));
        }

        private static void ShowInternal(string title, string message, ToastType type, int durationMs)
        {
            if (_host == null) return;

            var (accent, icon) = type switch
            {
                ToastType.Success => ("#CC0000", "✓"),
                ToastType.Warning => ("#AA0000", "⚠"),
                ToastType.Error   => ("#880000", "✕"),
                _                 => ("#CC0000", "ℹ"),
            };

            // Outer container
            var toast = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromRgb(0x0D, 0x0D, 0x0D)),
                BorderBrush = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(accent)),
                BorderThickness = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(8),
                MinWidth = 320,
                MaxWidth = 480,
                Margin = new Thickness(0, 0, 0, 10),
                Opacity = 0,
                RenderTransformOrigin = new WpfPoint(0.5, 1),
                RenderTransform = new TranslateTransform(0, 30),
                HorizontalAlignment = WpfHA.Right
            };

            // Left accent bar
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bar = new Border
            {
                Background = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(accent)),
                CornerRadius = new CornerRadius(8, 0, 0, 8)
            };
            Grid.SetColumn(bar, 0);
            grid.Children.Add(bar);

            // Icon
            var iconBlock = new TextBlock
            {
                Text = icon,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(accent)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 12, 10, 12)
            };
            Grid.SetColumn(iconBlock, 1);
            grid.Children.Add(iconBlock);

            // Text
            var textPanel = new StackPanel { Margin = new Thickness(0, 12, 16, 12), VerticalAlignment = VerticalAlignment.Center };
            textPanel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = WpfBrushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (!string.IsNullOrEmpty(message))
            {
                textPanel.Children.Add(new TextBlock
                {
                    Text = message,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(0xAA, 0xAA, 0xAA)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0),
                    MaxWidth = 340
                });
            }
            Grid.SetColumn(textPanel, 2);
            grid.Children.Add(textPanel);

            // Bottom progress bar
            var progressGrid = new Grid();
            var progressBg = new Border { Background = new SolidColorBrush(WpfColor.FromRgb(0x1A, 0x1A, 0x1A)), Height = 2, CornerRadius = new CornerRadius(0, 0, 8, 8) };
            var progressFill = new Border
            {
                Background = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(accent)),
                Height = 2,
                HorizontalAlignment = WpfHA.Left,
                Width = double.NaN,
                CornerRadius = new CornerRadius(0, 0, 8, 8)
            };
            progressGrid.Children.Add(progressBg);
            progressGrid.Children.Add(progressFill);

            var outerStack = new StackPanel();
            outerStack.Children.Add(grid);
            outerStack.Children.Add(progressGrid);

            toast.Child = outerStack;
            toast.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = (WpfColor)WpfColorConverter.ConvertFromString(accent),
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.5
            };

            _host.Children.Add(toast);

            // Animate in
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            var slideIn = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            toast.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            ((TranslateTransform)toast.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slideIn);

            // Animate progress bar
            progressFill.Loaded += (s, e) =>
            {
                progressFill.Width = progressBg.ActualWidth;
                var shrink = new DoubleAnimation(progressFill.Width, 0, TimeSpan.FromMilliseconds(durationMs))
                {
                    EasingFunction = new SineEase()
                };
                progressFill.BeginAnimation(FrameworkElement.WidthProperty, shrink);
            };

            // Dismiss after duration
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                fadeOut.Completed += (_, _) => _host?.Children.Remove(toast);
                toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
            timer.Start();

            // Click to dismiss
            toast.MouseLeftButtonDown += (s, e) =>
            {
                timer.Stop();
                _host?.Children.Remove(toast);
            };
        }
    }
}



