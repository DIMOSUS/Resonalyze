using System.Reflection;

namespace Resonalyze.Screenshots;

/// <summary>
/// Reaches the shell's private parts. The tool drives the real application rather
/// than a mock, so it has to name fields the app never meant to expose.
/// </summary>
/// <remarks>
/// Every accessor throws with the name it could not find. That is the whole design:
/// a renamed field must stop the tool with "no field buttonExport on
/// VirtualCrossoverPanel", not quietly skip a shot and leave a stale image in
/// <c>assets/images</c>. Compile-time breakage is preferable and the project is in
/// the solution for that reason, but names reached by string cannot have it.
/// </remarks>
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
