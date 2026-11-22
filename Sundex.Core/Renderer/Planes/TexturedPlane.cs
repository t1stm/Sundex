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

[PreloadGraphicsContext]
public sealed class TexturedPlane : Renderable, IGamePreloadable, IBorderRadius
{
    private static Pipeline _pipeline = null!;

    private Uniform _uniform;

    public TexturedPlane()
    {
        // The shader multiplies the sampled texel by this, so the inherited default of
        // zero would draw nothing. White is "the texture as it is"; a caller lowers the
        // alpha to fade it (ElementAlpha does exactly that) or the RGB to tint it.
        Color = Vector4.One;
    }

    public GPUTexture? Texture { get; set; }

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

    public override Shader Shader
    {
        get => _pipeline.Shader;
        set => _pipeline = CreatePipeline(value ?? throw new ArgumentNullException(nameof(value)));
    }

    public float BorderRadius { get; set; }

    [UsedImplicitly]
    public static void Preload(AssetProvider assetProvider)
    {
        var layout = new ShaderLayout(ShaderLayout.View, ShaderLayout.Draw,
            new ShaderBinding("uTexture", 2, ShaderBindingType.Texture));
        _pipeline = CreatePipeline(assetProvider.ShaderPool.GetOrLoad("Assets/Shaders/Planes/Textured/textured", layout));
    }

    private static Pipeline CreatePipeline(Shader shader) =>
        new(new PipelineDescription(shader, [new VertexBufferLayout().PushFloat(3).PushFloat(2)]));

    public override void Render(in RenderContext context)
    {
        var commands = context.Commands;
        commands.SetPipeline(_pipeline);
        commands.SetVertexBuffer(0, UnitQuad.PositionsWithUV);
        commands.SetIndexBuffer(UnitQuad.Indices);
        if (Texture is { } texture) commands.SetTexture(2, texture, texture.DefaultSampler);
        PushUniforms(commands);
        commands.DrawIndexed(UnitQuad.IndexCount);
        base.Render(context);
    }

    /// <summary>The part of the texture drawn (offset x, offset y, width, height, in 0–1); all of it by default.</summary>
    public Vector4 UvRect { get; set; } = new(0, 0, 1, 1);

    private void PushUniforms(ICommandList commands)
    {
        _uniform.ScaleAndBorderPx = new Vector4(Scale.X, Scale.Y, BorderRadius, 0);
        _uniform.Color = Color;
        _uniform.UvRect = UvRect;

        _uniform.Model = Model;

        commands.PushUniforms(1, _uniform);
    }

    [StructLayout(LayoutKind.Explicit, Size = 112)]
    public struct Uniform
    {
        [FieldOffset(0)] public Matrix4 Model;
        [FieldOffset(64)] public Vector4 ScaleAndBorderPx;

        /// <summary>Multiplied over the sampled texel: white draws the texture as-is.</summary>
        [FieldOffset(80)] public Vector4 Color;

        /// <summary>The part of the texture drawn: offset x, offset y, width, height, in 0–1.</summary>
        [FieldOffset(96)] public Vector4 UvRect;
    }
}