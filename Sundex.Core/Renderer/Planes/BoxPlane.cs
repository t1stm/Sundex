using System.Runtime.InteropServices;
using JetBrains.Annotations;
using OpenTK.Mathematics;
using Sundex.Core.Renderer.Planes.Blur;
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

    private static Pipeline _pipeline = null!;
    // The frame under a box, and its blur.
    private static readonly RenderTargetPool Copies = new(8);
    private static readonly RenderTargetPool Blurred = new(8);

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
        Copies.Clear();
        Blurred.Clear();
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
    ///     Copies the frame under this box with the margin of real pixels its blur method reads, then blurs the
    ///     copy. The copy is rounded up to <see cref="BackdropStep" /> pixels, so a few pooled textures serve
    ///     every size. Returns the blurred copy and the part of it the box covers.
    /// </summary>
    private (GPUTexture Texture, Vector4 UvRect)? BlurBackdrop(ICommandList commands)
    {
        var method = IBlurMethod.For(BackgroundBlur.Method);
        var margin = method.Margin(BackgroundBlur);
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
        var copy = Copies.Rent(size);
        var blurred = Blurred.Rent(size);
        commands.CopyFromTarget(new Box2i(x, y, x + size.X, y + size.Y), copy);
        method.Blur(commands, copy, blurred, BackgroundBlur);

        // Texel row 0 holds the bottom row of the copy, and the shader flips V.
        return (blurred, new Vector4((left - x) / (float)size.X, (y + size.Y - top - height) / (float)size.Y,
            width / (float)size.X, height / (float)size.Y));

        static int RoundUp(int pixels) => (pixels + BackdropStep - 1) / BackdropStep * BackdropStep;
    }

    [StructLayout(LayoutKind.Explicit, Size = 176)]
    public struct Uniform
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
