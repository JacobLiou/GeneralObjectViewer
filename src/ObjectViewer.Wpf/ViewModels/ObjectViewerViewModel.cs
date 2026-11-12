using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Paths;
using ObjectViewer.Core.Search;
using ObjectViewer.Core.Snapshots;
using ObjectViewer.Wpf.Collections;

namespace ObjectViewer.Wpf.ViewModels;

public sealed record SnapshotRecord(string DisplayName, DateTimeOffset CreatedAt, ObjectSnapshot Snapshot);

public sealed record SearchResultItem(string DisplayPath, string Name, string TypeText, string ValueText, ObjectPath Path);

public sealed record DiffResultItem(string State, string DisplayPath, string Detail);

/// <summary>
/// Viewer-level view model: owns the current inspection session, the flattened
/// visible projection, search/refresh/snapshot commands and navigation
/// (Proposal §16.2). Background loads run through Task.Run so slow business
/// getters never block the UI thread (NFR-004).
/// </summary>
public partial class ObjectViewerViewModel : ObservableObject
{
    private InspectionSession? _session;
    private ObjectNodeViewModel? _rootVm;
    private CancellationTokenSource? _searchCancellation;

    [ObservableProperty]
    private VisibleObjectNode? selectedRow;

    [ObservableProperty]
    private string searchText = string.Empty;

    /// <summary>0 = loaded nodes, 1 = bounded deep search.</summary>
    [ObservableProperty]
    private int searchScopeIndex;

    [ObservableProperty]
    private bool highlightChanges = true;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private bool hasBottomContent;

    [ObservableProperty]
    private int bottomTabIndex;

    [ObservableProperty]
    private SnapshotRecord? selectedSnapshot;

    public VisibleNodeCollection VisibleNodes { get; } = [];

    public ObservableCollection<SearchResultItem> SearchResults { get; } = [];

    public ObservableCollection<SnapshotRecord> Snapshots { get; } = [];

    public ObservableCollection<DiffResultItem> DiffResults { get; } = [];

    public InspectionSession? Session => _session;

