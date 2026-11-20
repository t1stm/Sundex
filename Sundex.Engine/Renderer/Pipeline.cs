using Sundex.Engine.Renderer.Shaders;

namespace Sundex.Engine.Renderer;

public enum BlendMode { Alpha, Opaque }
public enum PrimitiveTopology { Triangles }

public readonly record struct PipelineDescription(
    Shader Shader,
    VertexBufferLayout[] VertexBuffers,
    BlendMode Blend = BlendMode.Alpha,
    PrimitiveTopology Topology = PrimitiveTopology.Triangles);

public sealed class Pipeline
{
    public Pipeline(PipelineDescription description)
    {
        Shader = description.Shader;
        Blend = description.Blend;
        Topology = description.Topology;
        VertexBuffers = Array.ConvertAll(description.VertexBuffers, layout => layout.Snapshot());
        HasInstanceAttributes = Array.Exists(VertexBuffers, layout => layout.StepRate == VertexStepRate.Instance);
    }

    public Shader Shader { get; }
    public BlendMode Blend { get; }
    public PrimitiveTopology Topology { get; }
    internal VertexBufferLayout[] VertexBuffers { get; }
    internal bool HasInstanceAttributes { get; }
}
