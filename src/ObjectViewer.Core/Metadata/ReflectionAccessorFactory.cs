using System.Linq.Expressions;
using System.Reflection;

namespace ObjectViewer.Core.Metadata;

/// <summary>
/// Builds compiled member accessors via expression trees, falling back to plain
/// reflection when compilation is not possible (Proposal §10.2/10.3).
/// </summary>
public static class ReflectionAccessorFactory
{
    public static Func<object, object?> CreateGetter(PropertyInfo property)
    {
        try
        {
            var owner = Expression.Parameter(typeof(object), "owner");
            var typedOwner = Expression.Convert(owner, property.DeclaringType!);
            var read = Expression.Property(typedOwner, property);
            var boxed = Expression.Convert(read, typeof(object));
            return Expression.Lambda<Func<object, object?>>(boxed, owner).Compile();
        }
        catch
        {
            return o => property.GetValue(o);
        }
    }

    public static Func<object, object?> CreateGetter(FieldInfo field)
    {
        try
        {
            var owner = Expression.Parameter(typeof(object), "owner");
            var typedOwner = Expression.Convert(owner, field.DeclaringType!);
            var read = Expression.Field(typedOwner, field);
            var boxed = Expression.Convert(read, typeof(object));
            return Expression.Lambda<Func<object, object?>>(boxed, owner).Compile();
        }
        catch
        {
            return o => field.GetValue(o);
        }
    }

    public static Action<object, object?>? CreateSetter(PropertyInfo property)
    {
        if (!property.CanWrite)
            return null;
        try
        {
            var owner = Expression.Parameter(typeof(object), "owner");
            var value = Expression.Parameter(typeof(object), "value");
            var typedOwner = Expression.Convert(owner, property.DeclaringType!);
            var typedValue = Expression.Convert(value, property.PropertyType);
            var assign = Expression.Assign(Expression.Property(typedOwner, property), typedValue);
            return Expression.Lambda<Action<object, object?>>(assign, owner, value).Compile();
        }
        catch
        {
            return (o, v) => property.SetValue(o, v);
        }
    }

    public static Action<object, object?>? CreateSetter(FieldInfo field)
    {
        if (field.IsInitOnly || field.IsLiteral)
            return null;
        try
        {
            var owner = Expression.Parameter(typeof(object), "owner");
            var value = Expression.Parameter(typeof(object), "value");
            var typedOwner = Expression.Convert(owner, field.DeclaringType!);
            var typedValue = Expression.Convert(value, field.FieldType);
            var assign = Expression.Assign(Expression.Field(typedOwner, field), typedValue);
            return Expression.Lambda<Action<object, object?>>(assign, owner, value).Compile();
        }
        catch
        {
            return (o, v) => field.SetValue(o, v);
        }
    }
}
