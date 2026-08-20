# Dock icon sources

The active SVG glyphs are exported from the editable Figma components in
[AmseokOS Logo Family](https://www.figma.com/design/oH46ZrNcvNnigREpEAKa6a)
on 2026-08-13. Their glossy squircle tiles are rendered by the Dock component
so they remain sharp at every responsive size.

| Local SVG | Figma component |
| --- | --- |
| `dashboard.svg` | `Logo/rocket` |
| `storage.svg` | `Logo/stethoscope` |
| `shares.svg` | `Logo/finder` |
| `users.svg` | `Logo/space` |
| `operations.svg` | `Logo/tasks` |
| `terminal.svg` | `Logo/terminal` |
| `app-store.svg` | `Logo/store` |
| `settings.svg` | `Logo/settings` |

The application never loads assets from Figma at runtime. Every glyph is
versioned in this directory, and its tile colors and effects are versioned in
`dock.component.scss`, so fresh clones render the same Dock without external
network access.
