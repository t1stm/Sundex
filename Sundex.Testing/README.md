# Sundex.Testing

Helpers for testing Sundex UI without a window or GPU.

- `TestUIContext` is a UI context with mock fonts. Run tests that use it sequentially.
- `MockFontProvider`, `MockGlyphProvider` and `MockTextProvider` stand in for real fonts. Every character is 0.6 em wide.
- `RecordingCommandList` records draw commands instead of running them.
