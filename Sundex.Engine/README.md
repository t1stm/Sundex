# Sundex.Engine

The base of Sundex: the window and scene loop, a backend-neutral renderer, Slug text, assets and audio. 
Every other Sundex package except Slug, Reactive and the markup generator builds on it.

```sh
dotnet add package Sundex.Engine --prerelease
```

The build compiles the native audio library on Linux and macOS. See [BUILDING.md](../BUILDING.md) for Windows.

## Game and scenes

- `Game` opens an OpenTK window and runs the scene loop, input, the frame tick and the clipboard. Alt+Enter, or Ctrl+Cmd+F on macOS, toggles fullscreen.
- `Scenes/` holds `Scene`, the base class an app's screens derive from, and `SceneManager`, which loads them and switches between them.
- `Threading/ThreadRunner` runs work on threads and tasks, and rethrows their exceptions on the main thread at `Update`.

## Rendering

- `Renderer/` is the backend-neutral API: `IGraphicsDevice` starts and ends frames, and `ICommandList` records passes, pipelines and draws.
- `Renderer/OpenGL/` implements it on OpenGL 3.3 and WebGL 2 (`GLGraphicsDevice.CreateWebGL2`). Only this folder may use `OpenTK.Graphics.OpenGL`. CI fails the build otherwise.
- `Renderer/Cameras/`, `Renderer/Textures/` and `Renderer/Shaders/` hold cameras, textures with atlases, and shader loading.

## Text

- `Text/` lays out and draws text with [Slug](../Sundex.Slug/README.md).
- `Text/Fonts/FontProvider` bundles Lato Regular, Lato Bold and Lucide icons. `AddFont` adds an app's font, and `Alias` replaces the default.

## Assets

- `AssetManagement/AssetProvider` loads embedded resources from every assembly marked `[assembly: SundexAssets]`, and preloads graphics for `IGamePreloadable` types.
- Debug builds load assets from the project directory first, and reload stylesheets automatically when saved.
- `buildTransitive/Sundex.Engine.props` stamps the asset and source roots into an app's assembly. Project references must import it themselves.

## Audio

- `Audio/AudioDevice` opens an output through miniaudio. With no output, it returns a silent device that keeps time.
- `AudioBuffer` holds samples that any number of `AudioVoice`s play. `AudioStream` plays frames the app feeds it.
- `Audio/Native/` holds miniaudio and the `sundex_audio` shim, built by `build/audio-native.sh`.
