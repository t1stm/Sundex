namespace Sundex.Engine.Audio;

/// <summary>How a buffer's samples are stored. The values are miniaudio's own.</summary>
public enum SampleFormat
{
    UInt8 = 1,
    Int16 = 2,
    Float32 = 5
}

public static class SampleFormatExtensions
{
    /// <summary>Bytes per sample.</summary>
    public static int Size(this SampleFormat format)
    {
        return format switch
        {
            SampleFormat.UInt8 => 1,
            SampleFormat.Int16 => 2,
            SampleFormat.Float32 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }
}

/// <summary>
///     Samples held by the device, played by any number of <see cref="AudioVoice" />s that share
///     them rather than copy them. Disposing the buffer disposes its voices. Thread-safe.
/// </summary>
public abstract class AudioBuffer(AudioDevice device, int channels, int sampleRate, long frames)
    : IDisposable
{
    private bool _disposed;

    public int Channels { get; } = channels;
    public int SampleRate { get; } = sampleRate;
    public long Frames { get; } = frames;

    /// <summary>Guards the buffer's native state; its voices take it too.</summary>
    protected internal Lock Gate { get; } = new();

    protected internal bool Disposed
    {
        get
        {
            lock (Gate)
            {
                return _disposed;
            }
        }
    }

    /// <summary>Plays the whole buffer once on a voice of its own, freed after it ends.</summary>
    /// <param name="volume">The volume of the voice.</param>
    /// <param name="pan">-1 left, 0 centre, 1 right.</param>
    public void PlayOnce(float volume = 1, float pan = 0)
    {
        lock (Gate)
        {
            if (_disposed) return;
            PlayOnceCore(volume, Math.Clamp(pan, -1, 1));
        }
    }

    /// <summary>A playhead of its own on this buffer's samples. Starts paused at frame 0.</summary>
    public AudioVoice CreateVoice()
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return CreateVoiceCore();
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            _disposed = true;
            Free();
        }

        device.Forget(this);
        GC.SuppressFinalize(this);
    }

    protected abstract void PlayOnceCore(float volume, float pan);
    protected abstract AudioVoice CreateVoiceCore();

    /// <summary>Frees the samples and every voice on them, under <see cref="Gate" />.</summary>
    protected abstract void Free();
}