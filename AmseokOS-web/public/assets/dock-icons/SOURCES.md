# Dock icon sources

The active system app icons were supplied as `system-icons-svg.zip` on
2026-08-20 and are versioned locally with their squircle artwork. Their
embedded drop shadows are removed locally to avoid dark halos in the Dock.

| Local SVG | Supplied SVG |
| --- | --- |
| `dashboard.svg` | `system-overview.svg` |
| `terminal.svg` | `terminal.svg` |
| `app-store.svg` | `app-store.svg` |
| `settings.svg` | `settings.svg` |
| `launchpad.svg` | `launchpad.svg` |

The inactive `storage.svg`, `shares.svg`, `users.svg`, and `operations.svg`
glyphs remain exports from the editable Figma components in
[AmseokOS Logo Family](https://www.figma.com/design/oH46ZrNcvNnigREpEAKa6a).

The application never loads icon assets from an external service at runtime.
