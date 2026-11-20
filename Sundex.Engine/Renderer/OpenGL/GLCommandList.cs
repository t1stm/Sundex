using System.Runtime.CompilerServices;
using System.Text;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Serilog;
using Sundex.Engine.Renderer.Buffers;
using Sundex.Engine.Renderer.Shaders;
using Sundex.Engine.Renderer.Textures;
using GLBufferUsage = OpenTK.Graphics.OpenGL.BufferUsage;
using BufferUsage = Sundex.Engine.Renderer.Buffers.BufferUsage;

namespace Sundex.Engine.Renderer.OpenGL;

internal sealed class GLCommandList(GLGraphicsDevice device, ILogger logger) : ICommandList
{
    private readonly ConditionalWeakTable<Pipeline, VertexArrayCache> _arrays = new();
    private readonly Dictionary<int, GLResource> _uniforms = [];
    private readonly Dictionary<int, GLResource> _boundUniforms = [];
    private readonly GLResource?[] _samplers = new GLResource[3];
    private readonly Dictionary<int, int> _boundSamplers = [];
    private readonly List<string> _labels = [];
    private GLResource?[] _vertices = [];
    private GLResource? _indices;
    private GLResource? _program;
    private Pipeline? _pipeline;
    private VertexArrayCache? _vertexArrays;
    private Matrix4 _view;
    private bool _hasView;
    private bool _viewBound;
    private bool _frame;
    private bool _pass;
    private int _boundProgram;
    private int _boundUniformBuffer;
    private int _boundArray;
    private int _activeTexture = -1;
    private BlendMode? _blend;
    private Box2i? _scissor;
    private GPUTexture? _target;
    private int _framebuffer;
    private bool _copied;

    public Vector2i TargetSize { get; private set; }

    /// <summary>The framebuffer and size of the last pass, which ReadPixels reads.</summary>
    internal int ReadFramebuffer { get; private set; }
    internal Vector2i ReadSize { get; private set; }
    internal bool CopiedLastFrame { get; private set; }

    public void BeginFrame()
    {
        if (_frame) throw new InvalidOperationException("A frame is already open.");
        _frame = true;
        CopiedLastFrame = _copied;
        _copied = false;
        InvalidateBindings();
    }

    public void EndFrame()
    {
        ValidateReadback();
        _frame = false;
        InvalidateBindings();
    }

    public void BeginPass(in PassDescription pass)
    {
        ValidateReadback();
        if (pass.Target is { } target && !target.Usage.HasFlag(TextureUsage.RenderTarget))
            throw new ArgumentException("The pass target needs TextureUsage.RenderTarget.", nameof(pass));
        _target = pass.Target;
        BindTarget();
        _pass = true;
        _pipeline = null;
        _program = null;
        _indices = null;
        _vertexArrays = null;
        if (pass.ClearColor is { } color)
        {
            GL.ClearColor(color.X, color.Y, color.Z, color.W);
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }
    }

