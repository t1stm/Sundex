namespace Sundex.Engine.Audio;

/// <summary>
///     A playhead of its own on an <see cref="AudioBuffer" />: position, pause, volume and pan,
///     over samples it shares with the buffer's other voices. Starts paused at frame 0. When it
///     reaches the end it stops there: unpausing it then plays from the start, and seeking it while
///     unpaused plays from where it was sought to. Every member is thread-safe, and does nothing
///     once the voice or its buffer is disposed.
/// </summary>
public abstract class AudioVoice(AudioBuffer buffer) : IDisposable
{
    private bool _disposed;

    public AudioBuffer Buffer { get; } = buffer;

    private bool Gone => _disposed || Buffer.Disposed;

    /// <summary>The frame playing now, in the buffer's frames. Excludes the device's latency.</summary>
    public long Position
    {
        get
        {
            lock (Buffer.Gate) return Gone ? 0 : GetPosition();
        }
    }

    public bool Paused
    {
        get => Volatile.Read(ref field);
        set
        {
            lock (Buffer.Gate)
            {
                Volatile.Write(ref field, value);
                if (!Gone) SetPaused(value);
            }
        }
    } = true;

    /// <summary>Linear gain: 1 plays the samples as they are, past 1 amplifies them.</summary>
    public float Volume
    {
        get => Volatile.Read(ref field);
        set
        {
            lock (Buffer.Gate)
            {
                Volatile.Write(ref field, value);
                if (!Gone) SetVolume(value);
            }
        }
    } = 1;

    /// <summary>-1 left, 0 centre, 1 right.</summary>
    public float Pan
    {
        get => Volatile.Read(ref field);
        set
        {
            lock (Buffer.Gate)
            {
                Volatile.Write(ref field, Math.Clamp(value, -1, 1));
                if (!Gone) SetPan(field);
            }
        }
    }

    public void Dispose()
    {
        lock (Buffer.Gate)
        {
            if (_disposed) return;
            _disposed = true;
            // the buffer frees its voices with it
            if (!Buffer.Disposed) Free();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Moves the playhead to <paramref name="frame" />, clamped to the buffer.</summary>
    public void Seek(long frame)
    {
        lock (Buffer.Gate)
        {
            if (Gone) return;
            SeekTo(Math.Clamp(frame, 0, Buffer.Frames));
            // a voice that stopped at its end plays on from here
            if (!Paused) SetPaused(false);
        }
    }

    // All called under the buffer's gate, while neither voice nor buffer is disposed.
    protected abstract long GetPosition();
    protected abstract void SeekTo(long frame);
    protected abstract void SetPaused(bool paused);
    protected abstract void SetVolume(float volume);
    protected abstract void SetPan(float pan);
    protected abstract void Free();
}
