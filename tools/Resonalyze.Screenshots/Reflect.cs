using System.Reflection;

namespace Resonalyze.Screenshots;

/// <summary>Reflection into the real app. Every accessor throws naming what it could not find, so a rename stops the tool
/// instead of silently leaving a stale image.</summary>
internal static class Reflect
{
    private const BindingFlags Any =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static object Field(object target, string name)
    {
        ArgumentNullException.ThrowIfNull(target);
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField(name, Any);
            if (field != null)
            {
                return field.GetValue(target)
                    ?? throw new InvalidOperationException(
                        $"Field {name} on {target.GetType().Name} is null.");
            }
        }

        throw new InvalidOperationException(
            $"No field {name} on {target.GetType().Name}.");
    }

    public static T Field<T>(object target, string name) => (T)Field(target, name);

    public static object Property(object target, string name)
    {
        ArgumentNullException.ThrowIfNull(target);
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            PropertyInfo? property = type.GetProperty(name, Any);
            if (property != null)
            {
                return property.GetValue(target)
                    ?? throw new InvalidOperationException(
                        $"Property {name} on {target.GetType().Name} is null.");
            }
        }

        throw new InvalidOperationException(
            $"No property {name} on {target.GetType().Name}.");
    }

    public static object? Invoke(object target, string name, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(target);
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            MethodInfo? method = type.GetMethod(name, Any);
            if (method != null)
            {
                return method.Invoke(target, arguments);
            }
        }

        throw new InvalidOperationException(
            $"No method {name} on {target.GetType().Name}.");
    }
}
