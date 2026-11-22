using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sundex.Engine.Renderer.Textures;

public enum TextureFormat { Rgba8Unorm, Rgba32Float }

[Flags]
public enum TextureUsage { Sampled = 1, RenderTarget = 2 }

public class GPUTexture : IDisposable
{
    private readonly Queue<Action<ITextureUploader>> _uploads = [];
    internal ResourceBacking Backing { get; } = new();

    public required int Width { get; init; }
    public required int Height { get; init; }
    public TextureFormat Format { get; init; } = TextureFormat.Rgba8Unorm;
    public TextureUsage Usage { get; init; } = TextureUsage.Sampled;
    public MipmapMode MipmapMode { get; init; } = MipmapMode.Enabled;
    public Sampler DefaultSampler => MipmapMode == MipmapMode.Enabled ? Sampler.LinearMipmapClamp : Sampler.LinearClamp;

    /// <summary>Queues pixels for the next use. Keep the frame alive until upload or texture disposal.</summary>
    public void QueueUploadToGPU<TPixel>(ImageFrame<TPixel> frame, Rectangle? rect = null)
        where TPixel : unmanaged, IPixel, IPixel<TPixel>
    {
        lock (Backing)
        {
            Backing.ThrowIfDisposed();
            Backing.CheckWrite(this);
            var region = rect ?? new Rectangle(0, 0, Width, Height);
            _uploads.Enqueue(uploader => uploader.Upload(frame, region));
        }
    }

    /// <summary>Runs the queued uploads. The caller holds <see cref="Backing" />, which guards the queue.</summary>
    internal bool Upload(ITextureUploader uploader)
    {
        var changed = _uploads.Count > 0;
        while (_uploads.TryPeek(out var upload))
        {
            upload(uploader);
            _uploads.Dequeue();
        }
        return changed;
    }

    public void Dispose()
    {
        lock (Backing)
        {
            Backing.Dispose();
            _uploads.Clear();
        }
        GC.SuppressFinalize(this);
    }
}

internal interface ITextureUploader
{
    void Upload<TPixel>(ImageFrame<TPixel> frame, Rectangle region)
        where TPixel : unmanaged, IPixel, IPixel<TPixel>;
}
