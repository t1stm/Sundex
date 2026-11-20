using OpenTK.Graphics.OpenGL;
using Sundex.Engine.Renderer.Debug;

namespace Sundex.Engine.Renderer.OpenGL;

internal sealed class GLGpuTimer(GLCommandList commands, GLResource[] queries) : GpuTimer
{
    private readonly bool[] _pending = new bool[4];
    private int _next;
    private bool _running;
    private bool _disposed;

    public override void Begin(ICommandList commandList)
    {
        Validate(commandList);
        if (_running) throw new InvalidOperationException("The timer is already running.");
        Collect();
        if (_pending[_next]) return;

        GL.BeginQuery(QueryTarget.TimeElapsed, queries[_next].Handle);
        _running = true;
    }

    public override void End(ICommandList commandList)
    {
        Validate(commandList);
        if (!_running) return;

        GL.EndQuery(QueryTarget.TimeElapsed);
        _pending[_next] = true;
        _next = (_next + 1) % _pending.Length;
        _running = false;
    }

    public override void Collect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (var index = 0; index < _pending.Length; index++)
        {
            if (!_pending[index]) continue;
            GL.GetQueryObjecti(queries[index].Handle, QueryObjectParameterName.QueryResultAvailable, out var available);
            if (available == 0) continue;

            GL.GetQueryObjecti64(queries[index].Handle, QueryObjectParameterName.QueryResult, out var nanoseconds);
            Samples.Add(nanoseconds / 1_000_000.0);
            _pending[index] = false;
        }
    }

    public override void Dispose()
    {
        if (_disposed) return;
        if (_running) End(commands);
        _disposed = true;
        foreach (var query in queries) query.Dispose();
    }

    private void Validate(ICommandList commandList)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(commandList, commands))
            throw new ArgumentException("The command list belongs to a different graphics device.", nameof(commandList));
    }
}
