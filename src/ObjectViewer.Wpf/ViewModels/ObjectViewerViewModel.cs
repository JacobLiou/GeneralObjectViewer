using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObjectViewer.Core.Graph;
using ObjectViewer.Wpf.Collections;

namespace ObjectViewer.Wpf.ViewModels;

/// <summary>
/// Viewer-level view model: owns the current inspection session, the flattened
/// visible projection and user commands (Proposal §16.2). Background loads run
/// through Task.Run so slow business getters never block the UI thread (NFR-004).
/// </summary>
public partial class ObjectViewerViewModel : ObservableObject
{
    private InspectionSession? _session;

    [ObservableProperty]
    private VisibleObjectNode? selectedRow;

    public VisibleNodeCollection VisibleNodes { get; } = [];

    public InspectionSession? Session => _session;

    public async Task SetRootAsync(InspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Create the new session first, then cancel and release the old one (Proposal §25.2).
        var newSession = InspectionSessionFactory.Start(request);
        var oldSession = _session;
        _session = newSession;

        VisibleNodes.Clear();
        if (oldSession is not null)
            await oldSession.DisposeAsync();

        var rootVm = new ObjectNodeViewModel(newSession.Root!, parent: null, owner: this);
        VisibleNodes.Reset(rootVm);

        // Like VS Locals, auto-expand the root for immediate feedback.
        await ToggleAsync(rootVm);
    }

    [RelayCommand]
    public async Task ToggleAsync(VisibleObjectNode? row)
    {
        if (row is null)
            return;
        await ToggleAsync(row.Node);
    }

    internal async Task ToggleAsync(ObjectNodeViewModel vm)
    {
        if (vm.Node.Kind == ObjectNodeKind.LoadMore)
        {
            await LoadMoreAsync(vm);
            return;
        }

        if (!vm.Node.CanExpand)
            return;

        if (vm.IsExpanded)
        {
            vm.IsExpanded = false;
            VisibleNodes.Collapse(vm);
            return;
        }

        vm.IsExpanded = true;
        if (vm.Node.LoadState is NodeLoadState.NotLoaded or NodeLoadState.Failed or NodeLoadState.Cancelled)
        {
            var session = _session;
            if (session is null)
                return;
            vm.IsBusy = true;
            try
            {
                await Task.Run(() => session.Explorer.LoadChildrenAsync(vm.Node, session.SessionToken));
                if (!ReferenceEquals(_session, session) || session.IsDisposed)
                    return; // stale result from a previous root
                vm.RebuildChildVms();
            }
            finally
            {
                vm.IsBusy = false;
            }
        }

        VisibleNodes.Expand(vm);
    }

    private async Task LoadMoreAsync(ObjectNodeViewModel marker)
    {
        var collectionVm = marker.Parent;
        var session = _session;
        if (collectionVm is null || session is null)
            return;

        marker.IsBusy = true;
        try
        {
            var result = await Task.Run(() => session.Explorer.LoadMoreAsync(collectionVm.Node, session.SessionToken));
            if (!ReferenceEquals(_session, session) || result.Outcome != NodeLoadOutcome.Success)
                return;

            // Replace the marker view model with the new page (and the next marker, if any).
            _ = collectionVm.RemoveChildVm(marker);
            collectionVm.AppendChildVms(result.Children);
            var newRows = collectionVm.ChildVms.TakeLast(result.Children.Count).ToList();
            VisibleNodes.ReplaceMarker(marker, newRows);
        }
        finally
        {
            marker.IsBusy = false;
        }
    }

    [RelayCommand]
    private void CopyValue(VisibleObjectNode? row)
        => CopyToClipboard(row?.Node.ValueText);

    [RelayCommand]
    private void CopyDisplayPath(VisibleObjectNode? row)
        => CopyToClipboard(row?.Node.Node.Path.ToDisplayPath());

    [RelayCommand]
    private void CopyStablePath(VisibleObjectNode? row)
        => CopyToClipboard(row?.Node.Node.Path.ToStablePath());

    private static void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard can be locked by another process; copy failures are non-fatal.
        }
    }
}
