using System.Diagnostics;
using Serilog;

namespace Sundex.Engine.Audio.Null;

/// <summary>
///     Plays nothing but keeps time like a device would, so a clock read off a voice or a stream
///     still moves when audio is off or no output opens: voices advance while unpaused and stop at
///     their end, streams advance while unpaused and stall where the data runs out.
/// </summary>
internal sealed class NullAudioDevice(ILogger logger, int sampleRate) : AudioDevice(logger)
{
    public override string Name => "Null";
    public override int SampleRate { get; } = sampleRate;
    public override double LatencySeconds => 0;
    public override bool Suspended => false;
    public override float Volume { get; set; } = 1;

    protected override AudioBuffer CreateBufferCore(ReadOnlySpan<byte> samples, SampleFormat format, int channels,
        int rate, long frames)
    {
        return new Buffer(this, channels, rate, frames);
    }

    protected override AudioStream CreateStreamCore(int rate, int channels, int capacityFrames)
    {
        return new Stream(this, rate, channels, capacityFrames);
    }

    protected override void Close()
    {
    }

    /// <summary>A playhead in frames that runs on the stopwatch while unpaused, up to a limit.</summary>
    private struct Clock(int rate)
    {
        private long _last = Stopwatch.GetTimestamp();
        private bool _running;

        public double Frames { get; private set; }

        /// <summary>Frames per stopwatch frame: a stream's <see cref="AudioStream.Rate" />.</summary>
        public double Speed { get; set; } = 1;

        public void Advance(double limit)
        {
            var now = Stopwatch.GetTimestamp();
            if (_running) Frames = Math.Min(Frames + (double)(now - _last) * rate * Speed / Stopwatch.Frequency, limit);
            _last = now;
        }

        public void Run(bool running, double limit)
        {
            Advance(limit);
            _running = running;
        }

        public void Set(double frames)
        {
            _last = Stopwatch.GetTimestamp();
            Frames = frames;
        }
    }

    private sealed class Buffer(AudioDevice device, int channels, int rate, long frames)
        : AudioBuffer(device, channels, rate, frames)
    {
        protected override void PlayOnceCore(float volume, float pan)
        {
        }

        protected override AudioVoice CreateVoiceCore()
        {
            return new Voice(this);
        }

        protected override void Free()
        {
        }
    }

    private sealed class Voice(AudioBuffer buffer) : AudioVoice(buffer)
    {
        private Clock _clock = new(buffer.SampleRate);
        private bool _ended;

        protected override long GetPosition()
        {
            _clock.Advance(Buffer.Frames);
            if (_clock.Frames >= Buffer.Frames) _ended = true;
            return (long)_clock.Frames;
        }

        protected override void SeekTo(long frame)
        {
            _clock.Set(frame);
            _ended = false;
        }

        protected override void SetPaused(bool paused)
        {
            // as a real voice does, one unpaused at its end plays again from the start
            GetPosition();
            if (!paused && _ended) SeekTo(0);
            _clock.Run(!paused, Buffer.Frames);
        }

        protected override void SetVolume(float volume)
        {
        }

        protected override void SetPan(float pan)
        {
        }

        protected override void Free()
        {
        }
    }

    private sealed class Stream(AudioDevice device, int rate, int channels, int capacityFrames)
        : AudioStream(device, rate, channels, capacityFrames)
    {
        private long _base;
        private Clock _clock = new(rate);
        private long _put;

        protected override long GetQueued()
        {
            _clock.Advance(_put);
            return _put - (long)_clock.Frames;
        }

        protected override long GetPlayed()
        {
            _clock.Advance(_put);
            return _base + (long)_clock.Frames;
        }

        protected override int PutCore(ReadOnlySpan<float> interleaved)
        {
            var frames = (int)Math.Min(interleaved.Length / Channels, CapacityFrames - GetQueued());
            _put += frames;
            return frames;
        }

        protected override void FlushCore(long basePosition)
        {
            _base = basePosition;
            _put = 0;
            _clock.Set(0);
        }

        protected override void SetPaused(bool paused)
        {
            _clock.Run(!paused, _put);
        }

        protected override void SetVolume(float volume)
        {
        }

        protected override void SetRate(float rate)
        {
            _clock.Advance(_put);
            _clock.Speed = rate;
        }

        protected override void Free()
        {
        }
    }
}
