# Flying Moai on the desktop

Runs [Flying Moai](../FlyingMoai/README.md) in an OpenTK window through Sundex's `Game`.

Run from the repository root with the .NET 10 SDK. On Windows, build the native audio library first, as [BUILDING.md](../../BUILDING.md) describes.

```sh
dotnet run --project samples/FlyingMoai.Desktop
```

## Contents

- `Program.cs` initializes the engine.
- `MoaiScene.cs` is a scene that wraps `MoaiGame`.
- `BestScoreFile.cs` keeps the best score in `$LOCAL/FlyingMoai/best.txt`

Debug builds also enable automatic stylesheet reparsing and reloading.
