using Serilog;
using Sundex.Engine.Audio.Miniaudio;
using Sundex.Engine.Audio.Null;

namespace Sundex.Engine.Audio;

/// <summary>
///     An output device and everything playing on it: buffers with their voices, and push streams.
///     <see cref="Open" /> picks miniaudio and falls back to a silent device that still keeps time.
///     Disposing it disposes every buffer and stream made from it.
///     <para>
///         Streams are fed through <see cref="AudioStream.Refill" />, which this device calls from
///         a feeder thread of its own every 10 ms, or sooner after <see cref="AudioStream.RequestRefill" />.
///         The browser has no threads to spare: there the host calls <see cref="Pump" /> instead.
///     </para>
/// </summary>
public abstract class AudioDevice(ILogger logger) : IDisposable
{
    /// <summary>The rate streams and buffers are usually made at, and what the device is asked to run at.</summary>
    public const int DefaultSampleRate = 48000;

    private readonly Lock _lock = new();
    private readonly HashSet<AudioBuffer> _buffers = [];
    private readonly ManualResetEventSlim _wake = new(false);
    private AudioStream[] _streams = [];
    private AudioStream? _refilling;
    private int _refillThread;
    private Thread? _feeder;
    private volatile bool _closing;

    protected ILogger Logger { get; } = logger.ForContext<AudioDevice>();

    public abstract string Name { get; }
    public abstract int SampleRate { get; }

    /// <summary>
    ///     How far behind the mixer the speakers are: the device's own buffering. Positions don't
    ///     include it; subtract it to line a clock up with what is heard.
    /// </summary>
    public abstract double LatencySeconds { get; }

    /// <summary>True while a browser keeps the page's audio locked until a gesture. Always false on desktop.</summary>
    public abstract bool Suspended { get; }

    /// <summary>Master gain over everything on the device: 1 leaves it as it is, past 1 amplifies.</summary>
    public abstract float Volume { get; set; }

    /// <summary>True for the device that only keeps time: audio is off, or no output opened.</summary>
    public bool Silent => this is NullAudioDevice;

    /// <summary>
    ///     Opens the output whose name contains <paramref name="device" /> (any case), or the
    ///     default output without one or when nothing matches. Falls back to a silent device when
    ///     no output opens or <paramref name="silent" /> is set.
    /// </summary>
    public static AudioDevice Open(ILogger logger, string? device = null, bool silent = false,
        int sampleRate = DefaultSampleRate)
    {
        if (silent) return new NullAudioDevice(logger, sampleRate);
        return (AudioDevice?)MiniaudioDevice.TryOpen(logger, MiniaudioBackend.Default, device, sampleRate)
               ?? new NullAudioDevice(logger, sampleRate);
    }

    /// <summary>
    ///     Copies <paramref name="samples" /> - interleaved frames of <paramref name="format" /> - to
    ///     memory of the device's own. 8- and 16-bit stay that size, rather than widened to float.
    /// </summary>
    public AudioBuffer CreateBuffer(ReadOnlySpan<byte> samples, SampleFormat format, int channels, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        var frame = format.Size() * channels;
        if (samples.Length % frame != 0)
            throw new ArgumentException($"{samples.Length} bytes is not a whole number of {frame}-byte frames.",
                nameof(samples));

        ObjectDisposedException.ThrowIf(_closing, this);
        // the copy runs outside the lock: a whole song's samples shouldn't hold up other uploads
        var buffer = CreateBufferCore(samples, format, channels, sampleRate, samples.Length / frame);
        lock (_lock)
        {
            if (!_closing)
            {
                _buffers.Add(buffer);
                return buffer;
            }
        }

        buffer.Dispose();
        throw new ObjectDisposedException(GetType().FullName);
    }

    public AudioBuffer CreateBuffer(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        return CreateBuffer(System.Runtime.InteropServices.MemoryMarshal.AsBytes(interleaved), SampleFormat.Float32,
            channels, sampleRate);
    }

