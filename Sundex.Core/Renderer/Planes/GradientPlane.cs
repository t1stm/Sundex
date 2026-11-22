using System.Runtime.InteropServices;
using JetBrains.Annotations;
using OpenTK.Mathematics;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Abstract;
using Sundex.Engine.Renderer.Attributes;
using Sundex.Engine.Renderer.Buffers;
using Sundex.Engine.Renderer.Shaders;

namespace Sundex.Core.Renderer.Planes;

[PreloadGraphicsContext]
public class GradientPlane : Renderable, IGamePreloadable, IBorderRadius
{
    private static Pipeline _pipeline = null!;

    private Uniform _uniform;

    public Type GradientType { get; set; } = Type.Linear;
    public List<Vector4> GradientColors { get; set; } = [];
    public List<float> GradientStops { get; set; } = [];

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
        var layout = new ShaderLayout(ShaderLayout.View, ShaderLayout.Draw);
        _pipeline = CreatePipeline(assetProvider.ShaderPool.GetOrLoad("Assets/Shaders/Planes/Gradient/gradient", layout));
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
        _uniform.Model = Model;
        _uniform.ScaleAndBorderPx = new Vector4(Scale.X, Scale.Y, BorderRadius, 0);
        _uniform.GradientType = GradientType;
        _uniform.GradientStopCount = Math.Min(GradientColors.Count, 8);

        for (var index = 0; index < _uniform.GradientStopCount; index++)
        {
            var color = GradientColors[index];
            var stop = index < GradientStops.Count
                ? GradientStops[index]
                : (float)index / Math.Max(1, _uniform.GradientStopCount - 1);

            switch (index)
            {
                case 0:
                    _uniform.Color0 = color;
                    _uniform.Stop0 = stop;
                    break;
                case 1:
                    _uniform.Color1 = color;
                    _uniform.Stop1 = stop;
                    break;
                case 2:
                    _uniform.Color2 = color;
                    _uniform.Stop2 = stop;
                    break;
                case 3:
                    _uniform.Color3 = color;
                    _uniform.Stop3 = stop;
                    break;
                case 4:
                    _uniform.Color4 = color;
                    _uniform.Stop4 = stop;
                    break;
                case 5:
                    _uniform.Color5 = color;
                    _uniform.Stop5 = stop;
                    break;
                case 6:
                    _uniform.Color6 = color;
                    _uniform.Stop6 = stop;
                    break;
                case 7:
                    _uniform.Color7 = color;
                    _uniform.Stop7 = stop;
                    break;
            }
        }

        commands.PushUniforms(1, _uniform);
    }

    public enum Type
    {
        Solid = 0,
        Linear = 1,
        Radial = 2,
        Conical = 3
    }

    [StructLayout(LayoutKind.Explicit, Size = 352)]
    public struct Uniform
    {
        [FieldOffset(0)] public Matrix4 Model;
        [FieldOffset(64)] public Vector4 ScaleAndBorderPx;
        [FieldOffset(80)] public Type GradientType;
        [FieldOffset(84)] public int GradientStopCount;

        [FieldOffset(96)] public Vector4 Color0;
        [FieldOffset(112)] public Vector4 Color1;
        [FieldOffset(128)] public Vector4 Color2;
        [FieldOffset(144)] public Vector4 Color3;
        [FieldOffset(160)] public Vector4 Color4;
        [FieldOffset(176)] public Vector4 Color5;
        [FieldOffset(192)] public Vector4 Color6;
        [FieldOffset(208)] public Vector4 Color7;

        [FieldOffset(224)] public float Stop0;
        [FieldOffset(240)] public float Stop1;
        [FieldOffset(256)] public float Stop2;
        [FieldOffset(272)] public float Stop3;
        [FieldOffset(288)] public float Stop4;
        [FieldOffset(304)] public float Stop5;
        [FieldOffset(320)] public float Stop6;
        [FieldOffset(336)] public float Stop7;
    }
}