using Sundex.Engine.AssetManagement;
using Sundex.Engine.AssetManagement.Types.Shader;

namespace Sundex.Engine.Renderer.Shaders;

public enum ShaderStage { Vertex, Fragment, Compute }

public sealed class Shader(AssetProvider assetProvider, ShaderLayout layout, params ShaderSource[] sources)
    : IDisposable
{
    internal ResourceBacking Backing { get; } = new();

    public static Shader Dummy { get; } = new(null!, ShaderLayout.Empty);

    public ShaderLayout Layout { get; } = layout;
    internal ShaderSource[] Sources { get; private set; } = sources;
    public int Version { get; private set; }

    public void ReloadShader()
    {
        lock (Backing)
        {
            Backing.ThrowIfDisposed();
            var replacement = new ShaderSource[Sources.Length];
            assetProvider.Load(replacement, Array.ConvertAll(Sources, source => source.Info));
            Sources = replacement;
            Version++;
        }
    }

    public void Dispose()
    {
        lock (Backing) Backing.Dispose();
    }
}
