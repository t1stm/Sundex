using OpenTK.Graphics.OpenGL;

namespace Sundex.Engine.Renderer.OpenGL;

internal sealed class GLVertexArray : GLResource
{
    private readonly GLResource _shader;
    private readonly GLResource _indices;
    private readonly GLResource[] _vertices;
    private readonly VertexArrayCache _cache;
    private readonly int _hash;

    public GLVertexArray(GLGraphicsDevice device, GLResource shader, GLResource indices,
        ReadOnlySpan<GLResource?> vertices, VertexBufferLayout[] layouts, VertexArrayCache cache, int hash)
        : base(device, GL.GenVertexArray(), GL.DeleteVertexArray)
    {
        _shader = shader;
        _indices = indices;
        _vertices = new GLResource[vertices.Length];
        _cache = cache;
        _hash = hash;

        GL.BindVertexArray(Handle);
        if (device.SupportsDirectStateAccess)
            GL.VertexArrayElementBuffer(Handle, indices.Handle);
        else
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, indices.Handle);

        uint attribute = 0;
        for (var slot = 0; slot < vertices.Length; slot++)
        {
            var buffer = _vertices[slot] = vertices[slot]!;
            GL.BindBuffer(BufferTarget.ArrayBuffer, buffer.Handle);
            var layout = layouts[slot];
            foreach (var element in layout.GetElements())
            {
                var offset = (nint)element.Offset;
                var stride = layout.GetStride();
                switch (element.Format)
                {
                    case VertexFormat.Float:
                    case VertexFormat.NormalizedByte:
                        var normalized = element.Format == VertexFormat.NormalizedByte;
                        GL.VertexAttribPointer(attribute, element.Count,
                            normalized ? VertexAttribPointerType.UnsignedByte : VertexAttribPointerType.Float,
                            normalized, stride, offset);
                        break;
                    default:
                        GL.VertexAttribIPointer(attribute, element.Count, element.Format switch
                        {
                            VertexFormat.UInt => VertexAttribIType.UnsignedInt,
                            VertexFormat.UShort => VertexAttribIType.UnsignedShort,
                            VertexFormat.Byte => VertexAttribIType.UnsignedByte,
                            _ => throw new ArgumentOutOfRangeException(nameof(layouts))
                        }, stride, offset);
                        break;
                }

                GL.EnableVertexAttribArray(attribute);
                if (layout.StepRate == VertexStepRate.Instance) GL.VertexAttribDivisor(attribute, 1);
                attribute++;
            }
            buffer.AddArray(this);
        }

        indices.AddArray(this);
        shader.AddArray(this);
        cache.Add(hash, this);
    }

    public bool Matches(GLResource indices, ReadOnlySpan<GLResource?> vertices) =>
        _indices == indices && vertices.SequenceEqual(_vertices.AsSpan());

    public override void Destroy()
    {
        _cache.Remove(_hash, this);
        _shader.RemoveArray(this);
        _indices.RemoveArray(this);
        foreach (var vertex in _vertices) vertex.RemoveArray(this);
        base.Destroy();
    }
}

internal sealed class VertexArrayCache
{
    private readonly Dictionary<int, List<GLVertexArray>> _buckets = [];
    private GLVertexArray? _last;

    public void Add(int hash, GLVertexArray array)
    {
        if (!_buckets.TryGetValue(hash, out var bucket)) _buckets.Add(hash, bucket = []);
        bucket.Add(array);
        _last = array;
    }

    public void Remove(int hash, GLVertexArray array)
    {
        if (_last == array) _last = null;
        var bucket = _buckets[hash];
        bucket.Remove(array);
        if (bucket.Count == 0) _buckets.Remove(hash);
    }

    public GLVertexArray? Get(int hash, GLResource indices, ReadOnlySpan<GLResource?> vertices)
    {
        if (_last is not null && _last.Matches(indices, vertices)) return _last;
        if (!_buckets.TryGetValue(hash, out var bucket)) return null;
        foreach (var array in bucket)
            if (array.Matches(indices, vertices)) return _last = array;
        return null;
    }
}
