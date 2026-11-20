namespace Sundex.Engine.Renderer.Shaders;

public enum ShaderBindingType { UniformBuffer, Texture }

public readonly record struct ShaderBinding(string Name, int Binding, ShaderBindingType Type);

/// <summary>Maps shader names to the shared descriptor binding space.</summary>
public sealed class ShaderLayout
{
    public static readonly ShaderBinding View = new("View", 0, ShaderBindingType.UniformBuffer);
    public static readonly ShaderBinding Draw = new("Draw", 1, ShaderBindingType.UniformBuffer);
    public static ShaderLayout Empty { get; } = new();

    public ShaderLayout(params ShaderBinding[] bindings)
    {
        var slots = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(binding.Binding);
            ArgumentException.ThrowIfNullOrEmpty(binding.Name);
            if (!slots.Add(binding.Binding) || !names.Add(binding.Name))
                throw new ArgumentException("Shader binding numbers and names must be unique.", nameof(bindings));
            if (((binding.Binding == 0 || binding.Name == View.Name) && binding != View) ||
                ((binding.Binding == 1 || binding.Name == Draw.Name) && binding != Draw))
                throw new ArgumentException("Bindings 0 and 1 are reserved for the View and Draw uniform blocks.", nameof(bindings));
        }
        Bindings = Array.AsReadOnly((ShaderBinding[])bindings.Clone());
    }

    public IReadOnlyList<ShaderBinding> Bindings { get; }
}
