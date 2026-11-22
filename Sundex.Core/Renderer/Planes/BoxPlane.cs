using System.Runtime.InteropServices;
using JetBrains.Annotations;
using OpenTK.Mathematics;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Abstract;
using Sundex.Engine.Renderer.Attributes;
using Sundex.Engine.Renderer.Buffers;
using Sundex.Engine.Renderer.Shaders;
using Sundex.Engine.Renderer.Textures;

namespace Sundex.Core.Renderer.Planes;

/// <summary>
///     The plane behind every panel: what ColoredPlane is, plus a per-side border, a
///     radial glow and a frosted backdrop. Follows ColoredPlane's own recipe, since a renderable
///     with a shader of its own has to bring its own quad, uniform block and preload.
/// </summary>
[PreloadGraphicsContext]
public sealed class BoxPlane : Renderable, IGamePreloadable, IBorderRadius
{
    private const int BackdropStep = 64;
    // A blur rents up to six textures at once, so evicting the oldest never takes one it holds.
    private const int MaxBackdrops = 16;

    // picom's strength levels (src/backend/backend_common.c): how many times the copy is halved, and
    // how far each pass samples, in texels of its source.
    private static readonly (int Iterations, float Offset)[] KawaseLevels =
    [
        (1, 1.25f), (1, 2.25f), (2, 2.00f), (2, 3.00f), (2, 4.25f),
        (3, 2.50f), (3, 3.25f), (3, 4.25f), (3, 5.50f), (4, 3.25f),
        (4, 4.00f), (4, 5.00f), (4, 6.00f), (4, 7.25f), (4, 8.25f),
        (5, 4.50f), (5, 5.25f), (5, 6.25f), (5, 7.25f), (5, 8.50f)
    ];

    private static Pipeline _pipeline = null!;
    private static Pipeline _kawase = null!;
    // Backdrop copies and their halvings by size, least recently used first.
    private static readonly List<GPUTexture> Backdrops = [];
    // The copy, then each halving of it, for the blur being drawn.
    private static readonly GPUTexture[] Levels = new GPUTexture[6];

    public Vector4 BorderColor;
    public Vector4 BorderWidths;
    public Vector4 GlowColor;
    public Vector4 Glow = new(0.5f, 0.5f, 1, 1);

    /// <summary>The blur of what's behind the fill; a strength of 0 draws no backdrop.</summary>
    public BackgroundBlur BackgroundBlur;

    /// <summary>Physical pixels per UI unit, needed to find this box in the framebuffer.</summary>
    public Vector2 PixelScale = Vector2.One;

    public override Shader Shader
    {
        get => _pipeline.Shader;
        set => _pipeline = CreatePipeline(value ?? throw new ArgumentNullException(nameof(value)));
    }

    public override Vector3 Position
    {
        get => base.Position;
        set
        {
            base.Position = value;
            UpdateModel(IsChild);
        }
    }

    public override Vector3 Scale
    {
        get => base.Scale;
        set
        {
            base.Scale = value;
            UpdateModel(IsChild);
        }
    }

    public float BorderRadius { get; set; }

    [UsedImplicitly]
    public static void Preload(AssetProvider assetProvider)
    {
        var layout = new ShaderLayout(ShaderLayout.View, ShaderLayout.Draw,
            new ShaderBinding("uBackdrop", 2, ShaderBindingType.Texture));
        _pipeline = CreatePipeline(assetProvider.ShaderPool.GetOrLoad("Assets/Shaders/Box/box", layout));
        var kawase = assetProvider.ShaderPool.GetOrLoad("Assets/Shaders/Blur/kawase",
            new ShaderLayout(ShaderLayout.Draw, new ShaderBinding("uSource", 2, ShaderBindingType.Texture)));
        _kawase = new Pipeline(new PipelineDescription(kawase, [new VertexBufferLayout().PushFloat(3)], BlendMode.Opaque));
        foreach (var backdrop in Backdrops) backdrop.Dispose();
        Backdrops.Clear();
    }

    private static Pipeline CreatePipeline(Shader shader) =>
        new(new PipelineDescription(shader, [new VertexBufferLayout().PushFloat(3)]));

    public override void Render(in RenderContext context)
    {
        var commands = context.Commands;
        // The blur draws passes of its own, so it runs before this draw's state is set.
        var backdrop = BackgroundBlur.Strength > 0 ? BlurBackdrop(commands) : null;
        commands.SetPipeline(_pipeline);
        commands.SetVertexBuffer(0, UnitQuad.Positions);
        commands.SetIndexBuffer(UnitQuad.Indices);
        if (backdrop is { } blurred) commands.SetTexture(2, blurred.Texture, Sampler.LinearClamp);

        var uniforms = new Uniform
        {
            Model = Model,
            Color = Color,
            BorderColor = BorderColor,
            Border = BorderWidths,
            ScaleRadiusBackdrop = new Vector4(Scale.X, Scale.Y, BorderRadius, backdrop is null ? -1 : 1),
            GlowColor = GlowColor,
            Glow = Glow,
            UvRect = backdrop?.UvRect ?? new Vector4(0, 0, 1, 1)
        };
        commands.PushUniforms(1, uniforms);
        commands.DrawIndexed(UnitQuad.IndexCount);
    }

