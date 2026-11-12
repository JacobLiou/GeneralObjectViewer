using CommunityToolkit.Mvvm.ComponentModel;
using ObjectViewer.Core.Graph;

namespace ObjectViewer.Wpf.ViewModels;

/// <summary>
/// Presentation wrapper around a Core <see cref="ObjectNode"/> (Proposal §16.2).
/// Holds only UI state (expansion, busy); all data comes from the core node.
/// </summary>
public partial class ObjectNodeViewModel : ObservableObject
{
    private readonly ObjectViewerViewModel _owner;
    private readonly List<ObjectNodeViewModel> _childVms = [];

    public ObjectNodeViewModel(ObjectNode node, ObjectNodeViewModel? parent, ObjectViewerViewModel owner)
    {
        Node = node;
        Parent = parent;
        _owner = owner;
    }

    public ObjectNode Node { get; }

    public ObjectNodeViewModel? Parent { get; }

    public IReadOnlyList<ObjectNodeViewModel> ChildVms => _childVms;

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private bool isBusy;

    public string Name => Node.Name;

    public string ValueText => Node.Display.Text;

    public string TypeText => Node.Display.TypeText;

    public string DeclaredTypeText => Core.Metadata.TypeNames.Get(Node.DeclaredType);

    public string ToolTip => Node.Display.ToolTip ?? Node.Display.Text;

    public bool IsLoadMore => Node.Kind == ObjectNodeKind.LoadMore;

    /// <summary>Rebuilds child view models from the core node after a load (used by expand and failed retries).</summary>
    public void RebuildChildVms()
    {
        _childVms.Clear();
        foreach (var child in Node.Children)
            _childVms.Add(new ObjectNodeViewModel(child, this, _owner));
    }

    /// <summary>Appends view models for nodes added by a LoadMore page (marker replacement).</summary>
    public void AppendChildVms(IEnumerable<ObjectNode> nodes)
    {
        foreach (var node in nodes)
            _childVms.Add(new ObjectNodeViewModel(node, this, _owner));
    }

    public bool RemoveChildVm(ObjectNodeViewModel child)
        => _childVms.Remove(child);
}
