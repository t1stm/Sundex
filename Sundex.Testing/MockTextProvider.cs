using Sundex.Engine.Renderer;
using Sundex.Engine.Text;
using Sundex.Engine.Text.Fonts;

namespace Sundex.Testing;

public class MockTextProvider(IGlyphProvider? glyphProvider = null) : ITextProvider
{
    public IGlyphProvider GlyphProvider { get; } = glyphProvider ?? new MockGlyphProvider();

    public void Bind(ICommandList commands)
    {
    }
}
