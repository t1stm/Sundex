# Sundex.Style.LanguageServer

`sundex-style-ls`, a language server that reports `.snx.ss` errors the way an app would hit them on load. [Sundex Style](../editors/sundex-style/README.md) runs it in VS Code and Rider.

```sh
dotnet tool install --global Sundex.Style.LanguageServer --prerelease
```

- `Program.cs` speaks the language server protocol.
- `StyleLinter.cs` checks a sheet, its imports and every property name.
