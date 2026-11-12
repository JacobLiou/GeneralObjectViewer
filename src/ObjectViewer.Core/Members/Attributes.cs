namespace ObjectViewer.Core.Members;

/// <summary>Marks a property or field as sensitive; the value is always masked (NFR-022).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ObjectViewerSensitiveAttribute : Attribute;

/// <summary>Marks a property or field as editable under an enabling edit policy (V1.0).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ObjectViewerEditableAttribute : Attribute;

/// <summary>Excludes a property or field from the viewer entirely.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ObjectViewerIgnoreAttribute : Attribute;
