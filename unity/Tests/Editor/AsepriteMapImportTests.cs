using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace AsepriteTilemapEditor.Tests
{
    public sealed class AsepriteMapImportTests
    {
        private const string Fixtures = "Packages/com.ash2ops.aseprite-tilemap-editor/Tests/Editor/Fixtures";
        private string assetFolder;
        private string sourceFolder;
        private string sourcePath;
        private string prefabPath;
        private string artPath;
        private readonly HashSet<string> createdSharedTextures = new();

        [SetUp]
        public void CreateScratchPaths()
        {
            var pathName = "AsepriteMapImportTests" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", pathName);
            assetFolder = "Assets/" + pathName;
            sourceFolder = Directory.CreateDirectory(Path.Combine("Temp", pathName)).FullName;
            sourcePath = Path.Combine(sourceFolder, "probe.aseprite");
            prefabPath = assetFolder + "/PF_Probe.prefab";
            artPath = assetFolder + "/T_ProbeTiles.asepritemap";
        }

        [TearDown]
        public void DeleteScratchPaths()
        {
            AssetDatabase.DeleteAsset(assetFolder);
            foreach (var path in createdSharedTextures)
                if (File.Exists(path))
                    AssetDatabase.DeleteAsset(path);
            createdSharedTextures.Clear();
            File.Delete(sourcePath);
            Directory.Delete(sourceFolder);
        }

        [Test]
        public void SamePathImportUpdatesCellsAndOrderWithoutDuplicatingAssets()
        {
            var prefab = ImportFixture("AsepriteMapInitial");
            var originalPrefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            var originalAtlasGuid = AssetDatabase.AssetPathToGUID(artPath);
            Assert.That(prefab.GetComponent<Grid>().cellSize, Is.EqualTo(new Vector3(1, 1, 0)));
            var ground = GetLayer(prefab, "Ground");
            var details = GetLayer(prefab, "Details");
            var groundObjectId = LocalId(ground.gameObject);
            var originalTile = ground.GetTile(new Vector3Int(3, 0));
            var originalTileId = LocalId(originalTile);
            var originalSpriteId = LocalId(((Tile)originalTile).sprite);
            var expectedCellBounds = new BoundsInt(0, 0, 0, 5, 4, 1);
            Assert.That(ground.cellBounds, Is.EqualTo(expectedCellBounds));
            Assert.That(ground.GetTile(new Vector3Int(1, 2)).name, Is.EqualTo("T_Tileset0Tile1"));
            Assert.That(ground.GetTile(new Vector3Int(2, 1)).name, Is.EqualTo("T_Tileset0Tile3"));
            Assert.That(details.GetTile(new Vector3Int(2, 1)).name, Is.EqualTo("T_Tileset0Tile2"));
            Assert.That(details.GetTile(new Vector3Int(4, 2)).name, Is.EqualTo("T_Tileset0Tile3"));
            Assert.That(details.GetComponent<TilemapRenderer>().sortingOrder,
                Is.GreaterThan(ground.GetComponent<TilemapRenderer>().sortingOrder));
            Assert.That(GetHierarchyNames(prefab), Is.EqualTo(new[] { "Ground", "Details" }));
            AssertArt();

            prefab = ImportFixture("AsepriteMapReordered");
            ground = GetLayer(prefab, "Ground");
            details = GetLayer(prefab, "Details");
            Assert.That(GetHierarchyNames(prefab), Is.EqualTo(new[] { "Details", "Ground" }));
            Assert.That(ground.GetComponent<TilemapRenderer>().sortingOrder,
                Is.GreaterThan(details.GetComponent<TilemapRenderer>().sortingOrder));
            Assert.That(ground.GetTile(new Vector3Int(1, 2)), Is.Null);
            Assert.That(ground.GetTile(new Vector3Int(2, 1)).name, Is.EqualTo("T_Tileset0Tile3"));
            Assert.That(details.GetTile(new Vector3Int(2, 1)).name, Is.EqualTo("T_Tileset0Tile2"));
            Assert.That(ground.cellBounds, Is.EqualTo(expectedCellBounds));
            Assert.That(LocalId(ground.gameObject), Is.EqualTo(groundObjectId));
            Assert.That(LocalId(ground.GetTile(new Vector3Int(3, 0))), Is.EqualTo(originalTileId));
            Assert.That(LocalId(((Tile)ground.GetTile(new Vector3Int(3, 0))).sprite), Is.EqualTo(originalSpriteId));
            AssertArt();

            prefab = ImportFixture("AsepriteMapLayerRemoved");
            Assert.That(GetHierarchyNames(prefab), Is.EqualTo(new[] { "Ground" }));
            Assert.That(LocalId(GetLayer(prefab, "Ground").gameObject), Is.EqualTo(groundObjectId));
            Assert.That(AssetDatabase.AssetPathToGUID(prefabPath), Is.EqualTo(originalPrefabGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(artPath), Is.EqualTo(originalAtlasGuid));
            AssertArt();
            Assert.That(Directory.GetFiles(assetFolder).Where(file => !file.EndsWith(".meta")).Count(), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator ProjectReimportKeepsTheLinkedPrefabAndSeparatesSourcesWithTheSameName()
        {
            var settings = AsepriteMapSettings.Read();
            if (!settings.autoImport || !settings.IncludesSource(assetFolder + "/probe.aseprite"))
                Assert.Ignore("This project's automatic import settings exclude the scratch source.");
            var output = settings.outputFolder;
            var name = "MapSync" + Guid.NewGuid().ToString("N");
            var source = $"{assetFolder}/{name}.aseprite";
            var duplicate = $"{assetFolder}/{name}.ase";
            var renamed = $"{assetFolder}/{name}Renamed.aseprite";
            var legacy = $"{assetFolder}/PF_{name}.prefab";
            var legacyArt = $"{assetFolder}/T_{name}Tiles.asepritemap";
            var destination = $"{output}/PF_{name}.prefab";
            var art = $"{output}/T_{name}Tiles.asepritemap";
            var alternate = $"{output}/PF_{name}_2.prefab";
            var alternateArt = $"{output}/T_{name}_2Tiles.asepritemap";
            var erroneousDuplicate = $"{output}/PF_{name}Renamed.prefab";
            var initial = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Fixtures}/AsepriteMapInitial.bytes").bytes;
            var reordered = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Fixtures}/AsepriteMapReordered.bytes").bytes;
            try
            {
                File.WriteAllBytes(source, initial);
                TrackSharedTextures(initial);
                TrackSharedTextures(reordered);
                AsepriteMapImporter.Import(source, legacy);
                var prefabGuid = AssetDatabase.AssetPathToGUID(legacy);
                var artGuid = AssetDatabase.AssetPathToGUID(legacyArt);
                AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                yield return WaitForImport(() => AssetDatabase.GUIDToAssetPath(prefabGuid) == destination);
                Assert.That(AssetDatabase.GUIDToAssetPath(artGuid), Is.EqualTo(art));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(legacy), Is.Null);

                File.WriteAllBytes(duplicate, initial);
                AssetDatabase.ImportAsset(duplicate, ImportAssetOptions.ForceSynchronousImport);
                yield return WaitForImport(() => AssetDatabase.LoadAssetAtPath<GameObject>(alternate));

                Assert.That(AssetDatabase.MoveAsset(source, renamed), Is.Empty);
                File.WriteAllBytes(renamed, reordered);
                AssetDatabase.ImportAsset(renamed, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                yield return WaitForImport(() => GetHierarchyNames(AssetDatabase.LoadAssetAtPath<GameObject>(destination))
                    .SequenceEqual(new[] { "Details", "Ground" }));
                Assert.That(AssetDatabase.AssetPathToGUID(destination), Is.EqualTo(prefabGuid));
                Assert.That(AssetDatabase.AssetPathToGUID(art), Is.EqualTo(artGuid));
                Assert.That(GetHierarchyNames(AssetDatabase.LoadAssetAtPath<GameObject>(alternate)),
                    Is.EqualTo(new[] { "Ground", "Details" }));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(erroneousDuplicate), Is.Null);
            }
            finally
            {
                foreach (var path in new[] { destination, art, alternate, alternateArt, erroneousDuplicate,
                             $"{output}/T_{name}RenamedTiles.asepritemap" })
                    if (File.Exists(path))
                        AssetDatabase.DeleteAsset(path);
            }
        }

        private static IEnumerator WaitForImport(Func<bool> condition)
        {
            var deadline = EditorApplication.timeSinceStartup + 12;
            while (!condition() && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(condition(), Is.True, "The automatic Aseprite import did not finish.");
        }

        [Test]
        public void GroupedTilemapRetainsHierarchyAndCanMoveOutOfADeletedGroup()
        {
            var prefab = ImportFixture("AsepriteMapGrouped");
            var scenery = prefab.transform.Find("Scenery");
            Assert.That(scenery.gameObject.activeSelf, Is.False);
            Assert.That(scenery.Find("Details").GetComponent<Tilemap>(), Is.Not.Null);
            Assert.That(scenery.Find("Details").GetComponent<TilemapRenderer>().sortingOrder,
                Is.GreaterThan(GetLayer(prefab, "Ground").GetComponent<TilemapRenderer>().sortingOrder));

            prefab = ImportFixture("AsepriteMapInitial");
            Assert.That(prefab.transform.Find("Scenery"), Is.Null);
            Assert.That(GetLayer(prefab, "Details").gameObject.activeSelf, Is.True);
            Assert.That(GetLayer(prefab, "Details").GetTile(new Vector3Int(2, 1)).name, Is.EqualTo("T_Tileset0Tile2"));
        }

        [Test]
        public void ImportAppliesSolidOnlyToTaggedLayersAndUpdatesItOnReimport()
        {
            var prefab = ImportFixture("AsepriteMapSolid", physics: true);
            var ground = GetLayer(prefab, "Ground");
            var details = GetLayer(prefab, "Details");
            var cell = new Vector3Int(1, 2);
            Assert.That(ground.GetComponent<TilemapCollider2D>().enabled, Is.True);
            Assert.That(ground.GetComponent<TilemapCollider2D>().isTrigger, Is.False);
            Assert.That(details.GetComponent<TilemapCollider2D>(), Is.Null);
            Assert.That(ground.GetColliderType(cell), Is.EqualTo(Tile.ColliderType.Grid));
            Assert.That(details.GetColliderType(cell), Is.EqualTo(Tile.ColliderType.None));
            Assert.That(((Tile)ground.GetTile(cell)).sprite, Is.SameAs(((Tile)details.GetTile(cell)).sprite));

            prefab = ImportFixture("AsepriteMapSolidSwapped", physics: true);
            ground = GetLayer(prefab, "Ground");
            details = GetLayer(prefab, "Details");
            Assert.That(ground.GetComponent<TilemapCollider2D>().enabled, Is.False);
            Assert.That(details.GetComponent<TilemapCollider2D>().enabled, Is.True);
            Assert.That(ground.GetColliderType(cell), Is.EqualTo(Tile.ColliderType.None));
            Assert.That(details.GetColliderType(cell), Is.EqualTo(Tile.ColliderType.Grid));
        }

        [Test]
        public void ImportsSixteenPixelTilesAtOneUnitWithLayerCollision()
        {
            var prefab = ImportFixture("AsepriteMap16");
            var ground = GetLayer(prefab, "Ground");
            var details = GetLayer(prefab, "Details");
            var cell = new Vector3Int(1, 2);
            Assert.That(prefab.GetComponent<Grid>().cellSize.x, Is.EqualTo(1));
            Assert.That(((Tile)ground.GetTile(cell)).sprite.pixelsPerUnit, Is.EqualTo(16));
            Assert.That(((Tile)details.GetTile(cell)).sprite.rect.size, Is.EqualTo(new Vector2(16, 16)));
            Assert.That(ground.GetColliderType(cell), Is.EqualTo(Tile.ColliderType.Grid));
            Assert.That(details.GetColliderType(cell), Is.EqualTo(Tile.ColliderType.None));
        }

        [Test]
        public void MapsShareTexturesAndKeepTheirArtWhenAnotherMapChanges()
        {
            var first = ImportFixture("AsepriteMapInitial");
            var shared = GroundTexture(first);
            var sharedPath = AssetDatabase.GetAssetPath(shared);
            var originalBytes = File.ReadAllBytes(sharedPath);
            var secondPrefab = $"{assetFolder}/PF_Other.prefab";
            var reordered = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Fixtures}/AsepriteMapReordered.bytes").bytes;
            File.WriteAllBytes(sourcePath, reordered);
            var second = AsepriteMapImporter.Import(sourcePath, secondPrefab);
            Assert.That(GroundTexture(second), Is.SameAs(shared));

            first = ImportFixture("AsepriteMapChangedArt");
            Assert.That(GroundTexture(first), Is.Not.SameAs(shared));
            Assert.That(GroundTexture(AssetDatabase.LoadAssetAtPath<GameObject>(secondPrefab)), Is.SameAs(shared));
            Assert.That(File.ReadAllBytes(sharedPath), Is.EqualTo(originalBytes));
        }

        [Test]
        public void MigratingLegacyArtKeepsExistingTileAndSpriteReferences()
        {
            var prefab = ImportFixture("AsepriteMapInitial");
            var initial = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Fixtures}/AsepriteMapInitial.bytes").bytes;
            File.WriteAllBytes(artPath, initial);
            AssetDatabase.ImportAsset(artPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(artPath), Is.TypeOf<Texture2D>());
            var artGuid = AssetDatabase.AssetPathToGUID(artPath);
            var cell = new Vector3Int(2, 1, 0);
            var previousTile = (Tile)GetLayer(prefab, "Ground").GetTile(cell);
            var tileId = LocalId(previousTile);
            var spriteId = LocalId(previousTile.sprite);

            prefab = ImportFixture("AsepriteMapInitial");
            var currentTile = (Tile)GetLayer(prefab, "Ground").GetTile(cell);
            Assert.That(AssetDatabase.AssetPathToGUID(artPath), Is.EqualTo(artGuid));
            Assert.That(LocalId(currentTile), Is.EqualTo(tileId));
            Assert.That(LocalId(currentTile.sprite), Is.EqualTo(spriteId));
            Assert.That(AssetDatabase.GetAssetPath(currentTile.sprite.texture), Is.Not.EqualTo(artPath));
        }

        [Test]
        public void ReexportedStampsCollideOnlyAtPaintedAnchors()
        {
            var previous = ImportFixture("AsepriteMap16", physics: true);
            var anchor = new Vector3Int(1, 2, 0);
            Assert.That(GetLayer(previous, "Ground").GetColliderType(anchor), Is.EqualTo(Tile.ColliderType.Grid));
            var prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var position = 0;
                foreach (var layerName in new[] { "Ground", "Details" })
                {
                    var prop = new GameObject("Visual");
                    SceneManager.MoveGameObjectToScene(prop, contents.scene);
                    prop.transform.SetParent(contents.transform.Find(layerName), false);
                    prop.transform.localPosition = new Vector3(++position, 0, 0);
                }
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            previous = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var groundProp = LocalId(previous.transform.Find("Ground/Visual").gameObject);
            var detailsProp = LocalId(previous.transform.Find("Details/Visual").gameObject);

            var prefab = ImportFixture("AsepriteMapAnchorCollision", physics: true);
            Assert.That(AssetDatabase.AssetPathToGUID(prefabPath), Is.EqualTo(prefabGuid));
            var ground = GetLayer(prefab, "Ground");
            var visualCells = new[] { new Vector3Int(1, 3, 0), new Vector3Int(2, 3, 0) };
            foreach (var cell in visualCells)
            {
                Assert.That(ground.HasTile(cell), Is.True);
                Assert.That(ground.GetColliderType(cell), Is.EqualTo(Tile.ColliderType.None));
            }
            Assert.That(((Tile)ground.GetTile(anchor)).sprite, Is.Null);
            Assert.That(ground.GetComponent<TilemapCollider2D>().enabled, Is.True);
            Assert.That(ground.GetColliderType(anchor), Is.EqualTo(Tile.ColliderType.Grid));
            var visibleAnchor = new Vector3Int(3, 2, 0);
            Assert.That(((Tile)ground.GetTile(visibleAnchor)).sprite, Is.Not.Null);
            Assert.That(ground.GetColliderType(visibleAnchor), Is.EqualTo(Tile.ColliderType.Grid));
            var count = 0;
            foreach (var cell in ground.cellBounds.allPositionsWithin)
                if (ground.GetColliderType(cell) != Tile.ColliderType.None)
                    count++;
            Assert.That(count, Is.EqualTo(2));
            Assert.That(prefab.GetComponentsInChildren<Tilemap>(true).Length, Is.EqualTo(2));
            Assert.That(prefab.transform.Find("Ground/Visual").localPosition.x, Is.EqualTo(1));
            Assert.That(prefab.transform.Find("Details/Visual").localPosition.x, Is.EqualTo(2));
            Assert.That(LocalId(prefab.transform.Find("Ground/Visual").gameObject), Is.EqualTo(groundProp));
            Assert.That(LocalId(prefab.transform.Find("Details/Visual").gameObject), Is.EqualTo(detailsProp));
        }

        [Test]
        public void GridImportsKeepSolidCellsAndRestorePhysicsOnPolicyChange()
        {
            var source = $"{assetFolder}/GridPolicy.aseprite";
            var input = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Fixtures}/AsepriteMapAnchorCollision.bytes").bytes;
            TrackSharedTextures(input);
            File.WriteAllBytes(source, input);
            AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceSynchronousImport);
            var settings = AsepriteMapSettings.Read();
            settings.outputFolder = assetFolder + "/PolicyMaps";
            settings.collisionMode = "Grid";
            settings.renderRules = new[]
            {
                new AsepriteMapRenderRule { layer = "Ground", role = "Static" },
                new AsepriteMapRenderRule { layer = "Details", role = "DepthSorted", includeOverlaps = true,
                    overrideSortingOrder = true, sortingOrder = 9 }
            };
            Assert.That(settings.RenderRule("Details · Overlap 2")?.role, Is.EqualTo("DepthSorted"));

            var prefab = AsepriteMapAutoImport.Sync(source, settings, out var deferred);
            Assert.That(deferred, Is.False);
            var anchor = new Vector3Int(1, 2);
            var art = new Vector3Int(1, 3);
            var ground = GetLayer(prefab, "Ground");
            var details = GetLayer(prefab, "Details");
            var groundId = LocalId(ground);
            var detailsId = LocalId(details);
            Assert.That(ground.GetComponent<TilemapCollider2D>(), Is.Null);
            Assert.That(ground.GetColliderType(anchor), Is.EqualTo(Tile.ColliderType.Grid));
            Assert.That(ground.GetColliderType(art), Is.EqualTo(Tile.ColliderType.None));
            Assert.That(ground.GetComponent<TilemapRenderer>().mode, Is.EqualTo(TilemapRenderer.Mode.Chunk));
            Assert.That(details.GetComponent<TilemapRenderer>().mode, Is.EqualTo(TilemapRenderer.Mode.Individual));
            Assert.That(details.GetComponent<TilemapRenderer>().sortingOrder, Is.EqualTo(9));

            settings.collisionMode = "Physics2D";
            prefab = AsepriteMapAutoImport.Sync(source, settings, out _);
            ground = GetLayer(prefab, "Ground");
            Assert.That(ground.GetComponent<TilemapCollider2D>().enabled, Is.True);

            settings.collisionMode = "Grid";
            prefab = AsepriteMapAutoImport.Sync(source, settings, out _);
            ground = GetLayer(prefab, "Ground");
            Assert.That(ground.GetComponent<TilemapCollider2D>().enabled, Is.False);
            Assert.That(ground.GetColliderType(anchor), Is.EqualTo(Tile.ColliderType.Grid));

            settings.collisionMode = "Physics2D";
            prefab = AsepriteMapAutoImport.Sync(source, settings, out _);
            ground = GetLayer(prefab, "Ground");
            details = GetLayer(prefab, "Details");
            Assert.That(ground.GetComponent<TilemapCollider2D>().enabled, Is.True);
            Assert.That(LocalId(ground), Is.EqualTo(groundId));
            Assert.That(LocalId(details), Is.EqualTo(detailsId));
            Assert.That(GetHierarchyNames(prefab), Is.EqualTo(new[] { "Ground", "Details" }));
            Assert.That(details.GetComponent<TilemapRenderer>().sortingOrder, Is.EqualTo(9));

            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                GetLayer(contents, "Ground").GetComponent<TilemapCollider2D>().enabled = false;
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            settings.collisionMode = "Grid";
            AsepriteMapAutoImport.Sync(source, settings, out _);
            settings.collisionMode = "Physics2D";
            prefab = AsepriteMapAutoImport.Sync(source, settings, out _);
            Assert.That(GetLayer(prefab, "Ground").GetComponent<TilemapCollider2D>().enabled, Is.False);
        }

        [Test]
        public void MalformedSourceLeavesThePreviousPrefabAndArtIntact()
        {
            ImportFixture("AsepriteMapInitial");
            var prefabBefore = File.ReadAllBytes(prefabPath);
            var artBefore = File.ReadAllBytes(artPath);
            File.WriteAllBytes(sourcePath, new byte[42]);
            Assert.Throws<InvalidDataException>(() => AsepriteMapImporter.Import(sourcePath, prefabPath));
            Assert.That(File.ReadAllBytes(prefabPath), Is.EqualTo(prefabBefore));
            Assert.That(File.ReadAllBytes(artPath), Is.EqualTo(artBefore));
        }

        private GameObject ImportFixture(string name, bool physics = false)
        {
            var fixture = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Fixtures}/{name}.bytes");
            Assert.That(fixture, Is.Not.Null, $"Missing test fixture: {name}");
            TrackSharedTextures(fixture.bytes);
            File.WriteAllBytes(sourcePath, fixture.bytes);
            if (physics)
            {
                var settings = AsepriteMapSettings.Read();
                settings.collisionMode = "Physics2D";
                return AsepriteMapImporter.ImportPrepared(fixture.bytes, AsepriteMapDocument.Read(fixture.bytes), prefabPath, settings);
            }
            return AsepriteMapImporter.Import(sourcePath, prefabPath);
        }

        private void AssertArt()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(artPath);
            Assert.That(assets.OfType<Texture2D>(), Is.Empty);
            Assert.That(assets.OfType<Tile>().Count(), Is.EqualTo(4));
            var sprites = assets.OfType<Sprite>().ToArray();
            Assert.That(sprites.Length, Is.EqualTo(4));
            Assert.That(sprites.All(sprite => sprite.rect.size == new Vector2(144, 144) && sprite.pixelsPerUnit == 144), Is.True);
        }

        private void TrackSharedTextures(byte[] input)
        {
            var folder = AsepriteMapSettings.Read().sharedTilesetFolder;
            foreach (var tileset in AsepriteMapDocument.Read(input).Tilesets.Values)
            foreach (var path in AsepriteMapSharedTextures.PagePaths(tileset, folder))
                if (!File.Exists(path))
                    createdSharedTextures.Add(path);
        }

        private static Texture2D GroundTexture(GameObject prefab) =>
            ((Tile)GetLayer(prefab, "Ground").GetTile(new Vector3Int(2, 1, 0))).sprite.texture;

        private static long LocalId(UnityEngine.Object asset)
        {
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long localId), Is.True);
            return localId;
        }

        private static Tilemap GetLayer(GameObject prefab, string name) =>
            prefab.transform.Find(name).GetComponent<Tilemap>();

        private static string[] GetHierarchyNames(GameObject prefab) =>
            prefab.transform.Cast<Transform>().Select(child => child.name).ToArray();
    }
}
