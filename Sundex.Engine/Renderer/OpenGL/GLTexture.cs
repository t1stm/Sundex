using OpenTK.Graphics.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Sundex.Engine.Renderer.Textures;

namespace Sundex.Engine.Renderer.OpenGL;

internal sealed class GLTexture(GLGraphicsDevice device)
    : GLResource(device, GL.GenTexture(), GL.DeleteTexture), ITextureUploader
{
    public bool Allocated { get; private set; }

    public void Prepare(GPUTexture texture)
    {
        GL.BindTexture(TextureTarget.Texture2D, Handle);
        var blank = !Allocated;
        if (blank)
        {
            GL.TexImage2D(TextureTarget.Texture2D, 0, Format(texture.Format), texture.Width, texture.Height,
                0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            Initialize(texture);
        }

        if ((texture.Upload(this) || blank) && texture.MipmapMode == MipmapMode.Enabled)
            GL.GenerateMipmap(TextureTarget.Texture2D);
    }

    private int _framebuffer;

    /// <summary>Copies from the bound framebuffer into level 0, starting at texel 0, 0.</summary>
    public void CopyFromTarget(GPUTexture texture, int x, int y, int width, int height)
    {
        Prepare(texture);
        GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, x, y, width, height);
    }

    public int GetFramebuffer(GPUTexture texture)
    {
        Prepare(texture);
        if (_framebuffer != 0) return _framebuffer;

        _framebuffer = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, Handle, 0);
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferStatus.FramebufferComplete)
            throw new InvalidOperationException($"The render target framebuffer is incomplete: {status}.");
        return _framebuffer;
    }

    public override void Destroy()
    {
        if (_framebuffer != 0) GL.DeleteFramebuffer(_framebuffer);
        _framebuffer = 0;
        base.Destroy();
    }

    private void Initialize(GPUTexture texture)
    {
        GL.TexParameteri(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel,
            texture.MipmapMode == MipmapMode.Enabled ? (int)Math.Floor(Math.Log2(Math.Max(texture.Width, texture.Height))) : 0);
        Allocated = true;
    }

    public unsafe void Upload<TPixel>(ImageFrame<TPixel> frame, Rectangle region)
        where TPixel : unmanaged, IPixel, IPixel<TPixel>
    {
        if (!frame.DangerousTryGetSinglePixelMemory(out var pixels))
        {
            var copy = new TPixel[frame.Width * frame.Height];
            frame.CopyPixelDataTo(copy);
            pixels = copy;
        }

        using var handle = pixels.Pin();
        var info = UploadInfoProvider<TPixel>.UploadInfo;
        GL.PixelStorei(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, region.X, region.Y, region.Width, region.Height,
            info.Format, info.Type, handle.Pointer);
    }

    private static InternalFormat Format(TextureFormat format) => format switch
    {
        TextureFormat.Rgba8Unorm => InternalFormat.Rgba8,
        TextureFormat.Rgba32Float => InternalFormat.Rgba32f,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
