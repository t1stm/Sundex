using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Sundex.Engine.AssetManagement.Abstract.Loading;

namespace Sundex.Engine.AssetManagement.Types.Texture;

public class TextureHolder : ILoadableAsset<TextureHolder, TextureInfo>
{
    public required TextureInfo TextureInfo { get; set; } = new();
    public required Image<Rgba32> Texture { get; set; }
    public static IAssetLoader<TextureHolder, TextureInfo> AssetLoader { get; } = new TextureLoader();
}