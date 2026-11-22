# Sundex.Slug

Builds the curve and band textures that the [Slug](https://github.com/EricLengyel/Slug) algorithm draws text from. Outlines come from SixLabors.Fonts. It has no native dependency and doesn't touch GL.

```csharp
var font = SlugFont.Load(File.OpenRead("Lato-Regular.ttf"));
var data = font.SlugData;
var glyph = data.GetGlyph("A");
```

All coordinates are in em space: one unit per em, Y up, origin on the baseline.

- `SlugFont` loads a font and builds its `SlugData` on first access.
- `SlugFontData` and `SlugGlyph` hold the textures and the per-glyph record.
- `Fonts/` reads metrics and outlines from SixLabors.Fonts.
- `Geometry/` holds the vectors, contours and bounds maths.