    public async Task SetRootAsync(InspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Create the new session first, then cancel and release the old one (Proposal §25.2).
        var newSession = InspectionSessionFactory.Start(request);
        var oldSession = _session;
        _session = newSession;

        _searchCancellation?.Cancel();
        SearchResults.Clear();
        DiffResults.Clear();
        Snapshots.Clear();
        SelectedSnapshot = null;
        UpdateBottomState();
        VisibleNodes.Clear();
        if (oldSession is not null)
            await oldSession.DisposeAsync();

        _rootVm = new ObjectNodeViewModel(newSession.Root!, parent: null, owner: this);
        VisibleNodes.Reset(_rootVm);

        // Like VS Locals, auto-expand the root for immediate feedback.
        await ToggleAsync(_rootVm);
        StatusText = $"Root: {request.RootName}";
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

    // ---------- Search (PRD FR-090~095) ----------

    [RelayCommand]
    private async Task SearchAsync()
    {
        var session = _session;
        var text = SearchText?.Trim();
        if (session is null || string.IsNullOrEmpty(text))
            return;

        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;

        SearchResults.Clear();
        BottomTabIndex = 0;

        try
        {
            if (SearchScopeIndex == 0)
            {
                var results = await Task.Run(() => NodeSearch.SearchLoaded(session.Root!, new SearchQuery(text)), token);
                foreach (var result in results)
                    SearchResults.Add(new SearchResultItem(
                        result.Node.Path.ToDisplayPath(),
                        result.Node.Name,
                        result.Node.Display.TypeText,
                        result.Node.Display.Text,
                        result.Node.Path));
                StatusText = $"已加载搜索 \"{text}\": {results.Count} 个结果(未触发任何 Getter)";
            }
            else
            {
                var result = await Task.Run(
                    () => new DeepSearchService(session).SearchAsync(new SearchQuery(text), DeepSearchOptions.Default, token),
                    token);
                foreach (var match in result.Matches)
                    SearchResults.Add(new SearchResultItem(
                        match.Path.ToDisplayPath(), match.Name, match.TypeText, match.ValueText, match.Path));
                StatusText = $"深度搜索 \"{text}\": {result.Matches.Count} 个结果,访问 {result.NodesVisited} 个节点"
                    + (result.Truncated ? "(结果可能不完整: 达到深度/节点数/超时边界)" : string.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        UpdateBottomState();
    }

    [RelayCommand]
    private async Task NavigateToResultAsync(SearchResultItem? item)
    {
        if (item is null)
            return;
        var found = await NavigateToAsync(item.Path);
        if (!found)
            StatusText = $"无法定位: {item.DisplayPath}(对象可能已变化)";
    }

    /// <summary>Expands ancestors along a stable path and selects the target row (PRD FR-091).</summary>
    private async Task<bool> NavigateToAsync(ObjectPath path)
    {
        var session = _session;
        var current = _rootVm;
        if (session is null || current is null)
            return false;

        for (var length = 1; length <= path.Segments.Count; length++)
        {
            var prefix = new ObjectPath(path.Segments.Take(length).ToArray());

            if (current.Node.LoadState is NodeLoadState.NotLoaded or NodeLoadState.Failed or NodeLoadState.Cancelled)
                await ToggleAsync(current); // loads and expands

            ObjectNode? next = null;
            foreach (var child in current.Node.Children)
            {
                if (child.Path.Equals(prefix))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
                return false;

            var nextVm = current.ChildVms.FirstOrDefault(vm => ReferenceEquals(vm.Node, next));
            if (nextVm is null)
                return false;

            if (length < path.Segments.Count && !nextVm.IsExpanded && nextVm.Node.CanExpand)
            {
                if (nextVm.Node.LoadState is NodeLoadState.NotLoaded or NodeLoadState.Failed or NodeLoadState.Cancelled)
                    await ToggleAsync(nextVm);
                else
                {
                    nextVm.IsExpanded = true;
                    VisibleNodes.Expand(nextVm);
                }
            }

            current = nextVm;
        }

        var index = VisibleNodes.RowIndexOf(current);
        if (index >= 0)
        {
            SelectedRow = VisibleNodes[index];
            RequestScrollIntoView?.Invoke(index);
        }

        return true;
    }

    internal Action<int>? RequestScrollIntoView { get; set; }

    // ---------- Refresh (PRD FR-100~105) ----------

    [RelayCommand]
    private async Task RefreshSelectedAsync()
        => await RefreshCoreAsync(RefreshScope.Node);

    [RelayCommand]
    private async Task RefreshSubtreeAsync()
        => await RefreshCoreAsync(RefreshScope.Subtree);

    [RelayCommand]
    private async Task RefreshAllAsync()
        => await RefreshCoreAsync(RefreshScope.LoadedTree);

    private async Task RefreshCoreAsync(RefreshScope scope)
    {
        var session = _session;
        var rootVm = _rootVm;
        if (session is null || rootVm is null)
            return;

        var target = scope == RefreshScope.LoadedTree ? rootVm : SelectedRow?.Node ?? rootVm;
        var actualScope = scope == RefreshScope.LoadedTree ? RefreshScope.LoadedTree : scope;

        NodeRefreshResult result;
        try
        {
            result = await Task.Run(() => session.Explorer.RefreshAsync(target.Node, actualScope, session.SessionToken));
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ReferenceEquals(_session, session) || session.IsDisposed)
            return;

        await ApplyRefreshAsync(target);
        StatusText = $"刷新完成: {result.RefreshedCount} 个节点,{result.ChangedCount} 个变化,{result.InvalidatedCount} 个已失效重载";
    }

    /// <summary>Syncs view models and visible rows after a core refresh (in-place updates where possible).</summary>
    private async Task ApplyRefreshAsync(ObjectNodeViewModel vm)
    {
        vm.RaiseDataChanged();

        // Structural invalidation: reload if the node was expanded.
        if (vm.Node.Children.Count == 0
            && vm.Node.LoadState == NodeLoadState.NotLoaded
            && vm.Node.Kind != ObjectNodeKind.Root
            && vm.IsExpanded)
        {
            vm.IsExpanded = false;
            VisibleNodes.Collapse(vm);
            await ToggleAsync(vm); // reload and re-expand
            return;
        }

        // Root reload case: root has no accessor, refresh invalidated it.
        if (vm.Node.Kind == ObjectNodeKind.Root && vm.Node.Children.Count == 0 && vm.Node.LoadState == NodeLoadState.NotLoaded && vm.IsExpanded)
        {
            vm.IsExpanded = false;
            VisibleNodes.Collapse(vm);
            await ToggleAsync(vm);
            return;
        }

        foreach (var child in vm.ChildVms.ToList())
            await ApplyRefreshAsync(child);
    }

    // ---------- Snapshots & Diff (PRD FR-110~115) ----------

    [RelayCommand]
    private void CaptureSnapshot()
    {
        var session = _session;
        var rootVm = _rootVm;
        if (session?.Root is null)
            return;

        var name = $"快照 {Snapshots.Count + 1}";
        var snapshot = SnapshotBuilder.Capture(name, session.Root, session.Options);
        var record = new SnapshotRecord($"{name} @ {DateTime.Now:HH:mm:ss}", snapshot.CreatedAt, snapshot);
        Snapshots.Add(record);
        SelectedSnapshot = record;
        StatusText = $"{name} 已创建(仅含已加载节点,敏感值只保存掩码)";
    }

    [RelayCommand]
    private void CompareSnapshots()
    {
        var session = _session;
        var before = SelectedSnapshot?.Snapshot;
        if (session?.Root is null || before is null)
        {
            StatusText = "请先选择一个要对比的快照";
            return;
        }

        var after = SnapshotBuilder.Capture("current", session.Root, session.Options);
        var diff = SnapshotDiffer.Diff(before, after);

        DiffResults.Clear();
        foreach (var difference in diff.Differences)
        {
            var detail = difference.State switch
            {
                SnapshotNodeState.Added => $"+ {difference.After}",
                SnapshotNodeState.Removed => $"- {difference.Before}",
                _ => $"{difference.Before} → {difference.After}",
            };
            DiffResults.Add(new DiffResultItem(difference.State.ToString(), difference.DisplayPath, detail));
        }

        BottomTabIndex = 1;
        UpdateBottomState();
        StatusText = $"对比完成: {diff.Count(SnapshotNodeState.Added)} 新增 / {diff.Count(SnapshotNodeState.Removed)} 移除 / "
            + $"{diff.Count(SnapshotNodeState.Changed)} 变化 / {diff.Count(SnapshotNodeState.TypeChanged)} 类型变化 / {diff.UnchangedCount} 未变化";
    }

    // ---------- Copy ----------

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

    private void UpdateBottomState()
        => HasBottomContent = SearchResults.Count > 0 || DiffResults.Count > 0;
}
