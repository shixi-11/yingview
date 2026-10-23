# Themes

YingView includes four appearance presets: Obsidian, Paper, Moss, and Iris. Open **More → Appearance** to choose a preset or import and export a theme as JSON.

The appearance editor previews changes as you make them. Choose a color area and click a color swatch; the advanced section also accepts a six-digit `#RRGGBB` color. You can rename the current theme, adjust interface text size from 85% to 130%, and turn on a compact toolbar. **Apply** saves these settings. **Cancel** restores the appearance from before the editor opened, and **Reset** returns to the default preset and layout settings.

JSON theme import and export are available in the current version. A theme file has exactly seven fields:

| Field | Purpose |
| --- | --- |
| `Name` | Theme name; 1–60 characters |
| `Shell` | Main window surface color |
| `Toolbar` | Toolbar surface color |
| `Text` | Primary interface text color |
| `Muted` | Secondary interface text color |
| `Accent` | Highlight and selection color |
| `Canvas` | Image-view canvas color |

Colors must use six-digit hexadecimal notation in the form `#RRGGBB`. JSON files control the theme name and colors. Text size and compact-toolbar preference are saved separately in the app settings.

To import a theme, choose **More → Appearance → Import theme…**. To save the current theme to a file, choose **Export current theme…**. The app checks the theme name and color values when importing. The `Moss` and `Iris` files in this folder match the built-in presets and can be imported directly.
