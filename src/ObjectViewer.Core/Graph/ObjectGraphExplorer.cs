using System.Globalization;
using ObjectViewer.Core.Adapters;
using ObjectViewer.Core.Formatting;
using ObjectViewer.Core.Metadata;
using ObjectViewer.Core.Paths;

namespace ObjectViewer.Core.Graph;

/// <summary>
/// Lazy object graph exploration with session/generation/cancellation protection,
/// per-member error isolation, reference detection and budget limits
/// (Proposal §12). All awaits use ConfigureAwait(false); callers decide which
/// thread the work starts on.
/// </summary>
public sealed class ObjectGraphExplorer
{
    private readonly InspectionSession _session;

    internal ObjectGraphExplorer(InspectionSession session)
        => _session = session;

    public InspectionSession Session => _session;

    /// <summary>Creates the root node summary only; no children are loaded (PRD FR-040).</summary>
    public ObjectNode CreateRoot(InspectionRequest request)
    {
        var root = new ObjectNode(_session.NextNodeId(), request.RootName, ObjectNodeKind.Root, ObjectPath.RootPath, 0)
        {
            DeclaredType = request.DeclaredType ?? request.Value?.GetType(),
            RawValue = request.Value,
        };
        ApplyValueSemantics(root, parentChain: null, isSensitive: false, readError: null, isRoot: true);
        _session.Root = root;
        return root;
    }

    /// <summary>Loads the first page of children for a node (user-initiated expansion, PRD FR-041).</summary>
    public async Task<NodeLoadResult> LoadChildrenAsync(ObjectNode node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!node.CanExpand)
            return NodeLoadResult.NotExpandable();
        if (node.LoadState == NodeLoadState.Loading)
            return NodeLoadResult.AlreadyLoading();

        node.Error = null;
        node.LoadState = NodeLoadState.Loading;
        var generation = _session.Generation;

