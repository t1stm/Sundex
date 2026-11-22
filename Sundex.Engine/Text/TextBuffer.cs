using OpenTK.Mathematics;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Abstract;
using Sundex.Engine.Renderer.Buffers;

namespace Sundex.Engine.Text;

public class TextBuffer(ITextProvider provider) : IRenderable, IClippable, IDisposable
{
    public readonly GpuBuffer<TextCharacter>.WithCPUCache Characters = new(BufferUsage.Vertex);
    public readonly ITextProvider TextProvider = provider;

    private readonly List<Range> _freeRanges = [];
    private readonly Dictionary<TextSlice, Range> _usedRanges = [];
    // Guards the ranges and the next free offset, since slices are taken and returned from any thread.
    private readonly Lock _lock = new();

    private int _currentOffset;
    private bool _disposing;

    /// <inheritdoc />
    public Vector4i? ClipRect { get; set; }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposing = true;
            foreach (var (textSlice, _) in _usedRanges) textSlice.Dispose();
        }

        Characters.Dispose();

        GC.SuppressFinalize(this);
    }

    public void Render(in RenderContext context) => RenderBuffer(context);

    public void Resize(int newSize)
    {
        Characters.ResizeCPUBuffer(newSize);
    }

    public TextSlice GetTextSlice(ReadOnlySpan<char> text, int capacity = -1)
    {
        return GetTextSlice(text, (value, buffer, range) => new TextSlice(buffer, range)
        {
            Value = value
        }, capacity);
    }

    public TextSlice GetTextSlice(ReadOnlySpan<char> text,
        Func<ReadOnlySpan<char>, TextBuffer, Range, TextSlice> factory, int capacity = -1)
    {
        var range = TakeRange(capacity < 0 ? text.Length : capacity);
        var slice = factory(text, this, range);
        lock (_lock) _usedRanges.Add(slice, range);

        return slice;
    }

    /// <summary>
    ///     Forgets every slice at once and starts laying text out from the top again, keeping
    ///     the characters' storage - for an owner that refills the buffer and hands its old
    ///     slices back in through <see cref="Reuse" /> rather than allocating new ones. A slice
    ///     from before the reset is dead until then: don't set it or dispose it.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _usedRanges.Clear();
            _freeRanges.Clear();
            _currentOffset = 0;
        }
    }

    /// <summary>
    ///     <see cref="GetTextSlice(ReadOnlySpan{char}, int)" /> into a slice this buffer handed
    ///     out before the last <see cref="Reset" />, so a refill allocates nothing. The slice
    ///     keeps its own settings (font size, color, manual updates).
    /// </summary>
    public void Reuse(TextSlice slice, ReadOnlySpan<char> text, int capacity = -1)
    {
        var range = TakeRange(capacity < 0 ? text.Length : capacity);
        slice.Rebind(range);
        lock (_lock) _usedRanges.Add(slice, range);

        slice.Value = text;
    }

    private Range TakeRange(int capacity)
    {
        lock (_lock)
        {
            // Doubling, as a list grows: a chunk adds its slices one at a time, and growing
            // to the exact size copied the whole buffer on every one of them.
            var charactersCapacity = Characters.Capacity;
            if (charactersCapacity < _currentOffset + capacity)
                Characters.ResizeCPUBuffer(charactersCapacity =
                    Math.Max(_currentOffset + capacity, charactersCapacity * 2));

            if (GetFreeRangeIfExists(capacity, charactersCapacity) is { } free) return free;

            var taken = new Range(_currentOffset, _currentOffset + capacity);
            _currentOffset += capacity;
            return taken;
        }
    }

    /// <summary>Takes a freed range that fits. The caller holds <see cref="_lock" />.</summary>
    private Range? GetFreeRangeIfExists(int capacity, int charactersCapacity)
    {
        for (var index = 0; index < _freeRanges.Count; index++)
        {
            var (offset, length) = _freeRanges[index].GetOffsetAndLength(charactersCapacity);
            if (length < capacity) continue;

            _freeRanges.RemoveAt(index);
            if (length > capacity) _freeRanges.Add(new Range(offset + capacity, offset + length));
            return new Range(offset, offset + capacity);
        }

        return null;
    }

    public void RenderBuffer(in RenderContext context, int endIndex = -1)
    {
        if (endIndex < 0)
            lock (_lock) endIndex = _currentOffset;

        if (endIndex == 0) return;
        var commands = context.Commands;
        TextProvider.Bind(commands);
        commands.SetVertexBuffer(0, UnitQuad.Positions);
        commands.SetVertexBuffer(1, Characters);
        commands.SetIndexBuffer(UnitQuad.Indices);
        commands.DrawIndexed(UnitQuad.IndexCount, endIndex);
        RenderMarker.Debug("Rendered Text Buffer", MarkerType.Hidden);
    }

    public void Remove(TextSlice textSlice)
    {
        Range range;
        bool disposing;
        lock (_lock)
        {
            // Taken out first, so a slice disposed twice frees its range once.
            if (!_usedRanges.Remove(textSlice, out range)) return;
            disposing = _disposing;
        }

        if (!disposing)
        {
            var (offset, length) = range.GetOffsetAndLength(Characters.Capacity);
            for (var index = offset; index < offset + length; index++) Characters[index] = new TextCharacter();
        }

        // Freed only once cleared, so no one is handed a range that still shows the old text.
        lock (_lock) _freeRanges.Add(range);
    }
}
