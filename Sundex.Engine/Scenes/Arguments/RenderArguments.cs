using Sundex.Engine.Renderer;
namespace Sundex.Engine.Scenes.Arguments;

/// <summary>
///     Arguments passed to the render call.
/// </summary>
/// <remarks>
///     Can be allocated only on the stack.
/// </remarks>
public ref struct RenderArguments
{
    public ICommandList Commands;

    /// <summary>
    ///     Time elapsed since render call.
    /// </summary>
    public double Delta;
}