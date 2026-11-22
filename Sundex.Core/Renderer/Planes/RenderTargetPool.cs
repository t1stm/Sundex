using OpenTK.Mathematics;
using Sundex.Engine.Renderer.Textures;

namespace Sundex.Core.Renderer.Planes;

/// <summary>
///     Render targets by size, least recently used first, so a few sizes serve every frame without
///     allocating. Renting a size again returns the same texture. Past the capacity, the oldest is disposed.
/// </summary>
internal sealed class RenderTargetPool(int capacity)
{
    private readonly List<GPUTexture> _textures = [];

    public GPUTexture Rent(Vector2i size)
    {
        GPUTexture? texture = null;
        for (var index = 0; index < _textures.Count; index++)
        {
            if (_textures[index].Width != size.X || _textures[index].Height != size.Y) continue;
            texture = _textures[index];
            _textures.RemoveAt(index);
            break;
        }

        if (texture is null)
        {
            texture = new GPUTexture
            {
                Width = size.X, Height = size.Y, Usage = TextureUsage.Sampled | TextureUsage.RenderTarget,
                MipmapMode = MipmapMode.Disabled
            };
            if (_textures.Count == capacity)
            {
                _textures[0].Dispose();
                _textures.RemoveAt(0);
            }
        }

        _textures.Add(texture);
        return texture;
    }

    /// <summary>Disposes every texture, for a new graphics context.</summary>
    public void Clear()
    {
        foreach (var texture in _textures) texture.Dispose();
        _textures.Clear();
    }
}
