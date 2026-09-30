global using static Festival.Tests.TestReflection;
using System.Reflection;

namespace Festival.Tests;

internal static class TestReflection
{
    /// <summary>Sets a private field, or a private property of the same name.</summary>
    public static void SetMember(Type type, string name, BindingFlags flags, object target, object? value)
    {
        if (type.GetField(name, flags) is { } field) field.SetValue(target, value);
        else type.GetProperty(name, flags)!.SetValue(target, value);
    }
}

