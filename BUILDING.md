# Building Sundex

## Build the libraries

You need the .NET 10 SDK. Run these from the repository root.

```sh
dotnet build Sundex.slnx
dotnet test --solution Sundex.slnx
```

`Sundex.Engine` builds its native audio library, miniaudio and the `sundex_audio` shim, for the host machine. On Linux and macOS the build runs `build/audio-native.sh` with `cc`, or `$CC` if set. On Windows the build skips it. Run `sh build/audio-native.sh win-x64 clang` from Git Bash, or take `sundex_audio.dll` from a CI build. Pass `-p:SundexSkipAudioNative=true` to skip the native build.

Debug builds need nothing else. Release builds validate the ImageSharp license, so set `SIXLABORS_LICENSE_KEY` in the environment or add a `sixlabors.lic` file.

To test an app against local changes, pack the libraries to a folder and use it as a package source.

```sh
dotnet pack Sundex.slnx -c Release -o artifacts -p:Version=0.0.1-local.1
```

A local pack ships only the audio libraries present under `Sundex.Engine/Audio/Native/runtimes`. Usually that is just the host's.

## Start a new app

Sundex is published to nuget.org as prerelease packages. Create a console app and add `Sundex.Core`, which brings in Engine, Slug and Reactive.

```sh
dotnet new console -n MyGame
cd MyGame
dotnet add package Sundex.Core --prerelease
```

`Game` opens the window and runs the scene loop. Pass it the assemblies that hold your embedded assets, then load a scene once the window is up.

```csharp
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Serilog;
using Sundex.Engine;

using var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
using var game = new Game(logger, [typeof(Program).Assembly], new GameWindowSettings(), new NativeWindowSettings
{
    ClientSize = (1280, 720),
    APIVersion = new Version(3, 3),
    Flags = ContextFlags.ForwardCompatible,
    Title = "My Game",
    Vsync = VSyncMode.On
}, "MyGame");

game.Enqueue(instance => instance.SceneManager.LoadScene("main", _ => new MainScene(instance)));
game.Enqueue(instance => instance.SceneManager.TransitionTo("main"));
game.Run();
```

`MainScene` derives from `Sundex.Engine.Scenes.Scene`. Build its contents in `Initialize`, and draw them in `Render`. [Flying Moai's scene](samples/FlyingMoai.Desktop/MoaiScene.cs) is a complete example. Keep textures, fonts and shaders as `EmbeddedResource` items.

### UI and markup

Add the packages for the parts you use. `Sundex.Style.DSL` adds styling. `Sundex.Components` adds UI controls. `Sundex.Markup` adds Markup 2.0, and brings both of those and `Sundex.Markup.Generator`, which compiles the documents to C#.

```sh
dotnet add package Sundex.Markup --prerelease
```

The generator compiles every `.snx.xml` in the project into a component class in `$(RootNamespace).Markup`. Set `SundexMarkupContext` in the project file to the type the documents bind to. Override the namespace with `SundexMarkupNamespace`. [Flying Moai](samples/FlyingMoai/FlyingMoai.csproj) has documents with styles and logic under `Markup/`.

### Develop against a Sundex checkout

To change Sundex and the app together, reference the projects instead of the packages. Project references don't apply the packages' `buildTransitive` props, so import them yourself. The samples do the same.

```xml
<Import Project="../Sundex/Sundex.Engine/buildTransitive/Sundex.Engine.props"/>
<Import Project="../Sundex/Sundex.Markup.Generator/buildTransitive/Sundex.Markup.Generator.props"/>

<ItemGroup>
  <ProjectReference Include="../Sundex/Sundex.Markup/Sundex.Markup.csproj"/>
  <ProjectReference Include="../Sundex/Sundex.Markup.Generator/Sundex.Markup.Generator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false"/>
</ItemGroup>
```

For a browser build, start from [the WASM sample](samples/FlyingMoai.WASM/README.md).
