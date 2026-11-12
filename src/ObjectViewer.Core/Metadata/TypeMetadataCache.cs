using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace ObjectViewer.Core.Metadata;

/// <summary>
/// Process-wide type metadata cache (NFR-003). Stores only type structure.
/// Note: for collectible AssemblyLoadContext scenarios this must move to a
/// ConditionalWeakTable or a per-context cache (Proposal §10.3, future work).
/// </summary>
public sealed class TypeMetadataCache
{
    public static TypeMetadataCache Shared { get; } = new();

    private readonly ConcurrentDictionary<Type, TypeMetadata> _cache = new();

    public TypeMetadata Get(Type type)
        => _cache.GetOrAdd(type, static t => new TypeMetadata(t, BuildMembers(t)));

    private static IReadOnlyList<MemberAccessor> BuildMembers(Type type)
    {
        var members = new List<MemberAccessor>();

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        foreach (var property in type.GetProperties(All))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;

            var browsable = property.GetCustomAttribute<BrowsableAttribute>()?.Browsable ?? true;
            members.Add(new MemberAccessor
            {
                Name = property.Name,
                DisplayName = GetDisplayName(property, property.Name),
                ValueType = property.PropertyType,
                MemberInfo = property,
                Kind = MemberKind.Property,
                IsPublic = property.GetMethod is { IsPublic: true } || (property.SetMethod is { IsPublic: true }),
                IsStatic = property.GetMethod?.IsStatic ?? property.SetMethod?.IsStatic ?? false,
                Order = GetOrder(property),
                IsBrowsable = browsable,
                Getter = ReflectionAccessorFactory.CreateGetter(property),
                Setter = ReflectionAccessorFactory.CreateSetter(property),
            });
        }

        foreach (var field in type.GetFields(All))
        {
            // Skip compiler-generated backing fields.
            if (field.Name.StartsWith('<'))
                continue;

            var browsable = field.GetCustomAttribute<BrowsableAttribute>()?.Browsable ?? true;
            members.Add(new MemberAccessor
            {
                Name = field.Name,
                DisplayName = GetDisplayName(field, field.Name),
                ValueType = field.FieldType,
                MemberInfo = field,
                Kind = MemberKind.Field,
                IsPublic = field.IsPublic,
                IsStatic = field.IsStatic,
                Order = GetOrder(field),
                IsBrowsable = browsable,
                Getter = ReflectionAccessorFactory.CreateGetter(field),
                Setter = ReflectionAccessorFactory.CreateSetter(field),
            });
        }

        return members
            .OrderBy(m => m.Order)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetDisplayName(MemberInfo member, string fallback)
    {
        var display = member.GetCustomAttribute<DisplayNameAttribute>();
        if (display is not null && !string.IsNullOrEmpty(display.DisplayName))
            return display.DisplayName;

        var dataDisplay = member.GetCustomAttribute<DisplayAttribute>();
        if (dataDisplay is not null)
        {
            var name = dataDisplay.GetName();
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        return fallback;
    }

    private static int GetOrder(MemberInfo member)
    {
        var display = member.GetCustomAttribute<DisplayAttribute>();
        if (display is not null && display.GetOrder() is { } order && order >= 0)
            return order;
        return member.MetadataToken;
    }
}
