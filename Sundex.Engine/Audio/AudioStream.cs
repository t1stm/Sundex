namespace Sundex.Engine.Audio;

/// <summary>
///     A channel fed interleaved float frames a block at a time. <see cref="Put" /> queues them in
///     the device's own memory and its audio thread plays them from there without entering managed
///     code, so a garbage collection stalls only whoever puts, and the queue plays on through it.
///     When the queue runs dry the playhead stalls on the last frame and moves on with the next put.
///     <para>
///         The device calls <see cref="Refill" /> on its feeder thread; <see cref="Put" /> and
///         <see cref="QueuedFrames" /> belong there. <see cref="Flush" /> may run on any thread, but
///         the caller must not let it overlap a <see cref="Put" />. Everything else may be called
///         from any thread. None of it allocates, and all of it does nothing once disposed.
///     </para>
/// </summary>
public abstract class AudioStream(AudioDevice device, int sampleRate, int channels, int capacityFrames)
    : IDisposable
{
    private bool _disposed;

    public int SampleRate { get; } = sampleRate;
    public int Channels { get; } = channels;

    /// <summary>The most frames the queue holds; <see cref="Put" /> takes no more than fit.</summary>
    public int CapacityFrames { get; } = capacityFrames;

    /// <summary>Guards the stream's native state.</summary>
    protected Lock Gate { get; } = new();

    /// <summary>
    ///     Called on the device's feeder thread to top the queue up with <see cref="Put" />. Never
    ///     runs once <see cref="Dispose" /> has returned.
    /// </summary>
    public Action? Refill { get; set; }

    internal bool IsDisposed => Volatile.Read(ref _disposed);

    /// <summary>Frames put but not played yet.</summary>
    public long QueuedFrames
    {
        get
        {
            lock (Gate) return _disposed ? 0 : GetQueued();
        }
    }

    /// <summary>
    ///     Frames handed to the mixer since the last <see cref="Flush" />, plus the position it
    ///     started from: the playhead, in the stream's frames whatever the <see cref="Rate" />.
    ///     Excludes the device's latency.
    /// </summary>
    public long PlayedPosition
    {
        get
        {
            lock (Gate) return _disposed ? 0 : GetPlayed();
        }
    }

    public bool Paused
    {
        get => Volatile.Read(ref field);
        set
        {
            lock (Gate)
            {
                field = value;
                if (!_disposed) SetPaused(value);
            }
        }
    } = true;

    /// <summary>Linear gain: 1 plays the samples as they are, past 1 amplifies them.</summary>
    public float Volume
    {
        get => Volatile.Read(ref field);
        set
        {
            lock (Gate)
            {
                field = value;
                if (!_disposed) SetVolume(value);
            }
        }
    } = 1;

    /// <summary>Playback speed by resampling, so pitch follows: 2 plays twice as fast and an octave up.</summary>
    public float Rate
    {
        get => Volatile.Read(ref field);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            lock (Gate)
            {
                field = value;
                if (!_disposed) SetRate(value);
            }
        }
    } = 1;

    /// <summary>Frees the stream, after waiting out a <see cref="Refill" /> the feeder is in the middle of.</summary>
    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            _disposed = true;
            Free();
        }

        device.AwaitRefill(this);
        device.Forget(this);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Queues whole frames after everything put so far, as many as fit. Returns the frames taken.
    /// </summary>
    public int Put(ReadOnlySpan<float> interleaved)
    {
        if (interleaved.Length % Channels != 0)
            throw new ArgumentException($"{interleaved.Length} samples is not a whole number of {Channels}-channel frames.",
                nameof(interleaved));

        lock (Gate) return _disposed ? 0 : PutCore(interleaved);
    }

    /// <summary>
    ///     Drops every frame not played yet and restarts <see cref="PlayedPosition" /> at
    ///     <paramref name="basePosition" />. Leaves <see cref="Paused" /> as it was.
    /// </summary>
    public void Flush(long basePosition)
    {
        lock (Gate)
        {
            if (!_disposed) FlushCore(basePosition);
        }
    }

    /// <summary>Has the device call <see cref="Refill" /> now rather than at its next 10 ms tick.</summary>
    public void RequestRefill()
    {
        device.Wake();
    }

    // All called under the gate while not disposed.
    protected abstract long GetQueued();
    protected abstract long GetPlayed();
    protected abstract int PutCore(ReadOnlySpan<float> interleaved);
    protected abstract void FlushCore(long basePosition);
    protected abstract void SetPaused(bool paused);
    protected abstract void SetVolume(float volume);
    protected abstract void SetRate(float rate);
    protected abstract void Free();
}
