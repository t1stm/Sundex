# Sundex.Markup.Generator

A source generator that compiles version 2.0 `.snx.xml` documents into C# classes. 
Build time checks report errors and warnings and fail the build as needed.

[Sundex.Markup](../Sundex.Markup/README.md) is the language itself. It reads these MSBuild properties.

- `SundexMarkupContext` is the context type documents get unless they name their own.
- `SundexMarkupNamespace` is where the classes go. It defaults to `$(RootNamespace).Markup`.

Contents:

- `MarkupDocument.cs` parses a document.
- `Emitter.cs` writes its class.
- `StyleIndex.cs` reads the names a `.snx.ss` sheet declares.
- `buildTransitive/` adds the documents and sheets to the build.
