using System.Reflection;
using Sundex.Components.Abstractions;
using Sundex.Core;
using Sundex.Engine;
using Sundex.Markup;
using Sundex.Markup.Legacy;
using Sundex.Reactive;
using Sundex.Slug;
using Sundex.Style.DSL;

namespace Sundex.Components.Tests;

public class GraphicsApiTests
{
    [Fact]
    public void PublicAndProtectedSignaturesDoNotExposeNativeGraphicsTypes()
    {
        Type[] anchors = [typeof(Game), typeof(Renderable), typeof(UIContext), typeof(SundexContext),
            typeof(CSharp), typeof(Frame), typeof(SlugFont), typeof(StyleSheet), typeof(TestUIContext)];
        var leaks = new List<string>();

        foreach (var assembly in anchors.Select(anchor => anchor.Assembly).Distinct())
        foreach (var type in assembly.GetTypes().Where(Visible))
        {
            if (IsNative(type.BaseType) || type.GetInterfaces().Any(IsNative) || type.GetGenericArguments().Any(IsNative))
                leaks.Add(type.FullName!);

            foreach (var member in type.GetMembers(BindingFlags.DeclaredOnly | BindingFlags.Instance |
                         BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (member switch
                    {
                        MethodInfo method when Visible(method) => IsNative(method.ReturnType) ||
                            method.GetParameters().Any(parameter => IsNative(parameter.ParameterType)) ||
                            method.GetGenericArguments().Any(IsNative),
                        ConstructorInfo constructor when Visible(constructor) =>
                            constructor.GetParameters().Any(parameter => IsNative(parameter.ParameterType)),
                        FieldInfo field when field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly =>
                            IsNative(field.FieldType),
                        _ => false
                    })
                    leaks.Add($"{type.FullName}.{member.Name}");
            }
        }

        Assert.Empty(leaks);
    }

    private static bool Visible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static bool Visible(Type type) => type.IsVisible ||
        (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem) &&
        type.DeclaringType is { } declaringType && Visible(declaringType);

    private static bool IsNative(Type? type)
    {
        if (type is null) return false;
        if (type.IsGenericParameter) return type.GetGenericParameterConstraints().Any(IsNativeType);
        return IsNativeType(type);
    }

    private static bool IsNativeType(Type? type) => type is not null &&
        (type.Namespace?.StartsWith("OpenTK.Graphics", StringComparison.Ordinal) == true ||
         IsNativeType(type.GetElementType()) ||
         (type.IsGenericType && type.GetGenericArguments().Any(IsNativeType)));
}
