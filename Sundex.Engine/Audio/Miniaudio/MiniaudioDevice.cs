using System.Runtime.InteropServices;
using Serilog;

namespace Sundex.Engine.Audio.Miniaudio;

internal enum MiniaudioBackend
{
    /// <summary>The platform's outputs: WASAPI, Core Audio, PulseAudio, ALSA and so on.</summary>
    Default = 0,

    /// <summary>miniaudio's null backend: plays nothing, in real time, with no hardware.</summary>
    Null = 1
}

/// <summary>An <see cref="AudioDevice" /> on miniaudio through the sundex_audio shim.</summary>
internal sealed class MiniaudioDevice : AudioDevice
{
    private const int DoesNotExist = -7; // MA_DOES_NOT_EXIST

    private readonly nint _handle;

    private MiniaudioDevice(ILogger logger, nint handle) : base(logger)
    {
        _handle = handle;
        Name = Marshal.PtrToStringUTF8(Native.DeviceName(handle)) ?? "";
        SampleRate = Native.DeviceSampleRate(handle);
    }

    public override string Name { get; }
    public override int SampleRate { get; }
    public override double LatencySeconds => Native.DeviceLatency(_handle);
    public override bool Suspended => Native.DeviceSuspended(_handle) != 0;

    public override float Volume
    {
        get => Volatile.Read(ref field);
        set
        {
            Volatile.Write(ref field, value);
            Native.DeviceSetVolume(_handle, value);
        }
    } = 1;

    /// <summary>Null when the library won't load or no output opens; says why in the log.</summary>
    public static MiniaudioDevice? TryOpen(ILogger logger, MiniaudioBackend backend, string? device, int sampleRate)
    {
        var log = logger.ForContext<MiniaudioDevice>();
        try
        {
            var result = Native.DeviceOpen((int)backend, device, sampleRate, out var handle);
            if (result == DoesNotExist)
            {
                log.Warning("[Audio] No output named {Device}. Using the default output", device);
                result = Native.DeviceOpen((int)backend, null, sampleRate, out handle);
            }

            if (result != 0)
            {
                log.Error("[Audio] miniaudio could not open an output (result {Result})", result);
                return null;
            }

            var opened = new MiniaudioDevice(logger, handle);
            log.Information("[Audio] Playing on {Name} at {Rate} Hz, {Latency:F1} ms latency", opened.Name,
                opened.SampleRate, opened.LatencySeconds * 1000);
            return opened;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            log.Error(exception, "[Audio] The native audio library is missing; audio is silent");
            return null;
        }
    }

    protected override unsafe AudioBuffer CreateBufferCore(ReadOnlySpan<byte> samples, SampleFormat format,
        int channels, int sampleRate, long frames)
    {
        nint handle;
        int result;
        fixed (byte* data = samples)
        {
            result = Native.BufferCreate(_handle, data, (int)format, channels, sampleRate, frames, out handle);
        }

        if (result != 0) throw new InvalidOperationException($"miniaudio could not hold the buffer (result {result}).");
        return new Buffer(this, handle, channels, sampleRate, frames);
    }

    protected override AudioStream CreateStreamCore(int sampleRate, int channels, int capacityFrames)
    {
        var result = Native.StreamCreate(_handle, sampleRate, channels, capacityFrames, out var handle);
        if (result != 0) throw new InvalidOperationException($"miniaudio could not make the stream (result {result}).");
        return new Stream(this, handle, sampleRate, channels, capacityFrames);
    }

    protected override void Close()
    {
        Native.DeviceClose(_handle);
    }

    private sealed class Buffer(AudioDevice device, nint handle, int channels, int sampleRate, long frames)
        : AudioBuffer(device, channels, sampleRate, frames)
    {
        protected override void PlayOnceCore(float volume, float pan)
        {
            var result = Native.BufferPlayOnce(handle, volume, pan);
            if (result != 0) throw new InvalidOperationException($"miniaudio could not play the buffer (result {result}).");
        }

        protected override AudioVoice CreateVoiceCore()
        {
            var result = Native.VoiceCreate(handle, out var voice);
            return result != 0 ? throw new InvalidOperationException($"miniaudio could not make the voice (result {result}).") : new Voice(this, voice);
        }

        protected override void Free()
        {
            Native.BufferFree(handle);
        }
    }

    private sealed class Voice(AudioBuffer buffer, nint handle) : AudioVoice(buffer)
    {
        protected override long GetPosition() => Native.VoicePosition(handle);
        protected override void SeekTo(long frame) => Native.VoiceSeek(handle, frame);
        protected override void SetPaused(bool paused) => Native.VoiceSetPaused(handle, paused ? 1 : 0);
        protected override void SetVolume(float volume) => Native.VoiceSetVolume(handle, volume);
        protected override void SetPan(float pan) => Native.VoiceSetPan(handle, pan);
        protected override void Free() => Native.VoiceFree(handle);
    }

    private sealed class Stream(AudioDevice device, nint handle, int sampleRate, int channels, int capacityFrames)
        : AudioStream(device, sampleRate, channels, capacityFrames)
    {
        protected override long GetQueued() => Native.StreamQueued(handle);
        protected override long GetPlayed() => Native.StreamPlayed(handle);

        protected override unsafe int PutCore(ReadOnlySpan<float> interleaved)
        {
            fixed (float* samples = interleaved)
            {
                return (int)Native.StreamPut(handle, samples, interleaved.Length / Channels);
            }
        }

        protected override void FlushCore(long basePosition) => Native.StreamFlush(handle, basePosition);
        protected override void SetPaused(bool paused) => Native.StreamSetPaused(handle, paused ? 1 : 0);
        protected override void SetVolume(float volume) => Native.StreamSetVolume(handle, volume);
        protected override void SetRate(float rate) => Native.StreamSetRate(handle, rate);
        protected override void Free() => Native.StreamFree(handle);
    }
}
