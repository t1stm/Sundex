# Building Sundex apps

Sundex apps publish for the browser from a dedicated .NET WebAssembly executable project. The browser and desktop entry points can share the app's logic, rendering, assets, and generated markup. The browser entry point owns the canvas, animation loop, and browser services. It follows the same pattern as [Flying Moai](samples/FlyingMoai.WASM/README.md), the working browser sample.

## Build Sundex and run the browser sample

From the Sundex repository root, use the .NET 10 SDK. The solution lists both sample hosts under Samples. Default solution builds cover the desktop projects and tests. Publish the browser sample directly with `wasm-tools` installed.

```sh
dotnet build Sundex.slnx
dotnet test --solution Sundex.slnx

dotnet workload install wasm-tools
dotnet publish samples/FlyingMoai.WASM -c Release
python -m http.server 8766 --bind 127.0.0.1 --directory samples/FlyingMoai.WASM/bin/Release/net10.0/publish/wwwroot
```

Run the desktop sample with `dotnet run --project samples/FlyingMoai.Desktop`. Both this project and `samples/FlyingMoai.WASM` reference `samples/FlyingMoai`, the shared game library that owns the assets and generated markup. Each host provides its own best-score storage.

Open `http://127.0.0.1:8766/?verify` in Zen or Chromium. Start with Space, let the Moai crash, and click **Clap again**. The sample reports WebGL errors with `?verify` and records startup and frame submission timings with `?benchmark`.

## Add a browser build to an existing app

1. Put the app code that both hosts will use in a managed project. Have each executable reference it. Keep the OpenTK window, desktop input, and native services in the desktop host. In the browser host, pass canvas size, frame timing, and input to the shared code. Flying Moai shares its game behind those entry points. [Shared game](samples/FlyingMoai/MoaiGame.cs) · [Desktop host](samples/FlyingMoai.Desktop/MoaiScene.cs) · [Browser host](samples/FlyingMoai.WASM/Program.cs)
2. Create an executable project with `Sdk="Microsoft.NET.Sdk.WebAssembly"`, `TargetFramework=net10.0`, and `RuntimeIdentifier=browser-wasm`. Use a Sundex checkout or a matching set of packages built from a revision with the WebGL 2 changes. Use the sample's `BrowserContext.cs`, `browser.c`, and project settings as the starting point for your browser host. [Browser project](samples/FlyingMoai.WASM/FlyingMoai.WASM.csproj) · [Browser helper](samples/FlyingMoai.WASM/BrowserContext.cs)
3. Add `wwwroot/index.html` with a `#sundex` canvas and a JS module that loads `_framework/dotnet.js`. Set `CopyToOutputDirectory="PreserveNewest"` on the `wwwroot` content to make the regular build output runnable from a static server. Create the WebGL 2 context and load OpenTK through `BrowserContext.Open()`, then call `GLGraphicsDevice.CreateWebGL2`. Call `Frame.Install()` and `AssetProvider.PreloadGraphics()` before initializing the app. Use `requestAnimationFrame` to run `Frame.Pump()`, update the app, and render. Feed the device physical canvas dimensions and use CSS dimensions for UI and pointer coordinates. [Sample loader](samples/FlyingMoai.WASM/wwwroot/main.js) · [Context creation](samples/FlyingMoai.WASM/browser.c)
4. Keep shaders, fonts, and bundled images as embedded resources. Keep the Markup 2.0 generator in the project that owns the `.snx.xml` files. Start with `PublishTrimmed=false` because Sundex discovers graphics preload methods through reflection. Check custom shaders and rendering calls against WebGL 2, and verify a published Release build before enabling trimming or AOT. [Markup build integration](Sundex.Markup.Generator/buildTransitive/Sundex.Markup.Generator.props) · [WebGL 2 specification](https://registry.khronos.org/webgl/specs/latest/2.0/)

The browser project enables `WasmBuildNative` to relink the Mono WASM runtime with Emscripten, the WebGL 2 library, and the tiny C context initializer. The app's C# and generated markup build with the regular .NET compiler. OpenTK's function pointers use Mono interpreter trampolines declared in `BrowserContext.cs`. An app that calls additional native GL functions may need additional signatures. [Browser context and signatures](samples/FlyingMoai.WASM/BrowserContext.cs) · [.NET native relinking](https://github.com/dotnet/runtime/blob/v10.0.0/src/mono/wasm/features.md#c-code-or-native-linked-libraries)

### Publish and host

Run `dotnet publish path/to/MyApp.Browser.csproj -c Release -o artifacts/browser`. Serve `artifacts/browser/wwwroot` with its `_framework` directory intact. The published directory includes the HTML entry point, JavaScript loader, .NET runtime, and managed assemblies. The web server serves static files and the app runs in the browser.

Serve `.wasm` as `application/wasm`. With NGINX, `gzip_static on` can serve the precompressed `.gz` assets from publish. Use HTTPS for deployment and test a cold load at the intended URL prefix. The sample uses a single-threaded WASM runtime, so it does not need cross-origin isolation headers for WASM threads.

## Thirty Dollar Visualizer

The Visualizer project lives at `Visualizer/ThirtyDollarVisualizer/ThirtyDollarVisualizer.csproj` in the [ThirtyDollarTools repository](https://github.com/t1stm/ThirtyDollarTools). Its existing executable starts Sundex's desktop `Game`, configures an OpenTK window, and loads the desktop scenes through that window. `dotnet publish` with `-r browser-wasm` alone cannot port that entry point. ThirtyDollarTools needs a browser executable, for example `Visualizer/ThirtyDollarVisualizer.Browser/ThirtyDollarVisualizer.Browser.csproj`, plus browser implementations of the platform services it uses.

| Area | Work for the browser target |
| --- | --- |
| Startup and drawing | Extract the Visualizer scene logic that depends on `Game` and `ThirtyDollarWorkflow` so a canvas host can drive it. Validate the custom playfield shaders and instance rendering on WebGL 2. For a first smoke test, play a bundled sequence silently and verify playfield movement and seek. |
| Audio and scheduling | The desktop workflow uses BASS or OpenAL and feeds audio from a managed thread in `VoicePlayback`. Port playback to Web Audio and adapt the feeder to the browser's scheduling model. Use playback time from the audio backend to keep the playfield in sync. The optional OpenGL 4.3 mixer uses compute shaders, which WebGL 2 does not support. Start with a CPU mixing path and measure dense sequences before choosing a separate browser compute backend. [Web Audio API](https://webaudio.github.io/web-audio-api/) |
| Files and settings | Load bundled or downloaded samples asynchronously. Use a browser file picker or drop handler for sequence import, browser storage for settings, and browser downloads for exports. Audit explicit thread creation, including the playfield chunk fill, for the initial single-threaded target. |
| Editor and other scenes | Port each scene's window and input dependencies as it comes online. The editor still has Markup 1.0 documents, so move any runtime C# scripting needed by the browser to compiled Markup 2.0 or regular C#. Verify editor import and export separately from visualization. |

After adding the browser project at the proposed path, run this command from the ThirtyDollarTools root.

```sh
dotnet publish Visualizer/ThirtyDollarVisualizer.Browser/ThirtyDollarVisualizer.Browser.csproj -c Release -o artifacts/visualizer-browser
```

Serve `artifacts/visualizer-browser/wwwroot`. If using locally packed Sundex packages, restore them from the local feed and set `SundexVersion` in `Sundex.props` or pass `-p:SundexVersion=<version>` so the Visualizer and its scene projects all use the same build. The desktop release workflow continues to publish the desktop project for each native RID.
