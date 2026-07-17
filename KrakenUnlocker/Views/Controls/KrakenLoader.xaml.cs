using System.Windows;
using System.Windows.Controls;

namespace KrakenUnlocker.Views.Controls;

public partial class KrakenLoader : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(KrakenLoader),
            new PropertyMetadata("Syncing library…"));

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public KrakenLoader()
    {
        InitializeComponent();
    }
}
