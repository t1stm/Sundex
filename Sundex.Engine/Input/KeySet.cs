using System.Runtime.CompilerServices;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sundex.Engine.Input;

/// <summary>A set of keys held by value, so a copy never changes with the set it came from.</summary>
public struct KeySet
{
    // Keys.LastKey is 348, so six 64-bit words cover every key.
    private const int WordCount = 6;
    private Words _words;

    public readonly bool IsEmpty
    {
        get
        {
            for (var index = 0; index < WordCount; index++)
                if (_words[index] != 0) return false;
            return true;
        }
    }

    public readonly bool Contains(Keys key) =>
        InRange(key) && (_words[(int)key >> 6] & Bit(key)) != 0;

    public void Add(Keys key)
    {
        if (InRange(key)) _words[(int)key >> 6] |= Bit(key);
    }

    public void Remove(Keys key)
    {
        if (InRange(key)) _words[(int)key >> 6] &= ~Bit(key);
    }

    public void Clear() => _words = default;

    private static bool InRange(Keys key) => (uint)key < WordCount * 64;
    private static ulong Bit(Keys key) => 1ul << ((int)key & 63);

    [InlineArray(WordCount)]
    private struct Words
    {
        private ulong _word;
    }
}
