using System.Windows;
using System.Windows.Controls;

namespace ObjectViewer.Demo.Controls;

/// <summary>Welcome page shown when no document is open; cards open the corresponding sample.</summary>
public partial class StartPage : UserControl
{
    public StartPage()
    {
        InitializeComponent();
    }

    public event Action<SampleFile>? SampleRequested;

    public event Action? OpenFileRequested;

    public void ShowSamples(IReadOnlyList<SampleFile> samples)
        => SampleCards.ItemsSource = samples;

    private void OnSampleCardClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SampleFile sample)
            SampleRequested?.Invoke(sample);
    }

    private void OnOpenFileClick(object sender, RoutedEventArgs e)
        => OpenFileRequested?.Invoke();
}
