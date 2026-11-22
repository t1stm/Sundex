# Flying Moai

A one-button game built on Sundex. Clap to rise and slip between the pillars. Space, Up, W, Enter or a click claps.

[FlyingMoai.Desktop](../FlyingMoai.Desktop/README.md) runs it in on a desktop
[FlyingMoai.WASM](../FlyingMoai.WASM/README.md) runs it in the browser

## Contents

- `MoaiGame.cs` is the game's main scene.
- `World.cs` builds the island and other params.
- `GameState.cs` holds the game's state.
- `Markup/` holds the UI's Sundex markup.
- `Assets/Textures/` holds the embedded images. `[assembly: SundexAssets]` in `AssemblyInfo.cs` registers them with Sundex.