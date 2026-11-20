using OpenTK.Mathematics;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Buffers;
using Sundex.Engine.Renderer.Textures;

namespace Sundex.Testing;

public sealed class RecordingCommandList : ICommandList
{
    public List<(string Name, object? Data)> Calls { get; } = [];

    public void BeginPass(in PassDescription pass) => Calls.Add((nameof(BeginPass), pass));
    public void EndPass() => Calls.Add((nameof(EndPass), null));
    public Vector2i TargetSize { get; set; }

    public void SetPipeline(Pipeline pipeline) => Calls.Add((nameof(SetPipeline), pipeline));
    public void SetView(in Matrix4 viewProjection) => Calls.Add((nameof(SetView), viewProjection));
    public void PushUniforms(int binding, ReadOnlySpan<byte> data) => Calls.Add((nameof(PushUniforms), (binding, data.ToArray())));
    public void SetUniformBuffer(int binding, GpuBuffer buffer) => Calls.Add((nameof(SetUniformBuffer), (binding, buffer)));
    public void SetTexture(int binding, GPUTexture texture, Sampler sampler) => Calls.Add((nameof(SetTexture), (binding, texture, sampler)));
    public void SetVertexBuffer(int slot, GpuBuffer buffer) => Calls.Add((nameof(SetVertexBuffer), (slot, buffer)));
    public void SetIndexBuffer(GpuBuffer<int> buffer) => Calls.Add((nameof(SetIndexBuffer), buffer));
    public void SetScissor(Box2i? physicalPixels) => Calls.Add((nameof(SetScissor), physicalPixels));
    public void DrawIndexed(int indexCount, int instanceCount = 1, int firstIndex = 0) =>
        Calls.Add((nameof(DrawIndexed), (indexCount, instanceCount, firstIndex)));

    public void CopyFromTarget(Box2i physicalPixels, GPUTexture destination) =>
        Calls.Add((nameof(CopyFromTarget), (physicalPixels, destination)));
    public void CopyToTarget(GPUTexture source) => Calls.Add((nameof(CopyToTarget), source));
    public void GenerateMipmaps(GPUTexture texture) => Calls.Add((nameof(GenerateMipmaps), texture));
    public void PushLabel(ReadOnlySpan<char> name) => Calls.Add((nameof(PushLabel), name.ToString()));
    public void PopLabel() => Calls.Add((nameof(PopLabel), null));
}
