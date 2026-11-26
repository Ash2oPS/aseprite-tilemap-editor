using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace AsepriteTilemapEditor
{
    [ScriptedImporter(4, Extension)]
    public sealed class AsepriteMapArtImporter : ScriptedImporter
    {
        internal const string Extension = "asepritemap";
        internal const string CollisionTileName = "T_AsepriteCollisionCell";
        internal static string TileName(uint tileset, int tile, bool solid = false) =>
            $"T_Tileset{tileset.ToString(CultureInfo.InvariantCulture)}Tile{tile.ToString(CultureInfo.InvariantCulture)}" +
            (solid ? "Solid" : "");

        public override void OnImportAsset(AssetImportContext context)
        {
            var companion = AsepriteMapCompanion.Decode(AsepriteMapCompanion.ReadFile(context.assetPath));
            var map = AsepriteMapDocument.Read(companion.Source);
            var solidTiles = map.Layers.Where(layer => layer.Solid == true)
                .SelectMany(layer => layer.Cells.Where(layer.IsSolid).Select(cell => (layer.TilesetId, cell.TileId)))
                .ToHashSet();
            if (map.Layers.Any(layer => layer.CollisionCells != null))
            {
                var collision = ScriptableObject.CreateInstance<Tile>();
                collision.name = CollisionTileName;
                collision.colliderType = Tile.ColliderType.Grid;
                context.AddObjectToAsset(CollisionTileName, collision);
            }
            Texture2D mainTexture = null;
            foreach (var tileset in map.Tilesets.Values)
            {
                var pageCount = AsepriteMapAtlas.PageCount(tileset);
                for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    var page = AsepriteMapAtlas.GetPage(tileset, pageIndex);
                    Texture2D texture;
                    if (companion.SharedPages == null)
                    {
                        var name = $"T_Tileset{tileset.Id.ToString(CultureInfo.InvariantCulture)}Page{pageIndex}";
                        texture = AsepriteMapAtlas.BuildTexture(tileset, page, name);
                        context.AddObjectToAsset(name, texture);
                        if (mainTexture == null)
                            mainTexture = texture;
                    }
                    else
                    {
                        if (!companion.SharedPages.TryGetValue(tileset.Id, out var guids) || guids.Length != pageCount)
                            throw new InvalidDataException($"Shared texture page list is invalid for tileset {tileset.Id}.");
                        var path = AssetDatabase.GUIDToAssetPath(guids[pageIndex]);
                        if (string.IsNullOrEmpty(path))
                            throw new IOException($"A shared tileset page is missing for tileset {tileset.Id}.");
                        context.DependsOnArtifact(path);
                        texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        if (!texture || texture.width != page.Width || texture.height != page.Height)
                            throw new InvalidDataException($"Shared tileset texture '{path}' cannot be used for this page.");
                    }

                    for (var tileIndex = page.FirstTile; tileIndex < page.EndTile; tileIndex++)
                    {
                        var (x, y) = page.GetTileOrigin(tileIndex);
                        var size = tileset.TileSize;
                        var sprite = Sprite.Create(texture, new Rect(x, y, size, size),
                            new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
                        var tileName = TileName(tileset.Id, tileIndex);
                        sprite.name = "S_" + tileName.Substring(2);
                        var tile = ScriptableObject.CreateInstance<Tile>();
                        tile.name = tileName;
                        tile.sprite = sprite;
                        tile.colliderType = Tile.ColliderType.None;

                        context.AddObjectToAsset(sprite.name, sprite);
                        context.AddObjectToAsset(tileName, tile);
                        if (solidTiles.Contains((tileset.Id, tileIndex)))
                        {
                            var solidTile = ScriptableObject.CreateInstance<Tile>();
                            solidTile.name = TileName(tileset.Id, tileIndex, true);
                            solidTile.sprite = sprite;
                            solidTile.colliderType = Tile.ColliderType.Grid;
                            context.AddObjectToAsset(solidTile.name, solidTile);
                        }
                    }
                }
            }
            if (companion.SharedPages != null)
            {
                var descriptor = new TextAsset("This map references shared Aseprite tileset textures.")
                {
                    name = Path.GetFileNameWithoutExtension(context.assetPath)
                };
                context.AddObjectToAsset("SharedTilesetDescriptor", descriptor);
                context.SetMainObject(descriptor);
                return;
            }
            if (mainTexture == null)
                throw new InvalidDataException("The map contains no embedded tileset.");
            context.SetMainObject(mainTexture);
        }
    }
}
