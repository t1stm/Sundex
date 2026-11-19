using JetBrains.Annotations;

namespace Sundex.Components.Attributes;

/// <summary>The UI draws a property with this attribute by reflection, in priority order.</summary>
[MeansImplicitUse(ImplicitUseKindFlags.Access)]
[AttributeUsage(AttributeTargets.Property)]
public class RenderPriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}