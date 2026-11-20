namespace Sundex.Engine.Renderer;

public enum VertexStepRate { Vertex, Instance }
public enum VertexFormat { Float, UInt, UShort, Byte, NormalizedByte }

public readonly record struct VertexBufferElement(VertexFormat Format, int Count, int Offset);

public sealed class VertexBufferLayout(VertexStepRate stepRate = VertexStepRate.Vertex)
{
    private readonly List<VertexBufferElement> _elements = [];
    private int _stride;

    public VertexStepRate StepRate { get; } = stepRate;

    public VertexBufferLayout PushFloat(int count) => Push(VertexFormat.Float, count, sizeof(float));
    public VertexBufferLayout PushUInt(int count) => Push(VertexFormat.UInt, count, sizeof(uint));
    public VertexBufferLayout PushUShort(int count) => Push(VertexFormat.UShort, count, sizeof(ushort));
    public VertexBufferLayout PushByte(int count, bool normalized = false) =>
        Push(normalized ? VertexFormat.NormalizedByte : VertexFormat.Byte, count, sizeof(byte));

    public VertexBufferLayout PushMatrix4(int count) => PushMatrix(4, 4, count);

    public VertexBufferLayout PushMatrix(int x, int y, int count)
    {
        if (StepRate != VertexStepRate.Instance)
            throw new InvalidOperationException("Matrix attributes require an instance-rate layout.");
        ArgumentOutOfRangeException.ThrowIfLessThan(y, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(y, 4);

        for (var index = 0; index < count * y; index++) PushFloat(x);
        return this;
    }

    public VertexBufferLayout Pad(int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        _stride += bytes;
        return this;
    }

    public IReadOnlyList<VertexBufferElement> GetElements() => _elements.AsReadOnly();
    public int GetStride() => _stride;

    internal VertexBufferLayout Snapshot()
    {
        var snapshot = new VertexBufferLayout(StepRate) { _stride = _stride };
        snapshot._elements.AddRange(_elements);
        return snapshot;
    }

    private VertexBufferLayout Push(VertexFormat format, int count, int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 4);
        _elements.Add(new VertexBufferElement(format, count, _stride));
        return Pad(count * size);
    }
}
