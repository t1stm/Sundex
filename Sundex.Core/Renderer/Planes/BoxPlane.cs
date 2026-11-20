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
    private const int MaxBackdrops = 8;

    private static Pipeline _pipeline = null!;
    // Backdrop copies by size, least recently used first.
    private static readonly List<GPUTexture> Backdrops = [];

    public Vector4 BorderColor;
    public Vector4 BorderWidths;
    public Vector4 GlowColor;
    public Vector4 Glow = new(0.5f, 0.5f, 1, 1);

    /// <summary>Mip level of the copied framebuffer to draw behind the fill; negative for none.</summary>
    public float BackdropLod = -1;

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
        foreach (var backdrop in Backdrops) backdrop.Dispose();
        Backdrops.Clear();
    }

    private static Pipeline CreatePipeline(Shader shader) =>
        new(new PipelineDescription(shader, [new VertexBufferLayout().PushFloat(3)]));

    public override void Render(in RenderContext context)
    {
        var commands = context.Commands;
        commands.SetPipeline(_pipeline);
        commands.SetVertexBuffer(0, UnitQuad.Positions);
        commands.SetIndexBuffer(UnitQuad.Indices);

        var uvRect = BackdropLod >= 0 ? CopyBackdrop(commands) : new Vector4(0, 0, 1, 1);
        var uniforms = new Uniform
        {
            Model = Model,
            Color = Color,
            BorderColor = BorderColor,
            Border = BorderWidths,
            ScaleRadiusLod = new Vector4(Scale.X, Scale.Y, BorderRadius, BackdropLod),
            GlowColor = GlowColor,
            Glow = Glow,
            UvRect = uvRect
        };
        commands.PushUniforms(1, uniforms);
        commands.DrawIndexed(UnitQuad.IndexCount);
    }

    /// <summary>
    ///     Copies the frame under this box and uses its mip levels as the blur. The copy is rounded up to
    ///     <see cref="BackdropStep" /> pixels with the real pixels beside the box, so a few pooled textures
    ///     serve every size. Returns the part of the texture the box covers.
    /// </summary>
    private Vector4 CopyBackdrop(ICommandList commands)
    {
        var target = commands.TargetSize;
        var left = (int)(Position.X * PixelScale.X);
        var top = (int)(Position.Y * PixelScale.Y);
        var width = Math.Max(1, (int)(Scale.X * PixelScale.X));
        var height = Math.Max(1, (int)(Scale.Y * PixelScale.Y));
        var size = new Vector2i(Math.Min(RoundUp(width), target.X), Math.Min(RoundUp(height), target.Y));
        if (size.X <= 0 || size.Y <= 0) return new Vector4(0, 0, 1, 1);

        // Start at the box's corner, moved back inside the target.
        var x = Math.Clamp(left, 0, target.X - size.X);
        var y = Math.Clamp(top, 0, target.Y - size.Y);
        var texture = RentBackdrop(size);
        commands.CopyFromTarget(new Box2i(x, y, x + size.X, y + size.Y), texture);
        commands.GenerateMipmaps(texture);
        commands.SetTexture(2, texture, Sampler.LinearMipmapClamp);

        // Texel row 0 holds the bottom row of the copy, and the shader flips V.
        return new Vector4((left - x) / (float)size.X, (y + size.Y - top - height) / (float)size.Y,
            width / (float)size.X, height / (float)size.Y);

        static int RoundUp(int pixels) => (pixels + BackdropStep - 1) / BackdropStep * BackdropStep;
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
            texture = new GPUTexture { Width = size.X, Height = size.Y };
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
        [FieldOffset(112)] public Vector4 ScaleRadiusLod;
        [FieldOffset(128)] public Vector4 GlowColor;
        [FieldOffset(144)] public Vector4 Glow;
        [FieldOffset(160)] public Vector4 UvRect;
    }
}
