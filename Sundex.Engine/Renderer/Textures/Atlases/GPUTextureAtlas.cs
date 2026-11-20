using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.AssetManagement.Types.Cache;
using Sundex.Engine.Common;

namespace Sundex.Engine.Renderer.Textures.Atlases;

public class GPUTextureAtlas(
    int width,
    int height,
    TextureFormat format = TextureFormat.Rgba8Unorm,
    MipmapMode mipmapMode = MipmapMode.Enabled)
{
    public required string AtlasID { get; init; } // used for identification of the atlas when caching
    public int Width { get; } = width;
    public int Height { get; } = height;

    public GPUTexture Texture { get; set; } = new()
    {
        Width = width,
        Height = height,
        Format = format,
        MipmapMode = mipmapMode
    };

    public GuillotineAtlas Atlas { get; set; } = new(width, height);

    public void AddTexture<TPixel>(string name, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel, IPixel<TPixel>
    {
        var rectangle = Atlas.AddImage(name, image);
        Texture.QueueUploadToGPU(image, rectangle);
    }

    public void LoadFromCache(AssetProvider assetProvider)
    {
        var textureExists = assetProvider.CacheProvider.TryLoadingCachedAsset(new CachedInfo
        {
            CacheID = "Atlas_Texture" + AtlasID
        }, out var atlasTexture);

        var atlasInfoExists = assetProvider.CacheProvider.TryLoadingCachedAsset(new CachedInfo
        {
            CacheID = "Atlas_Lookup" + AtlasID
        }, out var atlasInfo);

        if (!(textureExists && atlasInfoExists))
            return;

        if (format != TextureFormat.Rgba8Unorm && format != TextureFormat.Rgba32Float)
            throw new Exception("Invalid atlas texture format for saving and loading.");

        switch (format)
        {
            case TextureFormat.Rgba8Unorm:
            {
                var texture = Image.Load<Rgba32>(atlasTexture!.AssetStream.Stream);
                Texture.QueueUploadToGPU(texture.Frames.RootFrame);
                break;
            }

            case TextureFormat.Rgba32Float:
            {
                var texture = Image.Load<RgbaVector>(atlasTexture!.AssetStream.Stream);
                Texture.QueueUploadToGPU(texture.Frames.RootFrame);
                break;
            }
        }

        var overrideAtlas =
            JsonSerializer.Deserialize<GuillotineAtlas>(atlasInfo!.AssetStream.Stream, SerializerOptions.Json);
        if (overrideAtlas == null) return;

        Atlas = overrideAtlas;
    }
}