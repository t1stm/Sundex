using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Sundex.Engine.Renderer.Textures.Atlases;

namespace Sundex.Components.Tests;

public class GuillotineAtlasTests
{
    [Fact]
    public void RemovingAnImage_FreesItsPadding()
    {
        var atlas = new GuillotineAtlas(64, 64);
        using var image = new Image<Rgba32>(10, 6);

        atlas.AddImage("first", image.Frames.RootFrame);
        Assert.True(atlas.RemoveImage("first"));

        Assert.Equal(0, atlas.GetUsedArea());
        Assert.Equal(64 * 64, atlas.GetRemainingArea());
    }
}
