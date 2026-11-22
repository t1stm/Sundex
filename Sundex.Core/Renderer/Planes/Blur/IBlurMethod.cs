using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Textures;

namespace Sundex.Core.Renderer.Planes.Blur;

/// <summary>A way to blur a texture, such as the copy of the frame behind a <see cref="BoxPlane" />.</summary>
internal interface IBlurMethod
{
    /// <summary>The method a <see cref="BlurMethod" /> names.</summary>
    static IBlurMethod For(BlurMethod method) => method switch
    {
        BlurMethod.DualKawase => DualKawaseBlur.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null)
    };

    /// <summary>How far past a pixel the blur reads. Copy that many pixels more around the area to blur.</summary>
    int Margin(BackgroundBlur blur);

    /// <summary>
    ///     Blurs <paramref name="source" /> into <paramref name="target" />, a different render target of any
    ///     size. Draws passes of its own, so the caller sets its pipeline, buffers and textures afterwards.
    /// </summary>
    void Blur(ICommandList commands, GPUTexture source, GPUTexture target, BackgroundBlur blur);
}
