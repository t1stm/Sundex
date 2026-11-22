# Sundex.Reactive

Signals for UI state: `State`, `Computed`, `Effect` and async `Resource`, with scopes that end them together. No dependencies.

Write reactive code with `using static Sundex.Reactive.Rx;`.

- `State.cs`, `Computed.cs`, `EffectHandle.cs` and `Resource.cs` hold the reactive values.
- `Scope.cs` holds the lifetime that effects, computeds and timers belong to.
- `Frame.cs` is the UI thread's run loop: timers, frame tickers and the effect flush.
- `Ease.cs` holds easing curves and tweens.

`Game` runs `Frame` for you. Another host calls `Frame.Install()` once on the UI thread and `Frame.Pump()` each frame.
