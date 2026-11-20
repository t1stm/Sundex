using OpenTK.Mathematics;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Abstract;
using Sundex.Engine.Renderer.Buffers;

namespace Sundex.Engine.Text;

public class TextBuffer : IRenderable, IClippable, IDisposable
{
    public readonly GpuBuffer<TextCharacter>.WithCPUCache Characters;
    public readonly ITextProvider TextProvider;

    private readonly List<Range> _freeRanges = [];
    private readonly Dictionary<TextSlice, Range> _usedRanges = [];

    private int _currentOffset;
    private bool _disposing;

    public TextBuffer(ITextProvider provider)
    {
        Characters = new GpuBuffer<TextCharacter>.WithCPUCache(BufferUsage.Vertex);
        TextProvider = provider;
    }

    /// <inheritdoc />
    public Vector4i? ClipRect { get; set; }

    public void Dispose()
    {
        _disposing = true;
        foreach (var (textSlice, _) in _usedRanges) textSlice.Dispose();

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
        lock (_usedRanges)
        {
            _usedRanges.Add(slice, range);
        }

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
        lock (_usedRanges)
        {
            _usedRanges.Clear();
        }

        lock (_freeRanges)
        {
            _freeRanges.Clear();
        }

        _currentOffset = 0;
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
        lock (_usedRanges)
        {
            _usedRanges.Add(slice, range);
        }

        slice.Value = text;
    }

    private Range TakeRange(int capacity)
    {
        int charactersCapacity;
        lock (Characters)
        {
            // Doubling, as a list grows: a chunk adds its slices one at a time, and growing
            // to the exact size copied the whole buffer on every one of them.
            if ((charactersCapacity = Characters.Capacity) < _currentOffset + capacity)
                Characters.ResizeCPUBuffer(charactersCapacity =
                    Math.Max(_currentOffset + capacity, charactersCapacity * 2));
        }

        var range = GetFreeRangeIfExists(capacity, charactersCapacity);
        if (range != null) return range.Value;

        var taken = new Range(_currentOffset, _currentOffset + capacity);
        _currentOffset += capacity;
        return taken;
    }

    private Range? GetFreeRangeIfExists(int capacity, int charactersCapacity)
    {
        lock (_freeRanges)
        {
            if (_freeRanges.Count <= 0) return null;
            for (var index = 0; index < _freeRanges.Count; index++)
            {
                var freeRange = _freeRanges[index];
                var (offset, length) = freeRange.GetOffsetAndLength(charactersCapacity);
                if (length < capacity) continue;

                _freeRanges.RemoveAt(index);

                if (length <= capacity) return new Range(offset, offset + capacity);
                var remainingRange = new Range(offset + capacity, offset + length);
                _freeRanges.Add(remainingRange);

                return new Range(offset, offset + capacity);
            }
        }

        return null;
    }

    public void RenderBuffer(in RenderContext context, int endIndex = -1)
    {
        if (endIndex < 0)
            endIndex = _currentOffset;

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
        lock (_usedRanges)
        {
            range = _usedRanges[textSlice];
        }

        if (!_disposing)
        {
            var (offset, length) = range.GetOffsetAndLength(Characters.Capacity);
            for (var index = offset; index < offset + length; index++) Characters[index] = new TextCharacter();
        }

        lock (_usedRanges)
        {
            _usedRanges.Remove(textSlice);
        }

        lock (_freeRanges)
        {
            _freeRanges.Add(range);
        }
    }
}