    /// <summary>
    ///     A push stream of interleaved float frames that holds up to <paramref name="capacityFrames" />
    ///     not played yet. Starts paused and empty.
    /// </summary>
    public AudioStream CreateStream(int sampleRate, int channels, int capacityFrames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityFrames, 1);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            var stream = CreateStreamCore(sampleRate, channels, capacityFrames);
            _streams = [.._streams, stream];
            if (_feeder != null || OperatingSystem.IsBrowser() || OperatingSystem.IsWasi()) return stream;
            
            _feeder = new Thread(Feed)
                { IsBackground = true, Name = "Sundex Audio Thread", Priority = ThreadPriority.AboveNormal };
            _feeder.Start();

            return stream;
        }
    }

    public void Dispose()
    {
        AudioStream[] streams;
        AudioBuffer[] buffers;
        lock (_lock)
        {
            if (_closing) return;
            _closing = true;
            streams = _streams;
            buffers = [.._buffers];
        }

        _wake.Set();
        _feeder?.Join();
        foreach (var stream in streams) stream.Dispose();
        foreach (var buffer in buffers) buffer.Dispose();
        Close();
        _wake.Dispose();
        GC.SuppressFinalize(this);
    }

    protected abstract AudioBuffer CreateBufferCore(ReadOnlySpan<byte> samples, SampleFormat format, int channels,
        int sampleRate, long frames);

    protected abstract AudioStream CreateStreamCore(int sampleRate, int channels, int capacityFrames);

    /// <summary>Frees the device itself, after every buffer and stream on it is gone.</summary>
    protected abstract void Close();

    internal void Forget(AudioBuffer buffer)
    {
        lock (_lock) _buffers.Remove(buffer);
    }

    internal void Forget(AudioStream stream)
    {
        lock (_lock)
        {
            var index = Array.IndexOf(_streams, stream);
            if (index < 0) return;
            _streams = [.._streams[..index], .._streams[(index + 1)..]];
        }
    }

    internal void Wake()
    {
        _wake.Set();
    }

    /// <summary>
    ///     Returns once the feeder is not inside <paramref name="stream" />'s refill, which it won't
    ///     enter again once the stream is disposed. A refill disposing its own stream returns at once.
    /// </summary>
    internal void AwaitRefill(AudioStream stream)
    {
        if (Environment.CurrentManagedThreadId == Volatile.Read(ref _refillThread)) return;
        Interlocked.MemoryBarrier(); // the disposed flag is visible before the check below
        var spin = new SpinWait();
        while (Volatile.Read(ref _refilling) == stream) spin.SpinOnce();
    }

    /// <summary>
    ///     Calls every stream's <see cref="AudioStream.Refill" /> once, on this thread. Only for a
    ///     host without threads, the browser, which calls it from a timer; everywhere else the
    ///     device's feeder thread does this, and calling it as well throws.
    /// </summary>
    public void Pump()
    {
        if (Volatile.Read(ref _feeder) != null)
            throw new InvalidOperationException("This device feeds its streams on a thread of its own.");
        if (!_closing) RefillAll();
    }

    private void Feed()
    {
        while (!_closing)
        {
            // a request made while this pass runs wakes the next wait straight away
            _wake.Reset();
            RefillAll();
            _wake.Wait(10);
        }
    }

    private void RefillAll()
    {
        Volatile.Write(ref _refillThread, Environment.CurrentManagedThreadId);
        foreach (var stream in Volatile.Read(ref _streams))
        {
            var refill = stream.Refill;
            if (refill == null) continue;

            // claimed before the disposed check, so Dispose either sees the claim or we see it
            Interlocked.Exchange(ref _refilling, stream);
            try
            {
                if (!stream.IsDisposed) refill();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "[Audio] A stream's refill threw");
            }
            finally
            {
                Volatile.Write(ref _refilling, null);
            }
        }
    }
}
