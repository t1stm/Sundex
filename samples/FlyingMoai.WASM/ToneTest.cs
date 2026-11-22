using Sundex.Engine.Audio;

namespace FlyingMoai.WASM;

/// <summary>
///     The browser audio test behind <c>?audiotest</c>: a 440 Hz tone kept 2 s ahead on a stream,
///     counting the times its queue ran dry while playing. main.js loads the main thread with long
///     frames and a full collection while it runs, and prints the report.
/// </summary>
internal static class ToneTest
{
    private const int Rate = 48000, Block = 1024;

    private static AudioStream? _stream;
    private static readonly float[] Samples = new float[2 * Block];
    private static long _phase;
    private static bool _fed;
    private static int _underruns;
    private static int _refills;
    private static byte[][] _garbage = [];

    public static void Start(AudioDevice device, float volume)
    {
        var stream = _stream = device.CreateStream(Rate, 2, 3 * Rate);
        stream.Volume = volume;
        stream.Refill = () =>
        {
            _refills++;
            if (stream.QueuedFrames == 0 && _fed) _underruns++;
            while (stream.QueuedFrames < 2 * Rate)
            {
                for (var i = 0; i < Block; i++)
                    Samples[2 * i] = Samples[2 * i + 1] = 0.2f * MathF.Sin(2 * MathF.PI * 440 * (_phase + i) / Rate);
                _phase += stream.Put(Samples);
                _fed = true;
            }
        };
        stream.Paused = false;
    }

    public static string Report(AudioDevice device)
    {
        return $"device={device.Name} rate={device.SampleRate} suspended={device.Suspended} " +
               $"latency={device.LatencySeconds * 1000:F1}ms played={_stream?.PlayedPosition} " +
               $"queued={_stream?.QueuedFrames} refills={_refills} underruns={_underruns}";
    }

    /// <summary>Fills the heap with <paramref name="megabytes" /> of live arrays, then collects everything.</summary>
    public static void Stress(int megabytes)
    {
        _garbage = new byte[megabytes][];
        for (var i = 0; i < megabytes; i++) _garbage[i] = new byte[1 << 20];
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        _garbage = [];
    }
}
