using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ObjectViewer.Core.Graph;
using ObjectViewer.Wpf.Controls;

namespace ObjectViewer.Demo.Controls;

/// <summary>Per-document view: description banner + the shared ObjectViewerControl instance.</summary>
public partial class DocumentView : UserControl
{
    public DocumentView()
    {
        InitializeComponent();
    }

    /// <summary>Bind a document and its (already loaded) viewer; chips describe edit policy and type.</summary>
    public void Bind(SampleDocument document, ObjectViewerControl viewer)
    {
        DataContext = document;
        ViewerHost.Content = viewer;
        FilePathText.Text = document.FilePath;
        BuildPolicyChip(document);
    }

    private void BuildPolicyChip(SampleDocument document)
    {
        var policy = document.Sample.Manifest.EditPolicy;
        (string Text, string BackgroundKey, string ForegroundKey) chip = policy switch
        {
            EditPolicyMode.PublicSetters => ("✎ 可编辑", "BadgeGreenBackground", "BadgeGreenForeground"),
            EditPolicyMode.ExplicitlyAllowedMembers => ("🔒 白名单编辑", "BadgeAmberBackground", "BadgeAmberForeground"),
            EditPolicyMode.FullTrust => ("⚠ 完全信任", "BadgeAmberBackground", "BadgeAmberForeground"),
            _ => ("只读", "BadgeGrayBackground", "BadgeGrayForeground"),
        };

        ChipPanel.Children.Add(new Border
        {
            Background = (Brush)FindResource(chip.BackgroundKey),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"EditPolicy: {policy}",
            Child = new TextBlock
            {
                Text = chip.Text,
                FontSize = 10.5,
                Foreground = (Brush)FindResource(chip.ForegroundKey),
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
    }
}
