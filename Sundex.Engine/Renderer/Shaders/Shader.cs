using Sundex.Engine.AssetManagement;
using Sundex.Engine.AssetManagement.Types.Shader;

namespace Sundex.Engine.Renderer.Shaders;

public enum ShaderStage { Vertex, Fragment, Compute }

public sealed class Shader : IDisposable
{
    private readonly AssetProvider _assetProvider;
    internal ResourceBacking Backing { get; } = new();

    public Shader(AssetProvider assetProvider, ShaderLayout layout, params ShaderSource[] sources)
    {
        _assetProvider = assetProvider;
        Layout = layout;
        Sources = sources;
    }

    public static Shader Dummy { get; } = new(null!, ShaderLayout.Empty);

    public ShaderLayout Layout { get; }
    internal ShaderSource[] Sources { get; private set; }
    public int Version { get; private set; }

    public void ReloadShader()
    {
        lock (Backing)
        {
            Backing.ThrowIfDisposed();
            var replacement = new ShaderSource[Sources.Length];
            _assetProvider.Load(replacement, Array.ConvertAll(Sources, source => source.Info));
            Sources = replacement;
            Version++;
        }
    }

    public void Dispose()
    {
        lock (Backing) Backing.Dispose();
    }
}
