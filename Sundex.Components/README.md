# Sundex.Components

The UI toolkit: elements, layout, input, focus and styling from [Sundex.Style.DSL](../Sundex.Style.DSL/README.md) sheets.

- `Abstractions/` holds `UIElement` and `UIContext`, which lays out, styles, draws and routes input for a tree.
- `Panels/` holds containers, such as flex, grid, stack, table, dropdown, tooltip and modal panels.
- `Labels/` holds labels, buttons, links and key chips.
- `Inputs/` holds text and numeric inputs, checkboxes, switches, sliders and segmented controls.
- `Scroll/`, `Bars/`, `Markdown/` and `FileSelector/` hold the scroll view, progress bar, Markdown view and file dialog.
- `Icons` lists the bundled Lucide icons.

A custom component needs `[MarkupTag("tag")]` to be used from markup, and `[NamedSetting("name")]` on each property a stylesheet sets.
