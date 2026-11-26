# Project setup

## Adapt it to a project

Copy [the minimal pack](../packs/minimal) to your own project and build with `--pack <folder>`. The core scripts stay the same.

- `profile.json` defines extension identity and version, menu and shortcut identifiers, plan group, starter layers, initial brush and local update naming. Use unique identities for packs you want to install side by side.
- `resources/tiles.png` is an RGBA atlas of square tiles. `resources/tiles.json` schema 2 defines the tile size, atlas column count and palette order. Use between 1 and 255 distinct opaque brush colors. Each brush has either `sprites` (Blob 47 masks mapped to zero-based atlas indices) or `parts` (tile indices and offsets for an anchored stamp). Names, categories, thumbnail masks and anchor labels are configurable.
- Blob masks use N/E/S/W = 1/2/4/8 and NE/SE/SW/NW = 16/32/64/128. A diagonal only counts if both adjacent cardinal neighbors match. Run `python scripts/make_demo_pack.py` to regenerate the original example tiles.
- For local updates, place versioned archives named `<archiveStem>-<version>.aseprite-extension` in the profile's `updateFolder` under Aseprite's configuration directory. The **Update Plugin…** command selects the highest newer version.

### Add your first decoration brush

This example reuses the Stone art to show how a brush is connected to an atlas tile.

1. Copy `packs/minimal` to `MyPack`. In the copied `profile.json`, set `name` to `my-tilemap-pack`, `menuTitle` to `My Tilemap Pack`, and `commandPrefix` to `MyTilemapPack` so you can install the pack alongside the sample.
2. In `MyPack/resources/tiles.json`, append `"marker"` to `brushOrder`. Add this entry inside `brushes`.

   ```json
   "marker": {
     "label": "Marker",
     "color": "#D76CB2",
     "parts": [{ "x": 0, "y": 0, "index": 47 }]
   }
   ```

   Index 47 is the Stone tile in the included 16 px atlas. For your own art, place a tile in an unused atlas cell and set `index` to its zero-based position, counting left to right and top to bottom.

3. From the repository root, build the extension with the new pack.

   ```sh
   python scripts/build_extension.py --pack MyPack --output .build/MyTilemapPack.aseprite-extension
   ```

4. Add the archive in Aseprite through **Edit → Preferences → Extensions → Add Extension**. Choose **File → Scripts → My Tilemap Pack → New Terrain Plan…**, then select **Marker** in the preview and paint on **Details**.

## Configure Unity

In Unity, add `ProjectSettings/AsepriteTilemapEditor.json` if you want to override these defaults:

```json
{
  "autoImport": true,
  "sourceFolder": "Assets",
  "outputFolder": "Assets/Resources/Tilemaps",
  "sharedTilesetFolder": "Assets/AsepriteTilemapEditor/SharedTilesets",
  "collisionMode": "Physics2D",
  "renderRules": []
}
```

Set `collisionMode` to `Grid` when gameplay reads Tilemap cells directly. It keeps solid tile types and disables native TilemapCollider2D components on imported layers. Reimporting in `Physics2D` restores physics for solid layers and preserves manually disabled colliders. Reimport the source to apply changed settings even if its bytes are unchanged.

A render rule such as `{"layer":"Trees","role":"DepthSorted","includeOverlaps":true}` gives that layer and its exported overlap planes Unity's Individual renderer mode. Use `Static` for Chunk rendering and native chunk culling. Rules may also specify `sortingLayer`, or `overrideSortingOrder` with `sortingOrder` to mix the layer with actors. With no rule, the importer retains the current render mode and layer stack order. The consuming project's camera and actor renderers define their transparency sorting. The importer keeps layers separate and leaves chunk sizes and authored culling bounds intact.

Each cell is one Unity unit. The tileset's square pixel size sets the sprite PPU. The importer supports single-frame RGBA native Tilemaps, square grids from 1 to 512 px and embedded tilesets without flips. It retains the source link across moves and renames inside Unity. Scenes, gameplay, visual packs and collision policy above the tile layer belong to the consuming project.

Unity stores compressed `.asepriteatlas` source pages once per identical embedded tileset. Maps keep their own Sprite and Tile sub-assets, with the same identifiers across reimports, and reference those shared textures. Reimporting a map with changed artwork creates new shared pages for that map. Old pages stay available to other maps that reference them. The cache defaults to a folder outside Resources so unused pages are not included merely because they are stored there. Existing maps switch to shared textures the next time they are reimported or synchronized.

When checking in generated prefabs, include their companion maps and referenced shared pages with their `.meta` files. Unity uses the page GUIDs across checkouts.

Solid layers generate full-cell colliders only where a pixel was painted in the editable plan. An exported stamp may render over several cells while its painted anchor remains the only solid cell. The export carries anchors in hidden `AsepriteTilemap:Collision:<layer index>` helper layers. Unity consumes these helpers when it sets collision on the original Tilemap. If a painted anchor has no art of its own, it receives a sprite-less collision tile.

Projects adopting older exports may set `legacySolidTag` and `legacyPassableTag` to their previous export markers. Old files marked `AsepriteTilemap:Solid` still import with all rendered cells solid. Reexport the editable plan to switch to painted-cell collisions.

## Verification

From the repository root, build and unpack the example, then run the native Aseprite smoke test.

```powershell
python scripts/build_extension.py --output .build/AsepriteTilemapEditor.aseprite-extension --script-root .build/extension
aseprite -b --script-param "root=$PWD/.build/extension" --script-param "output=$PWD/.build/Demo.aseprite" --script tests/aseprite.lua
```

For a local or Git UPM dependency, add `"testables": ["com.ash2ops.aseprite-tilemap-editor"]` to the consuming project's Unity manifest to enable the package tests. The Edit Mode assembly `AsepriteTilemapEditor.Editor.Tests` covers native exports, collisions, reimports, source renames, duplicate filenames, importer ownership and shared texture preservation when one map changes artwork.

To regenerate the README example, run this after unpacking the extension as above.

```powershell
aseprite -b --script-param "root=$PWD/.build/extension" --script-param "tilemap=$PWD/.build/ReadmeTilemap.aseprite" --script-param "output=$PWD/docs/plan-to-tilemap.png" --script scripts/render_demo.lua
```
