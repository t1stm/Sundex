# Flying Moai in the browser

Run from the repository root with the .NET 10 SDK and `wasm-tools` workload installed.

```sh
dotnet publish samples/FlyingMoai.WASM -c Release
python -m http.server 8766 --bind 127.0.0.1 --directory samples/FlyingMoai.WASM/bin/Release/net10.0/publish/wwwroot
```

Open `http://127.0.0.1:8766/` in a WebGL 2 browser. Press Space, Up, W, Enter, or click the canvas to clap. After a crash, wait for the results card and use its “Clap again” button or press Space. Best scores are saved per browser origin.

For a regular `dotnet build samples/FlyingMoai.WASM -c Release`, serve `samples/FlyingMoai.WASM/bin/Release/net10.0/wwwroot`. That directory contains the page, loader, and runtime assets.

Serve the published `wwwroot` directory as a static site. Keep `_framework` in place and serve `.wasm` as `application/wasm`. NGINX can serve the precompressed output with `gzip_static on`. The sample publishes without IL trimming because graphics initialization discovers preload methods through reflection.

## Browser checks and timings

- Add `?verify` to fail the run on a GL error and expose the current game screen on the canvas in `data-screen`. The animated title and results cards exercise generated Markup, Slug text, textures, and framebuffer passes for backdrop blur.
- Add `?benchmark` and leave the title screen running to record 120 consecutive frames after 30 warmup frames. Use the same canvas size, device pixel ratio, and GPU configuration when comparing runs.

Read the measurements from the browser console after the 120 frames finish.

```js
const frames = performance.getEntriesByName('flying-moai-frame').map(entry => entry.duration);
const firstFrame = performance.getEntriesByName('flying-moai-first-frame')[0].startTime;
const transferBytes = performance.getEntriesByType('resource').reduce((sum, entry) => sum + entry.transferSize, 0);
```

`firstFrame` measures time from navigation to the first submitted frame. Each duration in `frames` covers managed update and render submission on the browser thread, in milliseconds. GPU execution and presentation happen separately.