        CancellationTokenSource? linked = null;
        try
        {
            linked = _session.CreateLinkedSource(cancellationToken);
            await _session.WaitLoadGateAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (IsStale(generation))
                {
                    node.LoadState = NodeLoadState.Cancelled;
                    return NodeLoadResult.Stale();
                }

                if (node.Depth + 1 > _session.Options.MaximumDepth)
                {
                    node.ReplaceChildren([CreateTruncatedNode(node, $"Maximum depth ({_session.Options.MaximumDepth}) reached")]);
                    node.LoadState = NodeLoadState.Truncated;
                    return NodeLoadResult.Success(node.Children);
                }

                var adapterContext = new ObjectAdapterContext(node.RawValue, node.DeclaredType, node.Accessor?.MemberInfo);
                var adapter = _session.Adapters.Resolve(adapterContext, _session);
                var page = await adapter.GetChildrenAsync(node, _session, linked.Token).ConfigureAwait(false);

                if (IsStale(generation))
                {
                    node.LoadState = NodeLoadState.Cancelled;
                    return NodeLoadResult.Stale();
                }

                var children = new List<ObjectNode>(page.Children.Count);
                var budgetExhausted = false;
                foreach (var descriptor in page.Children)
                {
                    if (!_session.TryConsumeNodeBudget())
                    {
                        budgetExhausted = true;
                        break;
                    }
                    children.Add(CreateChildNode(node, descriptor));
                }

                if (budgetExhausted)
                {
                    children.Add(CreateTruncatedNode(node, $"Node budget ({_session.Options.MaximumNodesPerSession}) reached"));
                }
                else if (page.IsTruncated)
                {
                    children.Add(CreateTruncatedNode(node, $"Enumerable limit ({_session.Options.MaximumEnumerableItems}) reached"));
                }

                if (page.NextCursor is not null && !budgetExhausted)
                {
                    node.Continuation = page.NextCursor;
                    children.Add(CreateLoadMoreNode(node));
                }
                else
                {
                    node.Continuation = null;
                }

                node.ReplaceChildren(children);
                node.LoadState = NodeLoadState.Loaded;
                return NodeLoadResult.Success(node.Children);
            }
            finally
            {
                _session.ReleaseLoadGate();
            }
        }
        catch (OperationCanceledException)
        {
            node.LoadState = NodeLoadState.Cancelled;
            return NodeLoadResult.Cancelled();
        }
        catch (Exception ex)
        {
            var error = NodeError.FromException(ex, "ExpandFailed");
            node.Error = error;
            node.ReplaceChildren([CreateErrorNode(node, error)]);
            node.LoadState = NodeLoadState.Failed;
            return new NodeLoadResult(NodeLoadOutcome.Failed, node.Children, error);
        }
        finally
        {
            linked?.Dispose();
        }
    }

    /// <summary>
    /// Loads the next collection page. Appends after the existing children and
    /// repositions the LoadMore marker; repeated calls never duplicate items (PRD FR-044).
    /// Returns only the nodes appended by this call.
    /// </summary>
    public async Task<NodeLoadResult> LoadMoreAsync(ObjectNode node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Continuation is null || node.LoadState is not (NodeLoadState.Loaded or NodeLoadState.Truncated))
            return NodeLoadResult.NotExpandable();

        var generation = _session.Generation;
        CancellationTokenSource? linked = null;
        try
        {
            linked = _session.CreateLinkedSource(cancellationToken);
            await _session.WaitLoadGateAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (IsStale(generation))
                    return NodeLoadResult.Stale();

                var adapterContext = new ObjectAdapterContext(node.RawValue, node.DeclaredType, node.Accessor?.MemberInfo);
                var adapter = _session.Adapters.Resolve(adapterContext, _session);
                var page = await adapter.GetChildrenAsync(node, _session, linked.Token).ConfigureAwait(false);

                if (IsStale(generation))
                    return NodeLoadResult.Stale();

                // Remove the trailing LoadMore marker before appending the next page.
                if (node.Children.Count > 0 && node.Children[^1].Kind == ObjectNodeKind.LoadMore)
                    node.RemoveChildAt(node.Children.Count - 1);

                var appended = new List<ObjectNode>(page.Children.Count + 1);
                var budgetExhausted = false;
                foreach (var descriptor in page.Children)
                {
                    if (!_session.TryConsumeNodeBudget())
                    {
                        budgetExhausted = true;
                        break;
                    }
                    appended.Add(CreateChildNode(node, descriptor));
                }
                node.AppendChildren(appended);

                if (budgetExhausted)
                {
                    node.AppendChildCore(CreateTruncatedNode(node, $"Node budget ({_session.Options.MaximumNodesPerSession}) reached"));
                    node.Continuation = null;
                    appended.Add(node.Children[^1]);
                }
                else if (page.IsTruncated)
                {
                    node.AppendChildCore(CreateTruncatedNode(node, $"Enumerable limit ({_session.Options.MaximumEnumerableItems}) reached"));
                    node.Continuation = null;
                    appended.Add(node.Children[^1]);
                }
                else if (page.NextCursor is not null)
                {
                    node.Continuation = page.NextCursor;
                    node.AppendChildCore(CreateLoadMoreNode(node));
                    appended.Add(node.Children[^1]);
                }
                else
                {
                    node.Continuation = null;
                }

                return NodeLoadResult.Success(appended);
            }
            finally
            {
                _session.ReleaseLoadGate();
            }
        }
        catch (OperationCanceledException)
        {
            return NodeLoadResult.Cancelled();
        }
        catch (Exception ex)
        {
            var error = NodeError.FromException(ex, "LoadMoreFailed");
            node.Error = error;
            return new NodeLoadResult(NodeLoadOutcome.Failed, [], error);
        }
        finally
        {
            linked?.Dispose();
        }
    }

    private bool IsStale(long generation)
        => _session.IsDisposed || generation != _session.Generation;

    private ObjectNode CreateChildNode(ObjectNode parent, ObjectMemberDescriptor descriptor)
    {
        var child = new ObjectNode(
            _session.NextNodeId(),
            descriptor.Name,
            descriptor.Kind,
            parent.Path.Append(descriptor.PathSegment),
            parent.Depth + 1)
        {
            DeclaredType = descriptor.DeclaredType ?? descriptor.Value?.GetType(),
            RawValue = descriptor.Value,
            Accessor = descriptor.Accessor,
            Owner = descriptor.Owner,
        };

        ApplyValueSemantics(child, parent.Ancestors, descriptor.IsSensitive, descriptor.ReadError, isRoot: false);

        if (descriptor.PresetDisplay is { } preset)
        {
            child.Display = preset;
            child.ValueKind = ObjectValueKind.Primitive;
            child.CanExpand = false;
        }

        return child;
    }

    /// <summary>
    /// Single place where a node's observable semantics are decided: read error,
    /// sensitive masking, null, reference detection, then adapter description.
    /// Masking happens before any consumer can observe the value (NFR-023).
    /// </summary>
    private void ApplyValueSemantics(
        ObjectNode node,
        AncestorChain? parentChain,
        bool isSensitive,
        Exception? readError,
        bool isRoot)
    {
        node.Ancestors = parentChain;

        if (readError is not null)
        {
            node.Error = NodeError.FromException(readError);
            node.Display = new ValueDisplay(
                TruncateText($"{node.Error.ExceptionType}: {node.Error.Message}"),
                TypeNames.Get(node.DeclaredType),
                ToolTip: node.Error.Message);
            node.CanExpand = false;
            node.LoadState = NodeLoadState.Failed;
            return;
        }

        var value = node.RawValue;

        if (isSensitive)
        {
            node.IsSensitive = true;
            node.Display = ValueDisplay.Masked(TypeNames.Get(node.DeclaredType));
            node.CanExpand = false;
            _session.Diagnostics.Record("SensitiveValueMasked", nodeId: node.NodeId);
            return;
        }

        if (value is null)
        {
            node.ValueKind = ObjectValueKind.Null;
            node.Display = new ValueDisplay("null", TypeNames.Get(node.DeclaredType));
            node.CanExpand = false;
            return;
        }

        if (ObjectIdentityMap.IsTrackable(value))
        {
            if (isRoot)
            {
                _session.IdentityMap.RegisterRoot(value, node.Path);
            }
            else
            {
                var evaluation = _session.IdentityMap.Evaluate(value, parentChain, node.Path);
                if (evaluation.IsReference)
                {
                    node.ValueKind = ObjectValueKind.Reference;
                    var target = evaluation.FirstPath?.ToDisplayPath() ?? "#" + evaluation.IdentityId;
                    node.Display = new ValueDisplay("↩ reference to " + target, TypeNames.Get(node.RuntimeType));
                    node.CanExpand = false;
                    return;
                }
            }

            node.Ancestors = new AncestorChain(parentChain, value, node.Path);
        }

        var adapterContext = new ObjectAdapterContext(value, node.DeclaredType, node.Accessor?.MemberInfo);
        try
        {
            var adapter = _session.Adapters.Resolve(adapterContext, _session);
            var descriptor = adapter.Describe(adapterContext, _session);
            node.ValueKind = descriptor.ValueKind;
            node.Display = descriptor.Display;
            node.CanExpand = descriptor.CanExpand;
        }
        catch (Exception ex)
        {
            _session.Diagnostics.Record("DescribeFailed", ex.GetType().Name, node.NodeId);
            var typeText = TypeNames.Get(node.RuntimeType);
            node.Display = new ValueDisplay("{" + typeText + "}", typeText);
            node.CanExpand = false;
        }
    }

    private ObjectNode CreateTruncatedNode(ObjectNode parent, string reason)
    {
        var node = new ObjectNode(
            _session.NextNodeId(),
            "… limit reached",
            ObjectNodeKind.Truncated,
            parent.Path.Append(new SyntheticSegment("limit", _session.NextNodeId().ToString(CultureInfo.InvariantCulture))),
            parent.Depth + 1)
        {
            Display = new ValueDisplay(reason, string.Empty),
            CanExpand = false,
            LoadState = NodeLoadState.Truncated,
        };
        return node;
    }

    private ObjectNode CreateLoadMoreNode(ObjectNode parent)
    {
        return new ObjectNode(
            _session.NextNodeId(),
            "Load more…",
            ObjectNodeKind.LoadMore,
            parent.Path.Append(new SyntheticSegment("loadmore", _session.NextNodeId().ToString(CultureInfo.InvariantCulture))),
            parent.Depth + 1)
        {
            Display = new ValueDisplay("…", string.Empty),
            CanExpand = false,
        };
    }

    private ObjectNode CreateErrorNode(ObjectNode parent, NodeError error)
    {
        return new ObjectNode(
            _session.NextNodeId(),
            "⚠ error",
            ObjectNodeKind.Error,
            parent.Path.Append(new SyntheticSegment("error", _session.NextNodeId().ToString(CultureInfo.InvariantCulture))),
            parent.Depth + 1)
        {
            Display = new ValueDisplay(
                TruncateText($"{error.ExceptionType}: {error.Message}"),
                string.Empty,
                ToolTip: error.Message),
            CanExpand = false,
        };
    }

    private static string TruncateText(string text, int maximum = 200)
        => text.Length <= maximum ? text : text[..maximum] + "…";
}
