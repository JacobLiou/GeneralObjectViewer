using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ObjectViewer.Core.Graph;
using ObjectViewer.Wpf.ViewModels;

namespace ObjectViewer.Wpf.Controls;

/// <summary>
/// Reusable object viewer control (Proposal §16.1). The control forwards work to
/// the view model; no reflection happens in code-behind.
/// </summary>
public partial class ObjectViewerControl : UserControl
{
    private const double EditPopupWidth = 380;

    public ObjectViewerControl()
    {
        InitializeComponent();
        ViewModel = new ObjectViewerViewModel();
        ViewModel.RequestScrollIntoView = index => PartGrid.ScrollIntoView(VisibleNodes[index]);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.RequestEditFocus = () => Dispatcher.BeginInvoke(() => FocusEditBox());
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
        else if (e.Key == Key.F2)
        {
            // Controlled editing: no-op unless the row is editable (PRD FR-250).
            ViewModel.BeginEditCommand.Execute(row);
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

        if (row.Node.CanEdit)
        {
            // MouseDoubleClick fires on the second mouse-down, so the trailing
            // mouse-up is still in flight. A StaysOpen=false popup opened now
            // treats that pending up as an outside click and closes instantly.
            // Defer to Background (below input priority) so the gesture fully
            // completes before the popup takes mouse capture (FR-122).
            Dispatcher.InvokeAsync(
                () => ViewModel.BeginEditCommand.Execute(row), // editable row: open the inline editor
                DispatcherPriority.Background);
        }
        else
            _ = ViewModel.ToggleAsync(row);           // otherwise keep expand/collapse
        e.Handled = true;
    }

    // ---------- Inline edit popup (方案A) ----------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Position the popup before the IsEditing binding opens it.
        if (e.PropertyName == nameof(ObjectViewerViewModel.IsEditing) && ViewModel.IsEditing)
            PositionEditPopup();
    }

    /// <summary>Anchors the editor under the edited row (grid center as a virtualization fallback).</summary>
    private void PositionEditPopup()
    {
        EditPopup.Placement = PlacementMode.RelativePoint;
        EditPopup.PlacementTarget = PartGrid;

        var row = ViewModel.SelectedRow;
        if (row is not null
            && PartGrid.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow container)
        {
            var origin = container.TranslatePoint(new Point(0, 0), PartGrid);
            EditPopup.HorizontalOffset = Math.Clamp(origin.X, 0, Math.Max(0, PartGrid.ActualWidth - EditPopupWidth));
            EditPopup.VerticalOffset = Math.Clamp(
                origin.Y + container.ActualHeight + 2, 0, Math.Max(0, PartGrid.ActualHeight - 150));
            return;
        }

        EditPopup.HorizontalOffset = Math.Max(0, (PartGrid.ActualWidth - EditPopupWidth) / 2);
        EditPopup.VerticalOffset = 48;
    }

    private void OnEditPopupOpened(object sender, EventArgs e)
        => FocusEditBox();

    private void FocusEditBox()
    {
        if (!EditPopup.IsOpen)
            return;
        EditTextBox.Focus();
        EditTextBox.SelectAll();
    }
}
