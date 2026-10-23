# Themes

YingView includes four appearance presets: Obsidian, Paper, Moss, and Iris. Use **More → Appearance** to switch presets, or import and export a theme as JSON.

A theme file has exactly seven fields:

| Field | Purpose |
| --- | --- |
| `Name` | Theme name; 1–60 characters |
| `Shell` | Main window surface color |
| `Toolbar` | Toolbar surface color |
| `Text` | Primary interface text color |
| `Muted` | Secondary interface text color |
| `Accent` | Highlight and selection color |
| `Canvas` | Image-view canvas color |

Colors must use six-digit hexadecimal notation in the form `#RRGGBB`. Theme files currently control these colors and the theme name only. They do not set fonts or layout.

To import a theme, choose **More → Appearance → Import theme…**. To save your current theme, choose **Export current theme…**. The app validates the theme name and color values when importing. Exported files are ordinary JSON and can be shared or edited in a text editor.

The `Moss` and `Iris` files in this folder match the built-in presets and can be imported directly.