    /// <summary>
    ///     Copies the frame under this box with a margin of the real pixels around it, then blurs the copy in
    ///     place with picom's dual Kawase passes. The copy is rounded up to <see cref="BackdropStep" /> pixels,
    ///     so a few pooled textures serve every size. Returns the copy and the part of it the box covers.
    /// </summary>
    private (GPUTexture Texture, Vector4 UvRect)? BlurBackdrop(ICommandList commands)
    {
        var (iterations, offset) = KawaseLevels[Math.Min(BackgroundBlur.Strength, KawaseLevels.Length) - 1];
        // picom's margin: the last upsample reaches twice the offset past the edge of the smallest halving.
        var margin = (1 << iterations) * 2 * (int)MathF.Ceiling(offset) + 1;
        var target = commands.TargetSize;
        var left = (int)(Position.X * PixelScale.X);
        var top = (int)(Position.Y * PixelScale.Y);
        var width = Math.Max(1, (int)(Scale.X * PixelScale.X));
        var height = Math.Max(1, (int)(Scale.Y * PixelScale.Y));
        var size = new Vector2i(Math.Min(RoundUp(width + 2 * margin), target.X),
            Math.Min(RoundUp(height + 2 * margin), target.Y));
        if (size.X <= 0 || size.Y <= 0) return null;

        // Centre the copy on the box, moved back inside the target.
        var x = Math.Clamp(left - (size.X - width) / 2, 0, target.X - size.X);
        var y = Math.Clamp(top - (size.Y - height) / 2, 0, target.Y - size.Y);
        Levels[0] = RentBackdrop(size);
        commands.CopyFromTarget(new Box2i(x, y, x + size.X, y + size.Y), Levels[0]);

        // Stop while the shorter side still halves, so every level has a size of its own in the pool.
        iterations = Math.Min(iterations, System.Numerics.BitOperations.Log2((uint)Math.Min(size.X, size.Y)));
        for (var level = 1; level <= iterations; level++)
            Levels[level] = RentBackdrop(new Vector2i(1 + ((size.X - 1) >> level), 1 + ((size.Y - 1) >> level)));
        for (var level = 1; level <= iterations; level++)
            KawasePass(commands, Levels[level - 1], Levels[level], offset, false);
        for (var level = iterations; level >= 1; level--)
            KawasePass(commands, Levels[level], Levels[level - 1], offset, true);

        // Texel row 0 holds the bottom row of the copy, and the shader flips V.
        return (Levels[0], new Vector4((left - x) / (float)size.X, (y + size.Y - top - height) / (float)size.Y,
            width / (float)size.X, height / (float)size.Y));

        static int RoundUp(int pixels) => (pixels + BackdropStep - 1) / BackdropStep * BackdropStep;
    }

    private static void KawasePass(ICommandList commands, GPUTexture source, GPUTexture target, float offset, bool up)
    {
        commands.BeginPass(new PassDescription(target));
        commands.SetPipeline(_kawase);
        commands.SetVertexBuffer(0, UnitQuad.Positions);
        commands.SetIndexBuffer(UnitQuad.Indices);
        commands.SetTexture(2, source, Sampler.LinearClamp);
        commands.PushUniforms(1, new Vector4(offset, up ? 1 : 0, 0, 0));
        commands.DrawIndexed(UnitQuad.IndexCount);
        commands.EndPass();
    }

    private static GPUTexture RentBackdrop(Vector2i size)
    {
        GPUTexture? texture = null;
        for (var index = 0; index < Backdrops.Count; index++)
        {
            if (Backdrops[index].Width != size.X || Backdrops[index].Height != size.Y) continue;
            texture = Backdrops[index];
            Backdrops.RemoveAt(index);
            break;
        }

        if (texture is null)
        {
            texture = new GPUTexture
            {
                Width = size.X, Height = size.Y, Usage = TextureUsage.Sampled | TextureUsage.RenderTarget,
                MipmapMode = MipmapMode.Disabled
            };
            if (Backdrops.Count == MaxBackdrops)
            {
                Backdrops[0].Dispose();
                Backdrops.RemoveAt(0);
            }
        }

        Backdrops.Add(texture);
        return texture;
    }

    [StructLayout(LayoutKind.Explicit, Size = 176)]
    private struct Uniform
    {
        [FieldOffset(0)] public Matrix4 Model;
        [FieldOffset(64)] public Vector4 Color;
        [FieldOffset(80)] public Vector4 BorderColor;
        [FieldOffset(96)] public Vector4 Border;
        [FieldOffset(112)] public Vector4 ScaleRadiusBackdrop;
        [FieldOffset(128)] public Vector4 GlowColor;
        [FieldOffset(144)] public Vector4 Glow;
        [FieldOffset(160)] public Vector4 UvRect;
    }
}
