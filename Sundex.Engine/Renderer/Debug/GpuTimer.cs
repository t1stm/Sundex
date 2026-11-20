namespace Sundex.Engine.Renderer.Debug;

/// <summary>Collects completed GPU timings without waiting for the GPU.</summary>
public abstract class GpuTimer : IDisposable
{
    public List<double> Samples { get; } = [];
    public abstract void Begin(ICommandList commands);
    public abstract void End(ICommandList commands);
    public abstract void Collect();
    public abstract void Dispose();

    /// <summary>
    ///     The nearest-rank percentile of <paramref name="samples" />: the smallest sample
    ///     that at least <paramref name="percent" />% of them are less than or equal to.
    ///     The input is left unsorted.
    /// </summary>
    public static double Percentile(IReadOnlyList<double> samples, double percent)
    {
        if (samples.Count == 0) throw new ArgumentException("No samples.", nameof(samples));

        var sorted = samples.Order().ToArray();
        var rank = (int)Math.Ceiling(percent / 100 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

}
