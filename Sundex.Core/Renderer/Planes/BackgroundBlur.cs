namespace Sundex.Core.Renderer.Planes;

/// <summary>How a <see cref="BoxPlane" /> blurs what's behind it.</summary>
public enum BlurMethod
{
    /// <summary>
    ///     picom's dual Kawase blur: the copy is halved a few times and doubled back, sampling a few
    ///     texels around each pixel on every pass.
    /// </summary>
    DualKawase
}

/// <summary>A blur of what's behind a box (CSS <c>backdrop-filter: blur</c>).</summary>
/// <param name="Strength">
///     picom's strength level, from 1 to 20; 0 for none. Like picom, it blurs physical pixels, so a
///     level reaches less of the UI on a scaled display.
/// </param>
/// <param name="Method">How to blur.</param>
public readonly record struct BackgroundBlur(int Strength, BlurMethod Method = BlurMethod.DualKawase);
