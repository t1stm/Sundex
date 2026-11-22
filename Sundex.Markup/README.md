# Sundex.Markup

The Sundex markup language: `.snx.xml` documents with a `<layout>`, an optional `<style>` and optional C# `<logic>`.

```sh
dotnet add package Sundex.Markup --prerelease
```

Version 2.0 documents compile to C# at build time through [Sundex.Markup.Generator](../Sundex.Markup.Generator/README.md), which the package brings in. Version 1.0 documents are built at runtime, and their logic needs [Sundex.Markup.Legacy](../Sundex.Markup.Legacy/README.md). [Flying Moai](../samples/FlyingMoai/Markup) has 2.0 examples.

- `MarkupComponent` is the base of every generated class.
- `SundexContext<T>` builds 2.0 documents and holds the `Context` they read.
- `MarkupRuntime` is what generated code calls.
- `MarkupParser`, `Document/` and `Builders/` parse and build 1.0 documents.
