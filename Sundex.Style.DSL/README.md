# Sundex.Style.DSL

The parser for Sundex `.snx.ss` stylesheets: rules for components, classes and ids, variables, imports, animations, gradients, states and media blocks.

- `StyleParser` parses a sheet and its imports into a `StyleSheet`.
- `StyleSheet` answers which properties apply to a tag and state.
- `Abstract/` holds the style value types.
- `Examples/` holds sample sheets.

[Sundex Style](../editors/sundex-style/README.md) adds highlighting and live checks in VS Code and Rider.
