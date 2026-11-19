# Sundex

Sundex is a .NET 10 game and UI framework built on OpenTK. Reference `Sundex.Core` for a
game with planes, text and a scene loop. The Core package brings Engine, Slug and Reactive.

`Sundex.Style.DSL`, `Sundex.Components` and `Sundex.Markup` add styling, UI controls and
compiled markup. Reference `Sundex.Markup.Legacy` and call
`Sundex.Markup.Legacy.CSharp.Register()` at startup to run C# logic in
Sundex 1.0 markup. `Sundex.Testing` provides a headless UI context and mock font providers.

Build and test with `dotnet build` and `dotnet test`. After packing into `artifacts/`, run
`build/smoke-core.sh <version>` on Linux with Xvfb and Mesa. The sample restores its Sundex
packages from that directory, draws 120 frames and checks the rendered pixels.
