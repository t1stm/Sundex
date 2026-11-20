using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.Renderer.Abstract;
using Sundex.Engine.Renderer.Attributes;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Shaders;
using Sundex.Engine.Renderer.Textures;
using Sundex.Engine.Text.Fonts;
using Sundex.Slug;

namespace Sundex.Engine.Text;

[PreloadGraphicsContext]
public class TextProvider(IAssetProvider provider, IFontProvider fontProvider, string fontName)
    : IGamePreloadable, ITextProvider
{
    private static Pipeline _pipeline = null!;

    private readonly GlyphProvider _glyphProvider = new(fontProvider, fontName);

    // ponytail: one texture set per provider, so two providers over one font upload it
    // twice (~7 MiB each). Cache them per SlugFontData if that ever matters.
    private GPUTexture? _curves;
    private GPUTexture? _bands;
    private GPUTexture? _glyphs;

    public IAssetProvider AssetProvider { get; } = provider;

    public static void Preload(AssetProvider assetProvider)
    {
        var layout = new ShaderLayout(ShaderLayout.View,
            new ShaderBinding("uCurves", 2, ShaderBindingType.Texture),
            new ShaderBinding("uBands", 3, ShaderBindingType.Texture),
            new ShaderBinding("uGlyphs", 4, ShaderBindingType.Texture));
        var shader = assetProvider.ShaderPool.GetOrLoad("Assets/Shaders/Text/Batched", layout);
        var glyphs = new VertexBufferLayout(VertexStepRate.Instance);
        TextCharacter.DescribeLayout(glyphs);
        _pipeline = new Pipeline(new PipelineDescription(shader, [new VertexBufferLayout().PushFloat(3), glyphs]));
    }

    public IGlyphProvider GlyphProvider => _glyphProvider;

    public void Bind(ICommandList commands)
    {
        if (_curves is null || _bands is null || _glyphs is null)
        {
            var data = _glyphProvider.SlugData;
            _curves = CreateTexture(data.Curves, data.CurveRows);
            _bands = CreateTexture(data.Bands, data.BandRows);
            _glyphs = CreateTexture(data.Glyphs, data.GlyphRows);
        }

        commands.SetPipeline(_pipeline);
        commands.SetTexture(2, _curves, Sampler.NearestClamp);
        commands.SetTexture(3, _bands, Sampler.NearestClamp);
        commands.SetTexture(4, _glyphs, Sampler.NearestClamp);
        RenderMarker.Debug("Bound Slug Textures: ", fontName, MarkerType.Hidden);
    }

    private static GPUTexture CreateTexture(float[] texels, int rows)
    {
        var texture = new GPUTexture
        {
            Width = SlugFontData.TextureWidth,
            Height = rows,
            Format = TextureFormat.Rgba32Float,
            MipmapMode = MipmapMode.Disabled
        };

        // The upload needs the pixels in one block, and a band texture (~5 MiB) is past the size
        // ImageSharp otherwise splits into several, unless the app set this globally.
        var configuration = Configuration.Default.Clone();
        configuration.PreferContiguousImageBuffers = true;

        var image = Image.LoadPixelData(configuration, MemoryMarshal.Cast<float, RgbaVector>(texels),
            SlugFontData.TextureWidth, rows);
        texture.QueueUploadToGPU(image.Frames.RootFrame);
        return texture;
    }
}
