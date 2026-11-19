using System.Diagnostics;

namespace Sundex.Core;

public class SeekableStopwatch : ISeekableStopwatch
{
    protected readonly SemaphoreSlim Lock = new(1);
    protected TimeSpan LastValue = TimeSpan.Zero;

    protected bool Running;
    protected long StartTime = long.MinValue;
    protected long? StopTime;

    public TimeSpan Elapsed
    {
        get
        {
            Lock.Wait();
            if (!Running)
            {
                Lock.Release();
                return LastValue;
            }

            var currentTime = GetCurrentTime();

            if (StopTime != null)
            {
                StartTime += currentTime - StopTime.Value;
                StopTime = null;
            }

            var val = LastValue = Stopwatch.GetElapsedTime(StartTime, currentTime);

            Lock.Release();
            return val;
        }
    }

    public long ElapsedMilliseconds => (long)Elapsed.TotalMilliseconds;
    public bool IsRunning => Running;

    public void Start()
    {
        if (Running) return;
        if (StartTime == long.MinValue)
            Restart();
        Running = true;
    }

    public void Restart()
    {
        Lock.Wait();

        Running = true;
        StopTime = null;
        StartTime = GetCurrentTime();

        Lock.Release();
    }

    public void Reset()
    {
        Lock.Wait();

        Running = false;
        StopTime = null;
        LastValue = TimeSpan.Zero;

        Lock.Release();
    }

    public void Stop()
    {
        if (!Running) return;

        Lock.Wait();

        Running = false;
        StopTime = Stopwatch.GetTimestamp();

        Lock.Release();
    }

    public void Seek(long delta)
    {
        Lock.Wait();

        var wantedTime = TimeSpan.FromMilliseconds(delta);
        LastValue = wantedTime;

        var current = GetCurrentTime();
        var deltaTime = current - wantedTime.Ticks * Stopwatch.Frequency / 10000000;
        StartTime = deltaTime;

        // Anchored whenever the clock is not accruing, so the wait between a seek and the
        // start that follows it is not counted as elapsed time. Covers a clock that has
        // never run, which has no StopTime of its own.
        if (!Running || StopTime != null) StopTime = current;

        Lock.Release();
    }

    protected static long GetCurrentTime()
    {
        return Stopwatch.GetTimestamp();
    }
}