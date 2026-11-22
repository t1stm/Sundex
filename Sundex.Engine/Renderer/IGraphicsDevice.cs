using OpenTK.Mathematics;

namespace Sundex.Engine.Renderer;

public interface IGraphicsDevice : IDisposable
{
    GraphicsBackend Backend { get; }
    DeviceCapabilities Capabilities { get; }

    /// <summary>Releases retired resources and starts a frame on the render thread.</summary>
    ICommandList BeginFrame();
    void EndFrame();
    void Resize(int framebufferWidth, int framebufferHeight);

    /// <summary>
    ///     Reads tightly packed, top-down RGBA8 pixels from the target of the last pass, between EndPass
    ///     and EndFrame. The rectangle uses top-left physical pixels.
    /// </summary>
    void ReadPixels(Box2i rectangle, Span<byte> rgba8);
    GpuTimer CreateGpuTimer();
}

public enum GraphicsBackend { OpenGL }

public sealed record DeviceCapabilities
{
    public string Vendor { get; init; } = "";
    public string Renderer { get; init; } = "";
    public string Version { get; init; } = "";
    public int MaxTexture2DSize { get; init; }
    public int MaxTexture2DLayers { get; init; }
    public bool SupportsCompute { get; init; }
    public bool SupportsDebug { get; init; }

    /// <summary>OpenGL's direct state access, from GL 4.5 or GL_ARB_direct_state_access; false on other backends.</summary>
    public bool SupportsDirectStateAccess { get; init; }
}
