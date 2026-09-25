# Asset generator

Enable **Asset generator** in Skills, then ask the AI to draw a logo, icon, diagram, game asset or illustration. In the GUI, open **Tools > Assets** to watch each completed drawing operation. The native preview does not need a browser. The CLI provides the same tools and saved exports.

For example:

> Create a 512 × 512 space badge with a transparent background. Use separate layers for the planet, orbit and title. Capture the canvas, inspect the result and refine the spacing. Export SVG and transparent PNG.

## Canvas and layers

- Draw rectangles (including rounded corners), ellipses, circles, lines, polygons, polylines, arbitrary SVG paths and text.
- Use the full RGB palette, alpha, fill, outline, outline width, opacity and rotation. Colors accept `#RRGGBB`, `#RRGGBBAA`, `none` and `transparent`.
- Name, reorder, hide or delete layers and shapes. Layers have independent opacity. The GUI lists the frontmost layer first and provides visibility and ordering controls.
- Coordinates start at the canvas's top-left. Rectangles and ellipses use their top-left corner; circles use their center; lines use their start; text uses its baseline. Paths and polygon points use absolute canvas coordinates. Rotation is around the shape's `x`/`y` point.
- Use the GUI palette to inspect colors and set or remove the canvas background. Export options are available under **Export**.

The AI updates a structured drawing scene, rather than executing SVG scripts or loading remote SVG resources. Changes become visible when each tool call succeeds. This is an AI drawing workspace; it is not a freehand mouse editor or a general SVG importer.

## Pixel art, guides and animation

Set `pixel_size` when creating a canvas to draw in logical cells. For example, a 64 × 64 canvas with `pixel_size: 8` has an 8 × 8 pixel-art grid. Width and height must be divisible by the pixel size. `asset_edit` accepts `pixel`, `erase_pixel`, `pixel_rect` and `pixel_line`; their `x`/`y` coordinates are logical cell coordinates, while vector shapes still use canvas coordinates. `color` accepts the full RGB palette with optional alpha. Pixel cells render without antialiasing.

`asset_guides` returns the exact canvas center, thirds, and the bounds and center offsets of visible shapes and painted pixel regions. Text, SVG paths and rotated shapes have approximate bounds; capture the canvas to verify them visually. Set `guides: true` or `grid: true` in `asset_capture`, or use the **Repères / Grille** controls in the GUI, to overlay visual guides. These overlays never appear in exported files.

Use `frame_add` to copy the base scene or another frame, then target edits with `frame_id`. `frame_duration` sets each frame's duration from 20 to 10,000 ms; `frame_delete` removes one. The GUI includes frame selection, playback and timing controls. Animated exports use the frames in order and loop. With no frames, animation exports contain one still frame.

Export `svg-animated` for a self-contained SVG with discrete timed frames, `gif` for a looping GIF, or `frames` for a ZIP of numbered PNG files and `frames.json` timing metadata. The existing `svg`, `png`, `webp`, `jpeg` and `pdf` formats export the first animation frame when frames exist. GIF is limited to 1024 pixels per side, 4 million pixels across all frames and scale 1; PNG sequences are limited to 64 million pixels total. Use animated SVG for larger artwork.

## Tools

| Tool | Purpose |
| --- | --- |
| `asset_create` | Create a named canvas with dimensions and an optional background. Returns `asset_id`, `revision`, and the initial `layer-1`. |
| `asset_inspect` | Read the scene and current revision, or list this conversation's drawings when no ID is supplied. |
| `asset_guides` | Read exact center and alignment coordinates, with visible shape and pixel-region bounds. |
| `asset_edit` | Apply an atomic batch of layer, shape, ordering or canvas operations. Requires `expected_revision` to protect edits made by the user or another operation. |
| `asset_capture` | Return a PNG image of the artwork, its original dimensions and capture scale. No desktop screenshot is taken. |
| `asset_export` | Save SVG, PNG, WebP, JPEG, PDF, animated SVG, GIF or a PNG-frame ZIP and return the absolute output path. |

Example edit after creating a canvas at revision 1:

```json
{
  "asset_id": "<ID returned by asset_create>",
  "expected_revision": 1,
  "operations": [
    { "action": "layer", "layer_id": "planet", "name": "Planet" },
    {
      "action": "shape",
      "layer_id": "planet",
      "shape": {
        "id": "disc", "type": "circle",
        "x": 256, "y": 256, "radius": 140,
        "fill": "#7C3AED", "stroke": "#4CC9F0", "strokeWidth": 8
      }
    }
  ]
}
```

Supported operations are `canvas`, `layer`, `delete_layer`, `move_layer`, `shape`, `delete_shape`, `move_shape`, `pixel`, `erase_pixel`, `pixel_rect`, `pixel_line`, `frame_add`, `frame_delete` and `frame_duration`. A `shape` upsert replaces the complete shape; omitted fields return to defaults. Move indices are zero-based, from back to front. Re-inspect the scene if an edit reports a revision conflict.

## Capture and export

Canvas captures are sent through the existing image pipeline. A vision-capable model or the configured image bypass skill is needed to interpret them. Captures contain the drawing only, without application chrome or a checkerboard background. The GUI displays a checkerboard to make transparency visible.

SVG preserves vector shapes and named layers. PNG and WebP preserve transparency; JPEG requires an opaque background such as `#FFFFFF`. PDF retains vectors. The `transparent` option removes the canvas background, but does not delete background shapes drawn into layers. Raster exports support scales up to 4×, subject to the pixel limit. The GUI also provides a Save As dialog.

Drawings are persisted beside the database under `assets/chat-<id>`, with exports in that conversation's `exports` subfolder. Reopening the conversation restores its drawings. Asset export creates a new file instead of overwriting previous exports.

## Availability and limits

The skill is opt-in and follows application permissions. Plan mode allows inspection, guides and capture only. It is unavailable in sandbox mode, in subagents, and through the OpenCode-native adapter. The live canvas is implemented in the Uno GUI.

Canvases are limited to 4096 × 4096, with up to 64 layers per frame, 32 frames, 5,000 shapes, 20,000 painted cells and a 2 MB serialized scene. Each edit accepts up to 100 operations. Raster exports are limited to 16 megapixels and 8192 pixels per side. Captures are limited to a 2048-pixel longest side and 8 MB. These limits bound memory use while the UI remains responsive.
