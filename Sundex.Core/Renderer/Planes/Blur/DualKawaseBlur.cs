using JetBrains.Annotations;
using OpenTK.Mathematics;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Abstract;
using Sundex.Engine.Renderer.Attributes;
using Sundex.Engine.Renderer.Buffers;
using Sundex.Engine.Renderer.Shaders;
using Sundex.Engine.Renderer.Textures;

namespace Sundex.Core.Renderer.Planes.Blur;

/// <summary>
///     picom's dual Kawase blur (src/backend/gl/blur.c). Halves the source a few times, then doubles it back
///     into the target, and every pass samples a few texels around each pixel.
/// </summary>
[PreloadGraphicsContext]
internal sealed class DualKawaseBlur : IBlurMethod, IGamePreloadable
{
    public static readonly DualKawaseBlur Instance = new();

    // picom's strength levels (src/backend/backend_common.c): how many times the source is halved, and
    // how far each pass samples, in texels of its source.
    private static readonly (int Iterations, float Offset)[] StrengthLevels =
    [
        (1, 1.25f), (1, 2.25f), (2, 2.00f), (2, 3.00f), (2, 4.25f),
        (3, 2.50f), (3, 3.25f), (3, 4.25f), (3, 5.50f), (4, 3.25f),
        (4, 4.00f), (4, 5.00f), (4, 6.00f), (4, 7.25f), (4, 8.25f),
        (5, 4.50f), (5, 5.25f), (5, 6.25f), (5, 7.25f), (5, 8.50f)
    ];

    // One blur holds up to five halvings, so evicting the oldest never takes one it holds.
    private static readonly RenderTargetPool Halvings = new(16);
    private static Pipeline _pipeline = null!;

    // The source, then each halving of it, for the blur being drawn.
    private readonly GPUTexture[] _levels = new GPUTexture[6];

    private DualKawaseBlur() { }

    [UsedImplicitly]
    public static void Preload(AssetProvider assetProvider)
    {
        var shader = assetProvider.ShaderPool.GetOrLoad("Assets/Shaders/Blur/kawase",
            new ShaderLayout(ShaderLayout.Draw, new ShaderBinding("uSource", 2, ShaderBindingType.Texture)));
        _pipeline = new Pipeline(new PipelineDescription(shader, [new VertexBufferLayout().PushFloat(3)], BlendMode.Opaque));
        Halvings.Clear();
    }

    public int Margin(BackgroundBlur blur)
    {
        var (iterations, offset) = Level(blur.Strength);
        // picom's margin: the last upsample reaches twice the offset past the edge of the smallest halving.
        return (1 << iterations) * 2 * (int)MathF.Ceiling(offset) + 1;
    }

    public void Blur(ICommandList commands, GPUTexture source, GPUTexture target, BackgroundBlur blur)
    {
        var (iterations, offset) = Level(blur.Strength);
        // Halve only while the shorter side still can, so every halving has a size of its own in the pool.
        iterations = Math.Max(1, Math.Min(iterations,
            System.Numerics.BitOperations.Log2((uint)Math.Min(source.Width, source.Height))));
        _levels[0] = source;
        for (var level = 1; level <= iterations; level++)
            _levels[level] = Halvings.Rent(new Vector2i(1 + ((source.Width - 1) >> level),
                1 + ((source.Height - 1) >> level)));

        for (var level = 1; level <= iterations; level++)
            Pass(commands, _levels[level - 1], _levels[level], offset, false);
        // The last upsample draws into the target, at its own size.
        for (var level = iterations; level >= 1; level--)
            Pass(commands, _levels[level], level == 1 ? target : _levels[level - 1], offset, true);
    }

    private static (int Iterations, float Offset) Level(int strength) =>
        StrengthLevels[Math.Clamp(strength, 1, StrengthLevels.Length) - 1];

    private static void Pass(ICommandList commands, GPUTexture source, GPUTexture target, float offset, bool up)
    {
        commands.BeginPass(new PassDescription(target));
        commands.SetPipeline(_pipeline);
        commands.SetVertexBuffer(0, UnitQuad.Positions);
        commands.SetIndexBuffer(UnitQuad.Indices);
        commands.SetTexture(2, source, Sampler.LinearClamp);
        commands.PushUniforms(1, new Vector4(offset, up ? 1 : 0, 0, 0));
        commands.DrawIndexed(UnitQuad.IndexCount);
        commands.EndPass();
    }
}