    private void BindTarget()
    {
        _framebuffer = 0;
        TargetSize = new Vector2i(device.Width, device.Height);
        if (_target is { } target)
        {
            ActivateTexture(0);
            lock (target.Backing) _framebuffer = GetTexture(target).GetFramebuffer(target);
            TargetSize = new Vector2i(target.Width, target.Height);
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        GL.Viewport(0, 0, TargetSize.X, TargetSize.Y);
    }

    public void EndPass()
    {
        RequirePass();
        SetScissor(null);
        _pass = false;
        ReadFramebuffer = _framebuffer;
        ReadSize = TargetSize;
    }

    public void SetPipeline(Pipeline pipeline)
    {
        RequirePass();
        var shader = pipeline.Shader;
        lock (shader.Backing)
        {
            var program = shader.Backing.Get<GLShader>(device);
            if (program is null || program.Version != shader.Version)
            {
                try
                {
                    var replacement = Link(shader);
                    if (program is not null) device.Destroy(program);
                    program = shader.Backing.Set(device, replacement);
                }
                catch (InvalidOperationException exception) when (program is not null)
                {
                    logger.Error(exception, "Shader reload failed. Keeping the previous program.");
                    program.Version = shader.Version;
                }
            }

            _program = program;
        }

        if (_pipeline != pipeline)
        {
            _pipeline = pipeline;
            _vertexArrays = _arrays.GetValue(pipeline, static _ => new VertexArrayCache());
            if (_vertices.Length < pipeline.VertexBuffers.Length)
                Array.Resize(ref _vertices, pipeline.VertexBuffers.Length);
            Array.Clear(_vertices);
            _indices = null;
        }

        ApplyPipeline();
    }

    private GLShader Link(Shader shader)
    {
        var handle = GLProgram.Link(shader.Sources);
        GL.UseProgram(handle);
        _boundProgram = handle;

        foreach (var entry in shader.Layout.Bindings)
        {
            if (entry.Type == ShaderBindingType.UniformBuffer)
            {
                var index = GL.GetUniformBlockIndex(handle, entry.Name);
                if (index != uint.MaxValue)
                {
                    GL.UniformBlockBinding(handle, index, (uint)entry.Binding);
                    continue;
                }
            }
            else
            {
                var index = GL.GetUniformLocation(handle, entry.Name);
                if (index != -1)
                {
                    GL.Uniform1i(index, entry.Binding);
                    continue;
                }
            }
            logger.Debug("Shader {Shader} has no active binding named {Name}.",
                shader.Sources[0].Info.AssetInfo.Location, entry.Name);
        }

        return device.Track(new GLShader(device, handle, shader.Version));
    }

    private void ApplyPipeline()
    {
        var handle = _program!.Handle;
        if (_boundProgram != handle)
        {
            GL.UseProgram(handle);
            _boundProgram = handle;
        }

        if (_blend == _pipeline!.Blend) return;
        _blend = _pipeline.Blend;
        if (_blend == BlendMode.Alpha) GL.Enable(EnableCap.Blend);
        else GL.Disable(EnableCap.Blend);
    }

    public void SetView(in Matrix4 viewProjection)
    {
        RequirePass();
        if (!_hasView || _view != viewProjection)
        {
            _view = viewProjection;
            _hasView = true;
            UploadUniforms(0, System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<Matrix4>(in _view)));
            _viewBound = true;
        }
        else
        {
            BindView();
        }
    }

    private void BindView()
    {
        if (!_hasView || _viewBound) return;
        BindUniformBuffer(0, _uniforms[0]);
        _viewBound = true;
    }

    public void PushUniforms(int binding, ReadOnlySpan<byte> data)
    {
        RequirePass();
        ArgumentOutOfRangeException.ThrowIfLessThan(binding, 1);
        UploadUniforms(binding, data);
    }

    private void UploadUniforms(int binding, ReadOnlySpan<byte> data)
    {
        if (!_uniforms.TryGetValue(binding, out var buffer))
            _uniforms.Add(binding, buffer = NewBuffer());

        if (_boundUniformBuffer != buffer.Handle)
        {
            GL.BindBuffer(BufferTarget.UniformBuffer, buffer.Handle);
            _boundUniformBuffer = buffer.Handle;
        }

        GL.BufferData(BufferTarget.UniformBuffer, data.Length, data, GLBufferUsage.StreamDraw);
        BindUniformBuffer(binding, buffer);
    }

    private void BindUniformBuffer(int binding, GLResource buffer)
    {
        // Base bindings cover the entire store even when BufferData replaces it.
        if (_boundUniforms.TryGetValue(binding, out var previous) && previous == buffer) return;
        GL.BindBufferBase(BufferTarget.UniformBuffer, (uint)binding, buffer.Handle);
        _boundUniforms[binding] = buffer;
        _boundUniformBuffer = buffer.Handle;
    }

    public void SetUniformBuffer(int binding, GpuBuffer buffer)
    {
        RequirePass();
        ArgumentOutOfRangeException.ThrowIfLessThan(binding, 2);
        BindUniformBuffer(binding, UseBuffer(buffer, BufferUsage.Uniform));
    }

    public void SetTexture(int binding, GPUTexture texture, Sampler sampler)
    {
        RequirePass();
        ArgumentOutOfRangeException.ThrowIfLessThan(binding, 2);
        ActivateTexture(binding);
        lock (texture.Backing)
        {
            GetTexture(texture).Prepare(texture);
            if (device.Hazards is { } hazards) texture.Backing.MarkRead(hazards, _pipeline, CurrentLabel);
        }

        var native = _samplers[(int)sampler] ??= CreateSampler(sampler);
        if (_boundSamplers.TryGetValue(binding, out var bound) && bound == native.Handle) return;
        GL.BindSampler((uint)binding, native.Handle);
        _boundSamplers[binding] = native.Handle;
    }

