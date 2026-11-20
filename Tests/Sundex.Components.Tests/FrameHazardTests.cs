using Serilog;
using Serilog.Core;
using Serilog.Events;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Buffers;

namespace Sundex.Components.Tests;

public class FrameHazardTests
{
    [Fact]
    public void ReportsAWriteAfterAReadInTheSameFrameOnce()
    {
        var sink = new ListSink();
        var hazards = new FrameHazards(new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger());
        using var buffer = new GpuBuffer<int>.WithCPUCache(BufferUsage.Vertex);

        hazards.BeginFrame();
        buffer.SetData([1, 2]);
        lock (buffer.Backing) buffer.Backing.MarkRead(hazards, null, "glyphs");
        buffer[0] = 3;
        buffer.SetData([4]);
        hazards.EndFrame();
        buffer.SetData([5]);
        hazards.BeginFrame();
        buffer[0] = 6;

        var report = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Error, report.Level);
        Assert.Contains("glyphs", report.RenderMessage());
    }

    private sealed class ListSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
