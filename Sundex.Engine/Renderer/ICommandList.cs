using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using Sundex.Engine.Renderer.Buffers;
using Sundex.Engine.Renderer.Cameras;
using Sundex.Engine.Renderer.Textures;

namespace Sundex.Engine.Renderer;

public interface ICommandList
{
    /// <summary>
    ///     Begins drawing into a target. A pass begun inside another suspends it, and its <see cref="EndPass" />
    ///     resumes the outer pass with its target, contents and scissor. Set the pipeline, buffers and textures
    ///     again after either.
    /// </summary>
    void BeginPass(in PassDescription pass);
    void EndPass();

    /// <summary>The size of the active pass target in physical pixels.</summary>
    Vector2i TargetSize { get; }

    void SetPipeline(Pipeline pipeline);
    void SetView(in Matrix4 viewProjection);

    /// <summary>Copies the uniform bytes now so subsequent writes cannot change this draw's data.</summary>
    void PushUniforms(int binding, ReadOnlySpan<byte> data);
    void SetUniformBuffer(int binding, GpuBuffer buffer);
    void SetTexture(int binding, GPUTexture texture, Sampler sampler);
    void SetVertexBuffer(int slot, GpuBuffer buffer);
    void SetIndexBuffer(GpuBuffer<int> buffer);

    /// <summary>Sets a scissor in top-left physical pixels. Null uses the whole target.</summary>
    void SetScissor(Box2i? physicalPixels);
    void DrawIndexed(int indexCount, int instanceCount = 1, int firstIndex = 0);

    /// <summary>
    ///     Copies a region of the active target into the destination's level 0 at texel 0, 0, keeping the
    ///     target's contents and the pass state. The region uses top-left physical pixels, lies inside the
    ///     target and fits in the destination. Texel row 0 receives the bottom row of the region.
    /// </summary>
    void CopyFromTarget(Box2i physicalPixels, GPUTexture destination);

    /// <summary>Copies a render target to the origin of the active target, ignoring the scissor. Parts outside the target are skipped.</summary>
    void CopyToTarget(GPUTexture source);
    void GenerateMipmaps(GPUTexture texture);

    void PushLabel(ReadOnlySpan<char> name);
    void PopLabel();
}

public readonly record struct PassDescription(GPUTexture? Target = null, Vector4? ClearColor = null);
public readonly record struct RenderContext(ICommandList Commands, Camera Camera);

public enum Sampler { LinearClamp, LinearMipmapClamp, NearestClamp }

public static class CommandListExtensions
{
    extension(ICommandList commands)
    {
        public void PushUniforms<T>(int binding, in T data)
            where T : unmanaged => commands.PushUniforms(binding, MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in data)));

        public RenderContext WithCamera(Camera camera)
        {
            commands.SetView(camera.GetVPMatrix());
            return new RenderContext(commands, camera);
        }
    }
}
