using System.Text;

namespace ObjectViewer.Core.Metadata;

/// <summary>Friendly, stable type names for display (e.g. List<OrderItem>, int[,]).</summary>
public static class TypeNames
{
    private static readonly Dictionary<Type, string> Aliases = new()
    {
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char",
        [typeof(decimal)] = "decimal",
        [typeof(double)] = "double",
        [typeof(float)] = "float",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(object)] = "object",
        [typeof(string)] = "string",
    };

    public static string Get(Type? type)
    {
        if (type is null)
            return string.Empty;
        if (Aliases.TryGetValue(type, out var alias))
            return alias;

        if (type.IsArray)
            return GetArrayType(type);

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            return Get(Nullable.GetUnderlyingType(type)) + "?";

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var name = definition.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0)
                name = name[..tick];
            var sb = new StringBuilder(name).Append('<');
            var args = type.GetGenericArguments();
            for (var i = 0; i < args.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(Get(args[i]));
            }
            return sb.Append('>').ToString();
        }

        return type.Name;
    }

    private static string GetArrayType(Type type)
    {
        var sb = new StringBuilder();
        var element = type;
        var rank = 1;
        while (true)
        {
            element = element.GetElementType()!;
            if (!element.IsArray)
                break;
            rank++;
        }
        _ = sb.Append(Get(element)).Append('[');
        var array = type;
        while (array.IsArray)
        {
            _ = sb.Append(',', array.GetArrayRank() - 1);
            array = array.GetElementType()!;
            if (!array.IsArray)
                break;
        }
        return sb.Append(']').ToString();
    }
}
