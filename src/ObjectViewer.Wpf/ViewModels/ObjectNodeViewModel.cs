using CommunityToolkit.Mvvm.ComponentModel;
using ObjectViewer.Core.Graph;

namespace ObjectViewer.Wpf.ViewModels;

/// <summary>
/// Presentation wrapper around a Core <see cref="ObjectNode"/> (Proposal §16.2).
/// All display-facing properties are INPC so a refresh can update visible cells
/// in place; glyphs and state badges derive from the core node.
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

    [ObservableProperty]
    private bool isChanged;

    public string Name => Node.Name;

    public string ValueText => Node.Display.Text;

    public string TypeText => Node.Display.TypeText;

    public string DeclaredTypeText => Core.Metadata.TypeNames.Get(Node.DeclaredType);

    public string ToolTip => Node.Display.ToolTip ?? Node.Display.Text;

    public int Depth => Node.Depth;

    public bool IsLoadMore => Node.Kind == ObjectNodeKind.LoadMore;

    public bool ShowToggle => Node.CanExpand || IsLoadMore;

    public string ToggleGlyph => IsExpanded ? "▾" : "▸";

    public string KindGlyph => Node.Kind switch
    {
        ObjectNodeKind.LoadMore => "…",
        ObjectNodeKind.Truncated => "…",
        ObjectNodeKind.Error => "⚠",
        _ => Node.ValueKind switch
        {
            ObjectValueKind.Null => "∅",
            ObjectValueKind.Primitive => "●",
            ObjectValueKind.String => "”",
            ObjectValueKind.Enum => "⊙",
            ObjectValueKind.ByteArray => "▒",
            ObjectValueKind.Collection => "▦",
            ObjectValueKind.Dictionary => "▤",
            ObjectValueKind.Reference => "↩",
            _ => "◆",
        },
    };

    public string StateGlyph
    {
        get
        {
            if (IsBusy)
                return "…";
            if (Node.Error is not null || Node.Kind == ObjectNodeKind.Error)
                return "⚠";
            if (Node.Display.IsTruncated)
                return "…";
            return string.Empty;
        }
    }

    public bool IsMasked => Node.Display.IsMasked;

    public bool HasError => Node.Error is not null || Node.Kind == ObjectNodeKind.Error;

    /// <summary>Raises change notifications for every display-facing property after a refresh (PRD FR-105).</summary>
    public void RaiseDataChanged()
    {
        IsChanged = Node.HasChanged;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(DeclaredTypeText));
        OnPropertyChanged(nameof(ToolTip));
        OnPropertyChanged(nameof(ShowToggle));
        OnPropertyChanged(nameof(ToggleGlyph));
        OnPropertyChanged(nameof(KindGlyph));
        OnPropertyChanged(nameof(StateGlyph));
        OnPropertyChanged(nameof(IsMasked));
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ToggleGlyph));
        OnPropertyChanged(nameof(ShowToggle));
    }

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(StateGlyph));

    /// <summary>Rebuilds child view models from the core node after a load.</summary>
    public void RebuildChildVms()
    {
        _childVms.Clear();
        foreach (var child in Node.Children)
            _childVms.Add(new ObjectNodeViewModel(child, this, _owner));
    }

    /// <summary>Appends view models for nodes added by a LoadMore page.</summary>
    public void AppendChildVms(IEnumerable<ObjectNode> nodes)
    {
        foreach (var node in nodes)
            _childVms.Add(new ObjectNodeViewModel(node, this, _owner));
    }

    public bool RemoveChildVm(ObjectNodeViewModel child)
        => _childVms.Remove(child);
}
