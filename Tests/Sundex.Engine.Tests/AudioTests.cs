using System.Diagnostics;
using Serilog.Core;
using Sundex.Engine.Audio;
using Sundex.Engine.Audio.Miniaudio;
using Sundex.Engine.Audio.Null;

namespace Sundex.Engine.Tests;

/// <summary>
///     The same checks against both backends: the managed null device, and miniaudio through the
///     native library on its null backend, which runs in real time with no hardware.
/// </summary>
public class AudioTests
{
    private const int Rate = AudioDevice.DefaultSampleRate;

    public static TheoryData<string> Backends => ["null", "miniaudio"];

    private static AudioDevice Open(string backend)
    {
        return backend == "null"
            ? new NullAudioDevice(Logger.None, Rate)
            : MiniaudioDevice.TryOpen(Logger.None, MiniaudioBackend.Null, null, Rate)
              ?? throw new InvalidOperationException("The native audio library did not load.");
    }

    private static float[] Frames(int frames, int channels = 2)
    {
        return new float[frames * channels];
    }

    private static void Wait(int milliseconds)
    {
        Thread.Sleep(milliseconds);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void StreamPlayheadAdvancesInRealTimeAtTheRate(string backend)
    {
        foreach (var rate in new[] { 1f, 1.5f })
        {
            using var device = Open(backend);
            using var stream = device.CreateStream(Rate, 2, 4 * Rate);
            Assert.Equal(3 * Rate, stream.Put(Frames(3 * Rate)));
            stream.Rate = rate;

            var clock = Stopwatch.StartNew();
            stream.Paused = false;
            Wait(1000);
            var played = stream.PlayedPosition;
            var expected = clock.Elapsed.TotalSeconds * Rate * rate;

            Assert.InRange(played, expected * 0.95, expected * 1.05);
            Assert.Equal(3 * Rate - played, stream.QueuedFrames, 0.05 * Rate);
        }
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void StreamStallsOnTheLastFrameAndPlaysOnWithTheNextPut(string backend)
    {
        using var device = Open(backend);
        using var stream = device.CreateStream(Rate, 2, Rate);
        stream.Put(Frames(Rate / 10));
        stream.Paused = false;
        Wait(300);
        Assert.Equal(Rate / 10, stream.PlayedPosition);
        Assert.Equal(0, stream.QueuedFrames);

        stream.Put(Frames(Rate / 10));
        Wait(300);
        Assert.Equal(Rate / 5, stream.PlayedPosition);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void FlushDropsTheQueueAndRestartsThePlayheadAtTheBase(string backend)
    {
        using var device = Open(backend);
        using var stream = device.CreateStream(Rate, 2, 2 * Rate);
        stream.Put(Frames(Rate));
        stream.Paused = false;
        Wait(100);

        stream.Flush(5000);
        Assert.Equal(5000, stream.PlayedPosition);
        Assert.Equal(0, stream.QueuedFrames);

        // the whole capacity is free again straight away
        Assert.Equal(2 * Rate, stream.Put(Frames(2 * Rate)));
        Wait(200);
        Assert.InRange(stream.PlayedPosition, 5000 + Rate / 10, 5000 + Rate / 2);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void PutTakesOnlyWhatFits(string backend)
    {
        using var device = Open(backend);
        using var stream = device.CreateStream(Rate, 2, 1000);
        Assert.Equal(1000, stream.Put(Frames(1500)));
        Assert.Equal(0, stream.Put(Frames(10)));
        Assert.Throws<ArgumentException>(() => stream.Put(new float[3]));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void PuttingAndReadingTheQueueAllocateNothing(string backend)
    {
        using var device = Open(backend);
        using var stream = device.CreateStream(Rate, 2, 4096);
        var block = Frames(1024);
        stream.Put(block);
        _ = stream.QueuedFrames + stream.PlayedPosition;
        stream.Flush(0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            stream.Put(block);
            _ = stream.QueuedFrames + stream.PlayedPosition;
            if (i % 3 == 2) stream.Flush(i);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void TheFeederCallsRefillAndComesSoonerWhenAsked(string backend)
    {
        using var device = Open(backend);
        using var stream = device.CreateStream(Rate, 2, Rate);
        var calls = 0;
        using var called = new ManualResetEventSlim();
        stream.Refill = () =>
        {
            Interlocked.Increment(ref calls);
            called.Set();
        };

        Assert.True(called.Wait(1000, TestContext.Current.CancellationToken));

        // it keeps ticking unasked; a loaded machine stretches the 10 ms waits, so allow it time
        var clock = Stopwatch.StartNew();
        while (Volatile.Read(ref calls) < 5 && clock.ElapsedMilliseconds < 2000) Wait(10);
        Assert.True(Volatile.Read(ref calls) >= 5);

        // and it waits between ticks rather than spinning: about 20 in 200 ms, not thousands
        var before = Volatile.Read(ref calls);
        Wait(200);
        Assert.InRange(Volatile.Read(ref calls) - before, 1, 100);

        // between 10 ms ticks, a request is answered without waiting for the next one
        called.Reset();
        stream.RequestRefill();
        Assert.True(called.Wait(1000, TestContext.Current.CancellationToken));
        // the browser's Pump, called next to the feeder thread, would refill on two threads at once
        Assert.Throws<InvalidOperationException>(device.Pump);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void DisposeWaitsOutARefillInFlightAndNoneRunsAfter(string backend)
    {
        using var device = Open(backend);
        var stream = device.CreateStream(Rate, 2, Rate);
        var finished = false;
        var calls = 0;
        using var started = new ManualResetEventSlim();
        stream.Refill = () =>
        {
            Interlocked.Increment(ref calls);
            started.Set();
            Thread.Sleep(200);
            Volatile.Write(ref finished, true);
        };

        Assert.True(started.Wait(1000, TestContext.Current.CancellationToken));
        stream.Dispose();
        Assert.True(Volatile.Read(ref finished));

        var after = Volatile.Read(ref calls);
        Wait(100);
        Assert.Equal(after, Volatile.Read(ref calls));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void VoicesOnOneBufferKeepPlayheadsOfTheirOwn(string backend)
    {
        using var device = Open(backend);
        var buffer = device.CreateBuffer(Frames(2 * Rate, 1), 1, Rate);
        Assert.Equal(2 * Rate, buffer.Frames);

        using var moving = buffer.CreateVoice();
        using var held = buffer.CreateVoice();
        Assert.Equal(0, moving.Position);
        Assert.True(moving.Paused);

        moving.Seek(Rate / 2);
        moving.Paused = false;
        Wait(200);
        Assert.InRange(moving.Position, Rate / 2 + Rate / 20, Rate / 2 + Rate / 2);
        Assert.Equal(0, held.Position);

        held.Seek(1000);
        Assert.Equal(1000, held.Position);

        // the buffer takes its voices with it; using them afterwards is harmless
        buffer.Dispose();
        Assert.Equal(0, moving.Position);
        moving.Paused = true;
        moving.Seek(5);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void AVoiceStopsAtTheEndAndUnpausingPlaysItAgain(string backend)
    {
        using var device = Open(backend);
        using var buffer = device.CreateBuffer(Frames(Rate / 10), 2, Rate);
        using var voice = buffer.CreateVoice();
        voice.Paused = false;
        Wait(300);
        Assert.Equal(Rate / 10, voice.Position);

        voice.Paused = true;
        voice.Paused = false;
        Wait(30);
        Assert.InRange(voice.Position, 0, Rate / 10 - 1);

        // seeking a voice that ran out, without pausing it, plays on from the new spot
        Wait(300);
        Assert.Equal(Rate / 10, voice.Position);
        voice.Seek(Rate / 20);
        Wait(20);
        Assert.InRange(voice.Position, Rate / 20 + 1, Rate / 10 - 1);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void SixteenBitBuffersKeepTheirSize(string backend)
    {
        using var device = Open(backend);
        using var buffer = device.CreateBuffer(new byte[4 * 300], SampleFormat.Int16, 2, 22050);
        Assert.Equal(300, buffer.Frames);
        Assert.Equal(22050, buffer.SampleRate);
        Assert.Throws<ArgumentException>(() => device.CreateBuffer(new byte[3], SampleFormat.Int16, 1, Rate));

        buffer.PlayOnce(0.5f, -1);
        buffer.PlayOnce();
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void DisposingTheDeviceDisposesWhatWasMadeOnIt(string backend)
    {
        var device = Open(backend);
        var stream = device.CreateStream(Rate, 2, Rate);
        var buffer = device.CreateBuffer(Frames(Rate), 2, Rate);
        var voice = buffer.CreateVoice();
        stream.Refill = () => stream.Put(Frames(256));
        stream.Paused = false;
        voice.Paused = false;
        Wait(50);

        device.Dispose();
        Assert.Equal(0, stream.Put(Frames(16)));
        Assert.Equal(0, voice.Position);
        stream.Dispose();
        voice.Dispose();
        Assert.Throws<ObjectDisposedException>(() => device.CreateStream(Rate, 2, Rate));
    }
}
