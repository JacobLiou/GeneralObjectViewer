using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ObjectViewer.Core.Graph;
using ObjectViewer.Wpf.ViewModels;

namespace ObjectViewer.Wpf.Controls;

/// <summary>
/// Reusable object viewer control (Proposal §16.1). The control forwards work to
/// the view model; no reflection happens in code-behind.
/// </summary>
public partial class ObjectViewerControl : UserControl
{
    public ObjectViewerControl()
    {
        InitializeComponent();
        ViewModel = new ObjectViewerViewModel();
        ViewModel.RequestScrollIntoView = index => PartGrid.ScrollIntoView(VisibleNodes[index]);
        DataContext = ViewModel;
    }

    public ObjectViewerViewModel ViewModel { get; }

    private ObjectViewer.Wpf.Collections.VisibleNodeCollection VisibleNodes => ViewModel.VisibleNodes;

    /// <summary>Sets a new root object; the previous session is cancelled and released (PRD FR-002/005).</summary>
    public Task SetRootAsync(InspectionRequest request)
        => ViewModel.SetRootAsync(request);

    private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel.SelectedRow is not { } row)
            return;

        if (e.Key == Key.Enter)
        {
            _ = ViewModel.ToggleAsync(row);
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Clipboard.SetText(string.IsNullOrEmpty(row.Node.ValueText) ? string.Empty : row.Node.ValueText);
            e.Handled = true;
        }
    }

    private void OnGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedRow is not { } row)
            return;
        _ = ViewModel.ToggleAsync(row);
        e.Handled = true;
    }
}
