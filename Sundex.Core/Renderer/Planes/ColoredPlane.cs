using JetBrains.Annotations;
using OpenTK.Mathematics;
using Sundex.Core.Renderer.Planes.Uniforms;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Abstract;
using Sundex.Engine.Renderer.Attributes;
using Sundex.Engine.Renderer.Buffers;
using Sundex.Engine.Renderer.Shaders;

namespace Sundex.Core.Renderer.Planes;

[PreloadGraphicsContext]
public class ColoredPlane : Renderable, IGamePreloadable, IBorderRadius
{
    private static Pipeline _pipeline = null!;

    private ColoredUniform _uniform;

    public ColoredPlane()
    {
        _uniform = new ColoredUniform();
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

    /// <summary>
    ///     The plane's fill. A zero alpha paints nothing, so setting one clears
    ///     <see cref="Renderable.IsVisible" /> and the draw is skipped entirely. The cull sits
    ///     here rather than on <see cref="Renderable" /> because batched renderables hold
    ///     their colors per instance and leave this property at its default.
    /// </summary>
    public override Vector4 Color
    {
        get => base.Color;
        set
        {
            base.Color = value;
            IsVisible = value.W > 0;
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
        var layout = new ShaderLayout(ShaderLayout.View, ShaderLayout.Draw);
        _pipeline = CreatePipeline(assetProvider.ShaderPool.GetOrLoad("Assets/Shaders/Planes/Colored/colored", layout));
    }

    private static Pipeline CreatePipeline(Shader shader) =>
        new(new PipelineDescription(shader, [new VertexBufferLayout().PushFloat(3)]));

    public override void Render(in RenderContext context)
    {
        var commands = context.Commands;
        commands.SetPipeline(_pipeline);
        commands.SetVertexBuffer(0, UnitQuad.Positions);
        commands.SetIndexBuffer(UnitQuad.Indices);
        PushUniforms(commands);
        commands.DrawIndexed(UnitQuad.IndexCount);
        base.Render(context);
    }

    private void PushUniforms(ICommandList commands)
    {
        _uniform.Color = Color;
        _uniform.ScaleAndBorderPx = new Vector4(Scale.X, Scale.Y, BorderRadius, 0);

        _uniform.Model = Model;

        commands.PushUniforms(1, _uniform);
    }
}