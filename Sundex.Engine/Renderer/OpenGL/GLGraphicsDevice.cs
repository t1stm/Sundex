using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using Serilog;
using Sundex.Reactive;

namespace Sundex.Engine.Renderer.OpenGL;

public sealed class GLGraphicsDevice : IGraphicsDevice
{
    private readonly GameWindow _window;
    private readonly ILogger _logger;
    private readonly GLCommandList _commands;
    private readonly GLDebugProc _debugCallback;
    private readonly RenderMarker.MarkerSink _markerSink;
    private readonly HashSet<GLResource> _resources = [];
    private readonly ConcurrentQueue<GLResource> _retired = new();
    private volatile bool _disposed;

    internal GLGraphicsDevice(GameWindow window, ILogger logger)
    {
        _window = window;
        _logger = logger.ForContext("SourceContext", "OpenGL");
        if (SundexDevelopment.Enabled) Hazards = new FrameHazards(_logger);
        _commands = new GLCommandList(this, _logger);
        _debugCallback = DebugCallback;
        _markerSink = InsertMarker;
    }

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;
    public DeviceCapabilities Capabilities { get; private set; } = new();
    internal bool IsDisposed => _disposed;
    internal FrameHazards? Hazards { get; }
    internal bool CopiedFromTargetLastFrame => _commands.CopiedLastFrame;
    internal int Width { get; private set; }
    internal int Height { get; private set; }

    internal void Initialize()
    {
        var extensions = new HashSet<string>(StringComparer.Ordinal);
        var extensionCount = GL.GetInteger(GetPName.NumExtensions);
        for (uint index = 0; index < extensionCount; index++)
            if (GL.GetStringi(StringName.Extensions, index) is { } name) extensions.Add(name);

        var major = GL.GetInteger(GetPName.MajorVersion);
        var minor = GL.GetInteger(GetPName.MinorVersion);
        var version = major * 10 + minor;
        Capabilities = new DeviceCapabilities
        {
            Vendor = GL.GetString(StringName.Vendor) ?? "",
            Renderer = GL.GetString(StringName.Renderer) ?? "",
            Version = GL.GetString(StringName.Version) ?? "",
            MaxTexture2DSize = GL.GetInteger(GetPName.MaxTextureSize),
            MaxTexture2DLayers = GL.GetInteger(GetPName.MaxArrayTextureLayers),
            SupportsCompute = version >= 43,
            SupportsDebug = version >= 43 || extensions.Contains("GL_KHR_debug"),
            SupportsDirectStateAccess = version >= 45 || extensions.Contains("GL_ARB_direct_state_access")
        };

        _logger.Information("{@Capabilities}, extensions {@Extensions}", Capabilities, extensions);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Enable(EnableCap.Multisample);

        if (!Capabilities.SupportsDebug) return;
        GL.Enable(EnableCap.DebugOutput);
        GL.Enable(EnableCap.DebugOutputSynchronous);
        GL.DebugMessageCallback(_debugCallback, in nint.Zero);
        RenderMarker.Sink = _markerSink;
    }

