# Flying Moai in the browser

Runs [Flying Moai](../FlyingMoai/README.md) on a WebGL 2 canvas. Start from this project for a browser build of a Sundex app.

Run from the repository root with the .NET 10 SDK. `dotnet build Sundex.slnx` skips this project.

```sh
dotnet workload install wasm-tools
dotnet publish samples/FlyingMoai.WASM -c Release
python -m http.server 8766 --bind 127.0.0.1 --directory samples/FlyingMoai.WASM/bin/Release/net10.0/publish/wwwroot
```

Open `http://127.0.0.1:8766/` in a WebGL 2 browser. Other servers must serve `.wasm` as `application/wasm`.

## Contents

- `FlyingMoai.WASM.csproj` relinks the .NET runtime with Emscripten, WebGL 2 and the C files.
- `browser.c` creates the WebGL 2 context on the `#sundex` canvas.
- `BrowserContext.cs` makes the context current and loads OpenTK's GL bindings. An app that calls other GL functions may need more signatures there.
- `Program.cs` exports the game's entry points to JavaScript.
- `ToneTest.cs` runs the `?audiotest` tone.
- `wwwroot/main.js` loads the runtime, forwards input, runs the frame loop and keeps the best score in `localStorage`.

Audio plays through miniaudio's Web Audio backend and is a test target for now. The project links `Sundex.Engine/Audio/Native/sundex_audio.c`, which the engine package doesn't ship, so a browser app needs it from a Sundex checkout.

## Page options

- `?verify` fails the run on a GL error and puts the current screen in the canvas's `data-screen`.
- `?benchmark` records frames 30 to 149 as `flying-moai-frame` performance measures, and the first frame as the `flying-moai-first-frame` mark.
- `?audiotest` plays a 440 Hz tone while the main thread is loaded, and reports in the page and console how often its queue ran dry. `&volume=0` mutes it.
