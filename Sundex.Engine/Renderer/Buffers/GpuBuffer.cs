using System.Runtime.InteropServices;
using Sundex.Engine.Renderer.Abstract;

namespace Sundex.Engine.Renderer.Buffers;

public enum BufferUsage { Vertex, Index, Uniform }

public abstract class GpuBuffer : IDisposable
{
    private protected GpuBuffer(BufferUsage usage) => Usage = usage;

    internal ResourceBacking Backing { get; } = new();
    public BufferUsage Usage { get; }
    public int Capacity { get; protected set; }

    internal abstract ReadOnlySpan<byte> GetUpload(out int byteOffset, out int byteCapacity);
    internal abstract void Uploaded();

    public void Dispose()
    {
        lock (Backing)
        {
            Backing.Dispose();
            ReleaseData();
        }
        GC.SuppressFinalize(this);
    }

    private protected abstract void ReleaseData();
}

public class GpuBuffer<T>(BufferUsage usage) : GpuBuffer(usage) where T : unmanaged
{
    private T[]? _pending;

    /// <summary>Copies data on the calling thread. The render thread uploads it on next use.</summary>
    public virtual void SetData(ReadOnlySpan<T> data)
    {
        lock (Backing)
        {
            Backing.ThrowIfDisposed();
            Backing.CheckWrite(this);
            if (_pending?.Length != data.Length) _pending = new T[data.Length];
            data.CopyTo(_pending);
            Capacity = data.Length;
        }
    }

    internal override ReadOnlySpan<byte> GetUpload(out int byteOffset, out int byteCapacity)
    {
        byteOffset = 0;
        var bytes = MemoryMarshal.AsBytes(_pending.AsSpan());
        byteCapacity = _pending is null ? -1 : bytes.Length;
        return bytes;
    }

    internal override void Uploaded() => _pending = null;
    private protected override void ReleaseData() => _pending = null;

    public class WithCPUCache(BufferUsage usage) : GpuBuffer<T>(usage), IIndexableCollection<int, T>
    {
        private T[] _data = [];
        private int _dirtyFrom = int.MaxValue;
        private int _dirtyTo;
        private bool _replace;

        public int Count => Capacity;
        public ReadOnlySpan<T> Data => _data;

        public T this[int index]
        {
            get
            {
                lock (Backing) return _data[index];
            }
            set
            {
                lock (Backing)
                {
                    Backing.ThrowIfDisposed();
                    Backing.CheckWrite(this);
                    _data[index] = value;
                    _dirtyFrom = Math.Min(_dirtyFrom, index);
                    _dirtyTo = Math.Max(_dirtyTo, index + 1);
                }
            }
        }

        public void ResizeCPUBuffer(int capacity)
        {
            lock (Backing)
            {
                Backing.ThrowIfDisposed();
                if (capacity == Capacity) return;
                Backing.CheckWrite(this);
                Array.Resize(ref _data, capacity);
                Capacity = capacity;
                _replace = true;
            }
        }

        public override void SetData(ReadOnlySpan<T> data)
        {
            lock (Backing)
            {
                Backing.ThrowIfDisposed();
                Backing.CheckWrite(this);
                ResizeCPUBuffer(data.Length);
                data.CopyTo(_data);
                _replace = true;
            }
        }

        internal override unsafe ReadOnlySpan<byte> GetUpload(out int byteOffset, out int byteCapacity)
        {
            byteOffset = 0;
            byteCapacity = -1;
            if (_replace)
            {
                byteCapacity = _data.Length * sizeof(T);
                return MemoryMarshal.AsBytes(_data.AsSpan());
            }

            if (_dirtyFrom >= _dirtyTo) return [];
            byteOffset = _dirtyFrom * sizeof(T);
            return MemoryMarshal.AsBytes(_data.AsSpan(_dirtyFrom, _dirtyTo - _dirtyFrom));
        }

        internal override void Uploaded()
        {
            _replace = false;
            _dirtyFrom = int.MaxValue;
            _dirtyTo = 0;
        }

        private protected override void ReleaseData() => _data = [];
    }
}
