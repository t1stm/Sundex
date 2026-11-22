using OpenTK.Graphics.OpenGL;
using Sundex.Engine.AssetManagement.Types.Shader;
using Sundex.Engine.Renderer.Shaders;

namespace Sundex.Engine.Renderer.OpenGL;

/// <summary>A native program for OpenGL code running inside GLGraphicsDevice.RunNative.</summary>
public sealed class GLProgram(params ShaderSource[] sources) : IDisposable
{
    private int _handle;
    private bool _disposed;

    public void Use()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == 0) _handle = Link(sources);
        GL.UseProgram(_handle);
    }

    public void SetUniform(string name, int value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == 0) throw new InvalidOperationException("Call Use before setting uniforms.");
        var location = GL.GetUniformLocation(_handle, name);
        if (location == -1) throw new ArgumentException($"The program has no active uniform named {name}.", nameof(name));
        GL.Uniform1i(location, value);
    }

    public void Dispose()
    {
        if (_handle != 0) GL.DeleteProgram(_handle);
        _handle = 0;
        _disposed = true;
    }

    internal static int Link(ReadOnlySpan<ShaderSource> sources, bool webGL = false)
    {
        var shaders = new int[sources.Length];
        var program = GL.CreateProgram();
        try
        {
            for (var index = 0; index < sources.Length; index++)
            {
                var source = sources[index];
                var shader = shaders[index] = GL.CreateShader(source.Type switch
                {
                    ShaderStage.Vertex => ShaderType.VertexShader,
                    ShaderStage.Fragment => ShaderType.FragmentShader,
                    ShaderStage.Compute => ShaderType.ComputeShader,
                    _ => throw new ArgumentOutOfRangeException(nameof(sources))
                });
                var code = source.SourceCode;
                const string desktopHeader = "#version 330 core";
                if (webGL && code.StartsWith(desktopHeader, StringComparison.Ordinal))
                    code = "#version 300 es\nprecision highp float;\nprecision highp int;\nprecision highp sampler2D;" + code[desktopHeader.Length..];
                GL.ShaderSource(shader, code);
                GL.CompileShader(shader);
                GL.GetShaderi(shader, ShaderParameterName.CompileStatus, out var compiled);
                if (compiled == 0)
                {
                    GL.GetShaderInfoLog(shader, out var error);
                    throw new InvalidOperationException($"Failed to compile {source.Info.AssetInfo.Location}: {error}");
                }
                GL.AttachShader(program, shader);
            }

            GL.LinkProgram(program);
            GL.GetProgrami(program, ProgramProperty.LinkStatus, out var linked);
            if (linked == 0)
            {
                GL.GetProgramInfoLog(program, out var error);
                throw new InvalidOperationException($"Failed to link shader program: {error}");
            }
            foreach (var shader in shaders) GL.DetachShader(program, shader);
            return program;
        }
        catch
        {
            GL.DeleteProgram(program);
            throw;
        }
        finally
        {
            // Emscripten invalidates the GL name as soon as DeleteShader is called.
            foreach (var shader in shaders)
                if (shader != 0) GL.DeleteShader(shader);
        }
    }
}