    private GLResource CreateSampler(Sampler sampler)
    {
        var native = device.Track(new GLResource(device, GL.GenSampler(), GL.DeleteSampler));
        var min = sampler switch
        {
            Sampler.LinearClamp => TextureMinFilter.Linear,
            Sampler.LinearMipmapClamp => TextureMinFilter.LinearMipmapLinear,
            Sampler.NearestClamp => TextureMinFilter.Nearest,
            _ => throw new ArgumentOutOfRangeException(nameof(sampler))
        };
        GL.SamplerParameteri(native.Handle, SamplerParameterI.TextureMinFilter, (int)min);
        GL.SamplerParameteri(native.Handle, SamplerParameterI.TextureMagFilter,
            (int)(sampler == Sampler.NearestClamp ? TextureMagFilter.Nearest : TextureMagFilter.Linear));
        GL.SamplerParameteri(native.Handle, SamplerParameterI.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.SamplerParameteri(native.Handle, SamplerParameterI.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return native;
    }

    private void ActivateTexture(int binding)
    {
        if (_activeTexture == binding) return;
        GL.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + binding));
        _activeTexture = binding;
    }

    private GLTexture GetTexture(GPUTexture texture) =>
        texture.Backing.Get<GLTexture>(device) ?? texture.Backing.Set(device, device.Track(new GLTexture(device)));

