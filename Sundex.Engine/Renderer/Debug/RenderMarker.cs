using Sundex.Reactive;

namespace Sundex.Engine.Renderer.Debug;

public static class RenderMarker
{
    internal delegate void MarkerSink(ReadOnlySpan<char> message, MarkerType markerType);

    [ThreadStatic] internal static MarkerSink? Sink;

    public static bool Enabled
    {
        get => field && SundexDevelopment.Enabled && Sink is not null;
        set;
    } = true;

    public static void Debug(ReadOnlySpan<char> message, MarkerType markerType = MarkerType.Visible)
    {
        if (Enabled) Sink?.Invoke(message, markerType);
    }

    public static void Debug(ReadOnlySpan<char> message1, ReadOnlySpan<char> message2,
        MarkerType markerType = MarkerType.Visible)
    {
        if (!Enabled) return;
        Span<char> message = stackalloc char[message1.Length + message2.Length];
        message1.CopyTo(message);
        message2.CopyTo(message[message1.Length..]);
        Sink?.Invoke(message, markerType);
    }
}