    public ICommandList BeginFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _commands.BeginFrame();
        Hazards?.BeginFrame();
        while (_retired.TryDequeue(out var resource)) Destroy(resource);
        return _commands;
    }

    public void EndFrame()
    {
        _commands.EndFrame();
        Hazards?.EndFrame();
        _window.Context.SwapBuffers();
    }

    public void Resize(int framebufferWidth, int framebufferHeight)
    {
        Width = framebufferWidth;
        Height = framebufferHeight;
    }

    public unsafe void ReadPixels(Box2i rectangle, Span<byte> rgba8)
    {
        _commands.ValidateReadback();
        var (width, height) = rectangle.Size;
        var target = _commands.ReadSize;
        if (rectangle.Min.X < 0 || rectangle.Min.Y < 0 || rectangle.Max.X > target.X || rectangle.Max.Y > target.Y)
            throw new ArgumentOutOfRangeException(nameof(rectangle));
        var rowBytes = checked(width * 4);
        if (rgba8.Length < checked(rowBytes * height))
            throw new ArgumentException("The destination is too small.", nameof(rgba8));

        GL.PixelStorei(PixelStoreParameter.PackAlignment, 4);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _commands.ReadFramebuffer);
        fixed (byte* pixels = rgba8)
            GL.ReadPixels(rectangle.Min.X, target.Y - rectangle.Max.Y, width, height,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        if (height < 2 || rowBytes == 0) return;
        var scratch = ArrayPool<byte>.Shared.Rent(rowBytes);
        try
        {
            var row = scratch.AsSpan(0, rowBytes);
            for (var top = 0; top < height / 2; top++)
            {
                var topRow = rgba8.Slice(top * rowBytes, rowBytes);
                var bottomRow = rgba8.Slice((height - 1 - top) * rowBytes, rowBytes);
                topRow.CopyTo(row);
                bottomRow.CopyTo(topRow);
                row.CopyTo(bottomRow);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    public GpuTimer CreateGpuTimer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var queries = new GLResource[4];
        for (var index = 0; index < queries.Length; index++)
            queries[index] = Track(new GLResource(this, GL.GenQuery(), GL.DeleteQuery));
        return new GLGpuTimer(_commands, queries);
    }

    /// <summary>Runs native code on the context thread. Reapply custom resource bindings before drawing again.</summary>
    public void RunNative(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            action();
        }
        finally
        {
            _commands.InvalidateNativeState();
        }
    }

    internal T Track<T>(T resource) where T : GLResource
    {
        _resources.Add(resource);
        return resource;
    }

    internal void Retire(GLResource resource)
    {
        if (!_disposed) _retired.Enqueue(resource);
    }

    internal void Destroy(GLResource resource)
    {
        if (_resources.Remove(resource)) resource.Destroy();
    }

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _commands.InvalidateBindings();
        while (_resources.Count > 0) Destroy(_resources.First());
        _retired.Clear();
        if (RenderMarker.Sink == _markerSink) RenderMarker.Sink = null;
        if (Capabilities.SupportsDebug) GL.DebugMessageCallback(nint.Zero, null);
    }

    private static unsafe void InsertMarker(ReadOnlySpan<char> message, MarkerType type)
    {
        Span<byte> bytes = stackalloc byte[Encoding.UTF8.GetMaxByteCount(message.Length) + 1];
        var length = Encoding.UTF8.GetBytes(message, bytes);
        bytes[length] = 0;
        fixed (byte* pointer = bytes)
            GL.DebugMessageInsert(DebugSource.DebugSourceApplication, DebugType.DebugTypeMarker,
                (uint)type, DebugSeverity.DebugSeverityNotification, length, pointer);
    }

    private unsafe void DebugCallback(DebugSource source, DebugType type, uint id, DebugSeverity severity,
        int length, nint message, nint userParameter)
    {
        if (type is DebugType.DebugTypeOther || type is DebugType.DebugTypeMarker && id == (uint)MarkerType.Hidden)
            return;

        var text = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(message.ToPointer(), length));
        if (type == DebugType.DebugTypeError)
            _logger.Warning("{Source}, ({Type}, {Id}) {Severity}: {Message}", source, type, id, severity, text);
        else
            _logger.Debug("{Source}, ({Type}, {Id}) {Severity}: {Message}", source, type, id, severity, text);
    }
}

internal class GLResource(GLGraphicsDevice device, int handle, Action<int> delete) : IDisposable
{
    private HashSet<GLVertexArray>? _arrays;
    private int _retired;
    public int Handle { get; private set; } = handle;

    public void AddArray(GLVertexArray array) => (_arrays ??= []).Add(array);
    public void RemoveArray(GLVertexArray array) => _arrays?.Remove(array);

    public void ClearArrays()
    {
        while (_arrays is { Count: > 0 }) device.Destroy(_arrays.First());
    }

    public virtual void Destroy()
    {
        ClearArrays();
        delete(Handle);
        Handle = 0;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _retired, 1) == 0) device.Retire(this);
    }
}
