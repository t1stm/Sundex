using System.Diagnostics;
using Serilog;
using Sundex.Engine.Renderer.Buffers;
using Sundex.Engine.Renderer.Textures;

namespace Sundex.Engine.Renderer;

/// <summary>Serializes CPU writes, realization and retirement of a resource.</summary>
internal sealed class ResourceBacking
{
    private object? _owner;
    private IDisposable? _native;
    private FrameHazards? _hazards;
    private long _readFrame;
    private Pipeline? _readPipeline;
    private string? _readLabel;
    private bool _hazardReported;

    public T? Get<T>(object owner) where T : class, IDisposable
    {
        ThrowIfDisposed();
        if (_owner is not null && _owner != owner)
            throw new InvalidOperationException("The resource belongs to a different graphics device.");

        return (T?)_native;
    }

    public T Set<T>(object owner, T native) where T : class, IDisposable
    {
        _owner = owner;
        _native = native;
        return native;
    }

    public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_owner == this, this);

    /// <summary>Remembers the first read in the frame being recorded. Call under the resource lock.</summary>
    public void MarkRead(FrameHazards hazards, Pipeline? pipeline, string? label)
    {
        var frame = hazards.RecordingFrame;
        if (frame == 0 || frame == _readFrame) return;
        _hazards = hazards;
        _readFrame = frame;
        _readPipeline = pipeline;
        _readLabel = label;
    }

    /// <summary>Reports a CPU write to a resource the frame being recorded has already read. Call under the resource lock.</summary>
    public void CheckWrite(object resource)
    {
        if (_hazards is not { } hazards || _hazardReported || hazards.RecordingFrame != _readFrame) return;
        _hazardReported = true;
        hazards.Report(resource, _readPipeline, _readLabel);
    }

    public void Dispose()
    {
        _native?.Dispose();
        _native = null;
        _owner = this;
    }
}

/// <summary>
///     Development check for writes a deferred backend would get wrong: a backend that records the
///     frame and uploads before submitting it would show a later write to every earlier draw.
/// </summary>
internal sealed class FrameHazards(ILogger logger)
{
    private long _recordingFrame;
    private long _lastFrame;

    /// <summary>The frame being recorded, or 0 between frames.</summary>
    public long RecordingFrame => Volatile.Read(ref _recordingFrame);

    public void BeginFrame() => Volatile.Write(ref _recordingFrame, ++_lastFrame);
    public void EndFrame() => Volatile.Write(ref _recordingFrame, 0);

    public void Report(object resource, Pipeline? pipeline, string? label)
    {
        var shader = pipeline?.Shader.Sources.FirstOrDefault()?.Info.AssetInfo.Location ?? "no pipeline";
        logger.Error("{Resource} was written after a draw in the same frame read it ({FirstRead}). " +
                     "Move the write out of rendering, into Update.{NewLine}{StackTrace}",
            Describe(resource), label is null ? shader : $"{label}, {shader}", Environment.NewLine,
            new StackTrace(2, true));
    }

    private static string Describe(object resource) => resource switch
    {
        GpuBuffer buffer => $"{buffer.Usage} buffer of {buffer.Capacity} {buffer.GetType().GetGenericArguments().FirstOrDefault()?.Name}",
        GPUTexture texture => $"{texture.Width}x{texture.Height} {texture.Format} texture",
        _ => resource.GetType().Name
    };
}
