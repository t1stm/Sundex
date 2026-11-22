# Sundex Style

Highlighting and live checks for Sundex `.snx.ss` stylesheets, in VS Code and Rider.

The checks come from `sundex-style-ls`, a language server built on Sundex's own parser, so an
editor reports what an app would throw when it loads the sheet:

- parse errors, at the line and column they're on
- imports that don't resolve, or that fail to parse themselves, at the `import`
- unit, keyframe and state mistakes the sheet only finds when it's built (reported on line 1)
- settings no element has, such as `font-weight` or a misspelt `opacty`, which the runtime
  would otherwise ignore without a word (warnings)

Settings that an app's own components declare with `[NamedSetting("…")]` count as known: the
server reads them from the `.cs` files of every project in the solution.

## Install the server

```sh
dotnet tool install --global Sundex.Style.LanguageServer
```

From a Sundex checkout, `dotnet pack Sundex.Style.LanguageServer -o artifacts` and add
`--add-source artifacts` instead.

`sundex-style-ls --check <files…>` checks sheets from a terminal or CI and prints problems in
MSBuild's format. It exits with 1 when any is an error.

## VS Code

```sh
cd editors/sundex-style
npm install
npx @vscode/vsce package --skip-license
code --install-extension sundex-style-0.1.0.vsix
```

`sundexStyle.server.command` and `sundexStyle.server.args` point at a server that isn't the
installed tool, such as `dotnet` with the path to a freshly built `Sundex.Style.LanguageServer.dll`.

## Rider

Highlighting comes from the same grammar, as a TextMate bundle:

1. Settings → Editor → TextMate Bundles → **+**, and pick the `editors/sundex-style` folder.
2. If `.snx.ss` files still open as plain text, add `*.snx.ss` to the bundle's file type under
   Settings → Editor → File Types.

Live checks need the free [LSP4IJ](https://plugins.jetbrains.com/plugin/23257-lsp4ij) plugin:

1. Install LSP4IJ from Settings → Plugins.
2. Settings → Languages & Frameworks → Language Servers → **+**.
3. Name it `Sundex Style`, and set the command to `sundex-style-ls`.
4. Under Mappings → File name patterns, add `*.snx.ss` with the language id `sundex-style`.
