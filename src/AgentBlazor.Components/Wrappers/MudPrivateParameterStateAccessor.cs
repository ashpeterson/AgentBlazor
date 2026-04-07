using System.Collections.Concurrent;
using System.Reflection;

namespace AgentBlazor.Components;

internal static class MudPrivateParameterStateAccessor
{
    private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public;
    private static readonly ConcurrentDictionary<(Type ComponentType, string FieldName), FieldInfo> FieldCache = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo> ValuePropertyCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> SetValueMethodCache = new();

    public static TValue? GetValue<TValue>(object component, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        var state = GetState(component, fieldName);
        var value = GetValueProperty(state.GetType()).GetValue(state);
        return value is null
            ? default
            : (TValue?)value;
    }

    public static Task SetValueAsync<TValue>(object component, string fieldName, TValue value)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        var state = GetState(component, fieldName);
        var result = GetSetValueMethod(state.GetType()).Invoke(state, [value]);
        return result as Task ?? Task.CompletedTask;
    }

    public static void SetValue<TValue>(object component, string fieldName, TValue value)
    {
        SetValueAsync(component, fieldName, value).GetAwaiter().GetResult();
    }

    private static object GetState(object component, string fieldName)
    {
        var field = FieldCache.GetOrAdd((component.GetType(), fieldName), static key =>
        {
            var (componentType, requestedFieldName) = key;
            for (var currentType = componentType; currentType is not null; currentType = currentType.BaseType)
            {
                var fieldInfo = currentType.GetField(requestedFieldName, FieldFlags | BindingFlags.DeclaredOnly);
                if (fieldInfo is not null)
                {
                    return fieldInfo;
                }
            }

            throw new InvalidOperationException(
                $"MudBlazor parameter state field '{requestedFieldName}' was not found on '{componentType.FullName}'.");
        });

        return field.GetValue(component)
               ?? throw new InvalidOperationException(
                   $"MudBlazor parameter state field '{fieldName}' on '{component.GetType().FullName}' was not initialized.");
    }

    private static PropertyInfo GetValueProperty(Type stateType)
    {
        return ValuePropertyCache.GetOrAdd(stateType, static type =>
            type.GetProperty("Value", MemberFlags)
            ?? throw new InvalidOperationException($"MudBlazor parameter state type '{type.FullName}' does not expose a Value property."));
    }

    private static MethodInfo GetSetValueMethod(Type stateType)
    {
        return SetValueMethodCache.GetOrAdd(stateType, static type =>
            type.GetMethod("SetValueAsync", MemberFlags)
            ?? throw new InvalidOperationException($"MudBlazor parameter state type '{type.FullName}' does not expose SetValueAsync."));
    }
}
