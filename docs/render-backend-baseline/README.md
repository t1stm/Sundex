# Render backend baseline

Captured on 2026-11-19 from clean master snapshots of Sundex `34b1422`,
ThirtyDollarTools `f39df77c`, and musicrain `83e9528`.

| View | Before | After |
| --- | --- | --- |
| Visualizer, Flower Castle playback | [Screenshot](before/visualizer.png) | [Screenshot](after/visualizer.png) |
| Editor, Flower Castle piano roll | [Screenshot](before/editor.png) | [Screenshot](after/editor.png) |
| musicrain, home page | [Screenshot](before/musicrain-home.png) | [Screenshot](after/musicrain-home.png) |
| musicrain, full player | [Screenshot](before/musicrain-player.png) | [Screenshot](after/musicrain-player.png) |

The Editor, home page, and full player matched pixel for pixel. The Visualizer
shows the same tiles, colors, and labels in both captures. Its camera scrolls
during playback, so its two captures are at different positions within the
same second.

## Measurement setup

- AMD Radeon RX 9070 XT with radeonsi and Mesa 26.2.6, private Xwayland at
  60 Hz, Debug builds, 120 warm-up frames and 600 measured frames per run.
- `GpuTimer` measured the window pass with GL elapsed-time queries. CPU wall
  time ran from the update callback through drawing, before presentation.
- ThirtyDollarTools ran at 1600 × 840 with audio output disabled. The Visualizer
  played `Flower Castle TDW Cover - Final.tdw`. The Editor opened
  `flower castle.tdwproj` on the `P2.1 koto and forward march` piano roll.
- musicrain ran at 1280 × 800 against its local mock API with a fixed random
  seed. The full player showed the paused fixture `audio://mock001`.

## Frame times

All values are milliseconds, shown as p50 / p95. ThirtyDollarTools has one
matched Debug run per engine. musicrain reports the median of each percentile
across four Debug baseline runs and two runs with the optimized bindings.

| View | CPU before | CPU after | GPU before | GPU after |
| --- | ---: | ---: | ---: | ---: |
| Visualizer | 0.748 / 0.959 | 0.690 / 0.924 | 0.077 / 0.091 | 0.073 / 0.089 |
| Editor | 0.962 / 1.084 | 0.879 / 0.994 | 0.231 / 0.242 | 0.226 / 0.231 |
| musicrain home | 0.523 / 0.594 | 0.487 / 0.562 | 0.111 / 0.144 | 0.104 / 0.154 |
| musicrain full player | 0.533 / 0.610 | 0.493 / 0.562 | 0.109 / 0.122 | 0.108 / 0.122 |

The musicrain home GPU p95 varied from 0.117 to 0.165 ms across baseline runs.
The full-player GPU p95 measured 0.121 to 0.136 ms at baseline and 0.121 to
0.123 ms after removing redundant uniform-buffer binds. OpenGL indexed base
bindings continue to reference the full buffer when its store is replaced.
[OpenGL core specification §6.1](https://registry.khronos.org/OpenGL/specs/gl/glspec46.core.pdf)

The ported Debug apps also ran against the final Release NuGet packages. Those
checks used a Debug engine for the timer hook and the other assemblies from
the Release packages. [Individual measurements](timings.json)

The original core-only sample drew 120 frames successfully under Mesa llvmpipe
before the refactor. The expanded sample passed all pixel probes under
llvmpipe with OpenGL 4.6 and with its OpenGL 3.3 profile.
An isolated engine build also passed the 3.3 smoke test with direct state
access forced off, exercising the fallback vertex array setup.

## Local verification

- Sundex passed 591 Debug tests and 586 Release tests, including the graphics API
  boundary test. The packaged sample probed each plane type, Slug text, scissor
  edges and a copied backdrop, before and after relinking a shader.
- ThirtyDollarTools passed all 1,100 tests against the prerelease. Its existing
  GPU mixer benchmark returned `go` on a ten-second window of Flower Castle.
- musicrain built in Debug and Release against the same prerelease.

The app ports are in the sibling worktrees `ThirtyDollarTools-render-backend`
and `AudioAPI-render-backend`. Both point to `1.0.0-render.5`. The ten packages
and their symbols are in Sundex's local `artifacts/` feed. From a sibling
worktree, pass the absolute feed path as `-p:RestoreAdditionalProjectSources` when
building against it.

## Phase 2

Measured on 2026-11-20 with the same machine, display, Debug app builds and frame counts. A
temporary timing hook in `Game`, which isn't part of the change, switched between three
configurations of one engine build. Each value is the mean of two interleaved runs, in
milliseconds, as p50 / p95.

- Direct draws to the window, as phase 1 does.
- Offscreen draws to the frame target and then copies it to the window.
- Hazards is offscreen with the frame hazard check on, as in a development build.

| View | CPU direct | CPU offscreen | CPU hazards | GPU direct | GPU offscreen |
| --- | ---: | ---: | ---: | ---: | ---: |
| Visualizer | 0.517 / 0.643 | 0.527 / 0.688 | 0.531 / 0.668 | 0.076 / 0.091 | 0.081 / 0.091 |
| Editor | 0.633 / 0.760 | 0.629 / 0.735 | 0.637 / 0.754 | 0.228 / 0.244 | 0.235 / 0.247 |
| musicrain home | 0.422 / 0.487 | 0.428 / 0.502 | 0.431 / 0.509 | 0.099 / 0.111 | 0.104 / 0.115 |
| musicrain full player | 0.433 / 0.507 | 0.442 / 0.508 | 0.443 / 0.520 | 0.106 / 0.116 | 0.111 / 0.118 |

CPU time and GPU p95 stayed within run-to-run noise. The copy to the window added 4 to 7 µs of GPU
time at p50 in every view. So frames draw offscreen only while they copy from their target, and
ThirtyDollarTools, which has no backdrop blur, keeps drawing straight to the window. Each run's
numbers are under `phase2` in [timings.json](timings.json).

Screenshots against the phase 1 captures:

- The Editor matched pixel for pixel.
- On the musicrain home page, only pixels under backdrop blur changed, by at most 10 of 255 per
  channel. The copy under each blurred box now includes neighboring pixels, so its mip levels
  differ slightly.
- The full player differed only in its progress bar, since it was paused at a different time.

The hazard check reported nothing in either app after the ThirtyDollarTools fixes. The runs covered:

- Visualizer: loading and 40 s of Flower Castle playback.
- Editor: opening the project, then playing, scrolling, adding a note and undoing in the P2.1
  piano roll.
- musicrain: home, search, roll again, scrolling, playback, the full player, queue and chat panels,
  and the library.

Both app worktrees now point to `1.0.0-render.6`, which includes phase 2. ThirtyDollarTools passed
all 1,100 tests against it, and musicrain built.