    public void SetVertexBuffer(int slot, GpuBuffer buffer)
    {
        RequirePass();
        if (_pipeline is null) throw new InvalidOperationException("Select a pipeline before binding vertices.");
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, _pipeline.VertexBuffers.Length);
        _vertices[slot] = UseBuffer(buffer, BufferUsage.Vertex);
    }

    public void SetIndexBuffer(GpuBuffer<int> buffer)
    {
        RequirePass();
        _indices = UseBuffer(buffer, BufferUsage.Index);
    }

    private GLResource NewBuffer() => device.Track(new GLResource(device, GL.GenBuffer(), GL.DeleteBuffer));

    private GLResource UseBuffer(GpuBuffer buffer, BufferUsage usage)
    {
        if (buffer.Usage != usage) throw new ArgumentException($"Expected a {usage} buffer.", nameof(buffer));
        lock (buffer.Backing)
        {
            var native = buffer.Backing.Get<GLResource>(device) ?? buffer.Backing.Set(device, NewBuffer());
            if (device.Hazards is { } hazards) buffer.Backing.MarkRead(hazards, _pipeline, CurrentLabel);
            var bytes = buffer.GetUpload(out var offset, out var capacity);
            if (capacity < 0 && bytes.IsEmpty) return native;

            // Uploading via COPY_WRITE_BUFFER leaves the active VAO's index binding alone.
            GL.BindBuffer(BufferTarget.CopyWriteBuffer, native.Handle);
            if (capacity >= 0) GL.BufferData(BufferTarget.CopyWriteBuffer, capacity, bytes, GLBufferUsage.StreamDraw);
            else GL.BufferSubData(BufferTarget.CopyWriteBuffer, offset, bytes.Length, bytes);
            buffer.Uploaded();
            return native;
        }
    }

    public void SetScissor(Box2i? physicalPixels)
    {
        RequirePass();
        if (_scissor == physicalPixels) return;
        var wasEnabled = _scissor.HasValue;
        _scissor = physicalPixels;
        ApplyScissor(wasEnabled);
    }

    private void ApplyScissor(bool wasEnabled)
    {
        if (_scissor is not { } box)
        {
            GL.Disable(EnableCap.ScissorTest);
            return;
        }

        if (!wasEnabled) GL.Enable(EnableCap.ScissorTest);
        GL.Scissor(box.Min.X, TargetSize.Y - box.Max.Y, box.Size.X, box.Size.Y);
    }

    public void DrawIndexed(int indexCount, int instanceCount = 1, int firstIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(indexCount);
        ArgumentOutOfRangeException.ThrowIfNegative(instanceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(firstIndex);
        RequirePass();
        if (indexCount == 0 || instanceCount == 0) return;
        if (_pipeline is not { } pipeline || _indices is not { } indices)
            throw new InvalidOperationException("Select a pipeline and index buffer before drawing.");
        var vertices = _vertices.AsSpan(0, pipeline.VertexBuffers.Length);
        var hash = indices.Handle;
        foreach (var vertex in vertices)
        {
            if (vertex is null) throw new InvalidOperationException("Bind all pipeline vertex buffers before drawing.");
            hash = unchecked(hash * 31 + vertex.Handle);
        }

        ApplyPipeline();
        BindView();
        var array = _vertexArrays!.Get(hash, indices, vertices);
        if (array is null)
        {
            array = device.Track(new GLVertexArray(device, _program!, indices, vertices, pipeline.VertexBuffers,
                _vertexArrays, hash));
            _boundArray = array.Handle;
        }
        else if (_boundArray != array.Handle)
        {
            GL.BindVertexArray(array.Handle);
            _boundArray = array.Handle;
        }

        var topology = pipeline.Topology switch
        {
            PrimitiveTopology.Triangles => PrimitiveType.Triangles,
            _ => throw new ArgumentOutOfRangeException(nameof(pipeline))
        };
        var offset = (IntPtr)((long)firstIndex * sizeof(int));
        if (instanceCount == 1 && !pipeline.HasInstanceAttributes)
            GL.DrawElements(topology, indexCount, DrawElementsType.UnsignedInt, offset);
        else
            GL.DrawElementsInstanced(topology, indexCount, DrawElementsType.UnsignedInt, offset, instanceCount);
    }

    public void CopyFromTarget(Box2i physicalPixels, GPUTexture destination)
    {
        RequirePass();
        var (min, max, size) = (physicalPixels.Min, physicalPixels.Max, physicalPixels.Size);
        if (min.X < 0 || min.Y < 0 || max.X > TargetSize.X || max.Y > TargetSize.Y)
            throw new ArgumentOutOfRangeException(nameof(physicalPixels), "The region must lie inside the target.");
        if (size.X > destination.Width || size.Y > destination.Height)
            throw new ArgumentException("The region must fit in the destination.", nameof(destination));
        if (ReferenceEquals(destination, _target))
            throw new ArgumentException("A pass cannot copy into its own target.", nameof(destination));

        _copied = true;
        ActivateTexture(0);
        lock (destination.Backing)
            GetTexture(destination).CopyFromTarget(destination, min.X, TargetSize.Y - max.Y, size.X, size.Y);
    }

    public void CopyToTarget(GPUTexture source)
    {
        RequirePass();
        if (!source.Usage.HasFlag(TextureUsage.RenderTarget))
            throw new ArgumentException("The source needs TextureUsage.RenderTarget.", nameof(source));
        if (ReferenceEquals(source, _target))
            throw new ArgumentException("A pass cannot copy its own target.", nameof(source));

        ActivateTexture(0);
        int framebuffer;
        lock (source.Backing) framebuffer = GetTexture(source).GetFramebuffer(source);
        var width = Math.Min(source.Width, TargetSize.X);
        var height = Math.Min(source.Height, TargetSize.Y);
        // Blits are scissored, and this copy ignores the scissor.
        if (_scissor.HasValue) GL.Disable(EnableCap.ScissorTest);
        // Creating the source framebuffer may have bound it, so bind both ends.
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _framebuffer);
        GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        if (_scissor.HasValue) GL.Enable(EnableCap.ScissorTest);
    }

    public void GenerateMipmaps(GPUTexture texture)
    {
        RequirePass();
        ActivateTexture(0);
        lock (texture.Backing)
        {
            GetTexture(texture).Prepare(texture);
            GL.GenerateMipmap(TextureTarget.Texture2D);
        }
    }

    private string? CurrentLabel => _labels.Count > 0 ? _labels[^1] : null;

    public unsafe void PushLabel(ReadOnlySpan<char> name)
    {
        if (device.Hazards is not null) _labels.Add(name.ToString());
        if (!device.SupportsDebug) return;
        Span<byte> bytes = stackalloc byte[Encoding.UTF8.GetMaxByteCount(name.Length) + 1];
        var length = Encoding.UTF8.GetBytes(name, bytes);
        bytes[length] = 0;
        fixed (byte* pointer = bytes)
            GL.PushDebugGroup(DebugSource.DebugSourceApplication, 0, length, pointer);
    }

    public void PopLabel()
    {
        if (_labels.Count > 0) _labels.RemoveAt(_labels.Count - 1);
        if (device.SupportsDebug) GL.PopDebugGroup();
    }

    private void RequirePass()
    {
        if (!_pass) throw new InvalidOperationException("Open a render pass before issuing commands.");
    }

    internal void ValidateReadback()
    {
        if (!_frame || _pass) throw new InvalidOperationException("This operation requires a frame outside a render pass.");
    }

    internal void InvalidateBindings()
    {
        _boundProgram = -1;
        _boundUniformBuffer = -1;
        _boundUniforms.Clear();
        _boundArray = -1;
        _activeTexture = -1;
        _blend = null;
        _viewBound = false;
        _boundSamplers.Clear();
    }

    internal void InvalidateNativeState()
    {
        InvalidateBindings();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        GL.Viewport(0, 0, TargetSize.X, TargetSize.Y);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        ApplyScissor(wasEnabled: false);
    }

    private sealed class GLShader(GLGraphicsDevice device, int handle, int version)
        : GLResource(device, handle, GL.DeleteProgram)
    {
        public int Version { get; set; } = version;
    }
}
