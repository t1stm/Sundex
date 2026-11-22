namespace Sundex.Style.DSL;

/// <summary>
///     A stylesheet that doesn't parse, and where: what an editor needs to underline the mistake.
///     <see cref="Exception.Message" /> keeps the source excerpt the runtime logs.
/// </summary>
public sealed class StyleParseException(string reason, int offset, string message) : Exception(message)
{
    /// <summary>What is wrong, without the excerpt.</summary>
    public string Reason { get; } = reason;

    /// <summary>Where, as a character offset into the file that failed.</summary>
    public int Offset { get; } = offset;

    /// <summary>
    ///     The imports from the parsed file down to the one that failed, outermost first. Empty when
    ///     the mistake is in the parsed file itself.
    /// </summary>
    public List<string> Imports { get; } = [];
}
