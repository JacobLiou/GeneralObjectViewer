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

    /// <summary>
    /// Refreshes already-loaded nodes by re-reading their backing members instead of
    /// rebuilding the tree (PRD FR-101, Proposal §18.1). Type changes invalidate the
    /// loaded children so the next expansion reloads them.
    /// </summary>
    public async Task<NodeRefreshResult> RefreshAsync(ObjectNode node, RefreshScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        var targets = scope switch
        {
            RefreshScope.Node => [node],
            RefreshScope.Subtree => CollectLoaded(node, includeSelf: true),
            RefreshScope.LoadedTree => CollectLoaded(_session.Root ?? node, includeSelf: true),
            _ => [node],
        };

        var changed = 0;
        var invalidated = 0;
        await Task.Yield();
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            target.Version++;
            switch (RefreshNode(target))
            {
                case RefreshOutcome.Changed:
                    changed++;
                    break;
                case RefreshOutcome.Invalidated:
                    invalidated++;
                    changed++;
                    break;
            }
        }

        _session.Diagnostics.Record("RefreshCompleted", $"targets={targets.Count}, changed={changed}, invalidated={invalidated}");
        return new NodeRefreshResult(targets.Count, changed, invalidated);
    }

    /// <summary>
    /// Controlled edit of one member node (PRD FR-122/123): validate → convert →
    /// write (isolated) → re-read → re-describe. Setter exceptions are surfaced on
    /// the node only and never propagate (FR-124). Loaded children are invalidated
    /// so the next expansion reloads the replaced value's members. Diagnostics
    /// record member names, never raw values (FR-126).
    /// </summary>
    public async Task<NodeEditResult> WriteAsync(ObjectNode node, string? text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (_session.IsDisposed)
            return NodeEditResult.Stale();

        if (!node.CanEdit
            || node.IsSensitive
            || node.Kind is not (ObjectNodeKind.Property or ObjectNodeKind.Field)
            || node.Accessor is not { CanWrite: true, Setter: { } setter }
            || node.Owner is null)
        {
            return NodeEditResult.NotEditable("This node cannot be edited.");
        }

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryConvertForEdit(node, text, out var value, out var conversionError))
        {
            _session.Diagnostics.Record("EditRejected", $"{node.Name}: conversion failed", node.NodeId);
            return NodeEditResult.ConversionFailed(conversionError ?? "Invalid value.");
        }

        try
        {
            setter(node.Owner, value);
        }
        catch (Exception ex)
        {
            var actual = NodeError.Unwrap(ex);
            _session.Diagnostics.Record("EditSetterFailed", $"{node.Name}: {actual.GetType().Name}", node.NodeId);
            // Re-read so the display reflects the object's real state: the setter
            // may have partially applied before throwing (FR-125). The error is
            // attached after the re-read so a read failure cannot clear it.
            ReReadAfterWrite(node);
            node.Error = NodeError.FromException(actual, "EditFailed");
            return NodeEditResult.SetterFailed(actual.Message);
        }

        node.Error = null;
        node.HasChanged = true;
        node.Version++;
        _session.Diagnostics.Record("EditApplied", node.Name, node.NodeId);
        ReReadAfterWrite(node);

        // Children were projected from the previous value; a replaced value
        // invalidates them so the next expansion reloads (mirrors refresh).
        if (node.Children.Count > 0)
            InvalidateChildren(node);

        return NodeEditResult.Applied();
    }

    /// <summary>Custom session converters first, then the built-in parser (FR-123/131).</summary>
    private bool TryConvertForEdit(ObjectNode node, string? text, out object? value, out string? error)
    {
        var targetType = node.Accessor!.ValueType;
        var culture = _session.Options.Culture;

        foreach (var converter in _session.EditConverters)
        {
            try
            {
                if (converter.TryConvert(text ?? string.Empty, targetType, culture, out value, out error))
                    return true;
            }
            catch
            {
                // A failing custom converter must never break the edit pipeline.
            }
        }

        return Editing.EditValueParser.TryParse(targetType, text, culture, out value, out error);
    }

    /// <summary>Re-reads the member and re-describes the node; getter failures become the node error.</summary>
    private void ReReadAfterWrite(ObjectNode node)
    {
        var (value, error) = MemberReader.Read(node.Accessor!, node.Owner!, _session, node.NodeId);
        if (error is not null)
        {
            node.Error = NodeError.FromException(error);
            node.Display = new ValueDisplay(
                TruncateText($"{node.Error.ExceptionType}: {node.Error.Message}"),
                TypeNames.Get(node.DeclaredType),
                ToolTip: node.Error.Message);
            return;
        }

        node.Error = null;
        node.RawValue = value;
        ApplyDescription(node);
    }

    private enum RefreshOutcome
    {
        Unchanged,
        Changed,
        Invalidated,
    }

    private static List<ObjectNode> CollectLoaded(ObjectNode start, bool includeSelf)
    {
        var result = new List<ObjectNode>();
        if (includeSelf && start.Kind != ObjectNodeKind.Root)
            result.Add(start);

        CollectLoadedCore(start, result);
        return result;

        static void CollectLoadedCore(ObjectNode node, List<ObjectNode> result)
        {
            foreach (var child in node.Children)
            {
                if (child.Kind is ObjectNodeKind.LoadMore or ObjectNodeKind.Truncated or ObjectNodeKind.Loading)
                    continue;
                result.Add(child);
                CollectLoadedCore(child, result);
            }
        }
    }

    private RefreshOutcome RefreshNode(ObjectNode node)
    {
        var oldText = node.Display.Text;
        var oldTypeText = node.Display.TypeText;
        var oldRuntimeType = node.RuntimeType;
        var hadChildren = node.Children.Count > 0;

        if (node.Accessor is not null && node.Owner is not null)
        {
            var (value, error) = MemberReader.Read(node.Accessor, node.Owner, _session, node.NodeId);
            if (error is not null)
            {
                var nodeError = NodeError.FromException(error);
                var isNew = node.Error?.Message != nodeError.Message;
                node.Error = nodeError;
                node.Display = new ValueDisplay(
                    TruncateText($"{nodeError.ExceptionType}: {nodeError.Message}"),
                    TypeNames.Get(node.DeclaredType),
                    ToolTip: nodeError.Message);
                node.HasChanged = isNew;
                return isNew ? RefreshOutcome.Changed : RefreshOutcome.Unchanged;
            }

            node.Error = null;
            node.RawValue = value;
            ApplyDescription(node);
            var changed = node.Display.Text != oldText
                || node.Display.TypeText != oldTypeText
                || node.RuntimeType != oldRuntimeType;
            node.HasChanged = changed;

            if (node.RuntimeType != oldRuntimeType && hadChildren)
            {
                InvalidateChildren(node);
                return RefreshOutcome.Invalidated;
            }

            return changed ? RefreshOutcome.Changed : RefreshOutcome.Unchanged;
        }

        // Nodes without an in-place read path (root, collections, imported values):
        // invalidate loaded children so the caller can reload on demand.
        if (hadChildren || node.LoadState == NodeLoadState.Loaded)
        {
            InvalidateChildren(node);
            return RefreshOutcome.Invalidated;
        }

        return RefreshOutcome.Unchanged;
    }

    /// <summary>Re-describes a node's value after a refresh read; re-renders references without re-registering identity.</summary>
    private void ApplyDescription(ObjectNode node)
    {
        var value = node.RawValue;

        if (node.IsSensitive)
        {
            node.Display = ValueDisplay.Masked(TypeNames.Get(node.DeclaredType));
            node.CanExpand = false;
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
            node.Ancestors = new AncestorChain(node.Parent?.Ancestors, value, node.Path);

            // Restore reference semantics: cycles via the ancestor chain, shared
            // occurrences via the session first-seen table (never re-registering).
            var target = node.Parent?.Ancestors?.Find(value)
                ?? _session.IdentityMap.FindShared(value, node.Path);
            if (target is not null)
            {
                node.ValueKind = ObjectValueKind.Reference;
                node.Display = new ValueDisplay("↩ reference to " + target.ToDisplayPath(), TypeNames.Get(node.RuntimeType));
                node.CanExpand = false;
                return;
            }
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

    private static void InvalidateChildren(ObjectNode node)
    {
        node.Continuation = null;
        node.ReplaceChildren([]);
        node.LoadState = NodeLoadState.NotLoaded;
    }

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

        // Defense in depth: the adapter's CanEdit claim is only honored when the
        // node really carries everything a write needs (FR-121).
        child.CanEdit = descriptor.CanEdit
            && descriptor.Kind is ObjectNodeKind.Property or ObjectNodeKind.Field
            && descriptor.Accessor is { CanWrite: true }
            && descriptor.Owner is not null
            && !descriptor.IsSensitive
            && descriptor.ReadError is null;

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
