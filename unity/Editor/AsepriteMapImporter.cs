using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace AsepriteTilemapEditor
{
    public static class AsepriteMapImporter
    {
        private const string MenuPath = "Tools/Aseprite Tilemap Editor/Import Aseprite Map";
        private const string AssetMenuPath = "Assets/Import Aseprite Map";
        internal const string OwnerLabelPrefix = "AsepriteMap_";
        private const ImportAssetOptions ImportNow = ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport;

        [MenuItem(MenuPath)]
        private static void ShowImport()
        {
            var sourcePath = EditorUtility.OpenFilePanelWithFilters("Import Aseprite Map", "", new[] { "Aseprite maps", "aseprite,ase" });
            if (string.IsNullOrEmpty(sourcePath))
                return;

            var assetPath = FileUtil.GetProjectRelativePath(sourcePath);
            if (assetPath?.StartsWith("Assets/", StringComparison.Ordinal) == true)
                ImportProjectMap(assetPath);
            else
                ShowPrefabDestination(sourcePath);
        }

        [MenuItem(AssetMenuPath, false, 120)]
        private static void ImportSelected()
        {
            var assetPath = GetSelectedAsepritePath();
            if (assetPath != null)
                ImportProjectMap(assetPath);
        }

        [MenuItem(AssetMenuPath, true, 120)]
        private static bool CanImportSelected() => CanImport() && GetSelectedAsepritePath() != null;

        private static string GetSelectedAsepritePath()
        {
            if (Selection.objects.Length != 1 || !AssetDatabase.IsMainAsset(Selection.activeObject))
                return null;

            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return path.StartsWith("Assets/", StringComparison.Ordinal) && IsAsepritePath(path) &&
                   !AssetDatabase.IsValidFolder(path) ? path : null;
        }

        private static void ImportProjectMap(string assetPath)
        {
            try
            {
                var prefab = AsepriteMapAutoImport.Sync(assetPath, out var deferred);
                if (deferred)
                    AsepriteMapAutoImport.Queue(assetPath);
                else if (prefab)
                {
                    Selection.activeObject = prefab;
                    EditorGUIUtility.PingObject(prefab);
                }
                else
                    EditorUtility.DisplayDialog("Import Aseprite Map", "Select an exported Aseprite Tilemap.", "Close");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorUtility.DisplayDialog("Aseprite Map Import Failed", error.Message, "Close");
            }
        }

        internal static string SuggestedPrefabName(string sourcePath)
        {
            var filename = Path.GetFileNameWithoutExtension(sourcePath);
            var stem = filename.StartsWith("PF_", StringComparison.Ordinal) ? filename.Substring(3) : filename;
            var name = string.Concat(stem.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
            return name.Length > 0 ? "PF_" + name : "PF_AsepriteMap";
        }

        private static void ShowPrefabDestination(string sourcePath)
        {
            var filename = Path.GetFileNameWithoutExtension(sourcePath);
            var prefabPath = EditorUtility.SaveFilePanelInProject("Save Aseprite Map Prefab", SuggestedPrefabName(sourcePath),
                "prefab", "Choose a prefab path. To update an existing imported map, choose its existing prefab.", "Assets");
            if (string.IsNullOrEmpty(prefabPath))
                return;

            try
            {
                var prefab = Import(sourcePath, prefabPath);
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Debug.Log($"Imported Aseprite map '{filename}' into {prefabPath}.", prefab);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorUtility.DisplayDialog("Aseprite Map Import Failed", error.Message, "Close");
            }
        }

        [MenuItem(MenuPath, true)]
        internal static bool CanImport() =>
            !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

        internal static bool IsAsepritePath(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".aseprite", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".ase", StringComparison.OrdinalIgnoreCase);
        }

        public static GameObject Import(string sourcePath, string prefabPath)
        {
            if (!CanImport())
                throw new InvalidOperationException("Import Aseprite maps in Edit Mode after compilation finishes.");

            if (!IsAsepritePath(sourcePath))
                throw new ArgumentException("Select an .aseprite or .ase file.", nameof(sourcePath));

            var input = AsepriteMapDocument.ReadFile(sourcePath);
            return ImportPrepared(input, AsepriteMapDocument.Read(input), prefabPath);
        }

        internal static string ArtPath(string prefabPath)
        {
            var name = Path.GetFileNameWithoutExtension(prefabPath);
            var stem = name.StartsWith("PF_", StringComparison.Ordinal) ? name.Substring(3) : name;
            return Path.GetDirectoryName(prefabPath).Replace('\\', '/') + $"/T_{stem}Tiles.{AsepriteMapArtImporter.Extension}";
        }

        internal static string ProjectFilePath(string assetPath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);

        internal static GameObject ImportPrepared(byte[] input, AsepriteMapDocument map, string prefabPath,
            AsepriteMapSettings settings = null)
        {
            settings ??= AsepriteMapSettings.Read();
            var fullPrefabPath = GetPrefabFilePath(prefabPath);
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == prefabPath)
                throw new InvalidOperationException("Close the map's Prefab Mode before updating it.");

            var prefabName = Path.GetFileNameWithoutExtension(prefabPath);
            var artPath = ArtPath(prefabPath);
            var fullArtPath = ProjectFilePath(artPath);

            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var existingArtGuid = AssetDatabase.AssetPathToGUID(artPath, AssetPathToGUIDOptions.OnlyExistingAssets);
            var ownerLabel = OwnerLabelPrefix + existingArtGuid;
            var isUpdate = existingPrefab != null;
            if (isUpdate)
            {
                if (string.IsNullOrEmpty(existingArtGuid) ||
                    AssetImporter.GetAtPath(artPath) is not AsepriteMapArtImporter ||
                    !AssetDatabase.GetLabels(existingPrefab).Contains(ownerLabel))
                    throw new InvalidOperationException("The chosen prefab is not paired with this Aseprite map asset.");
                if (!AssetDatabase.IsOpenForEdit(prefabPath) || !AssetDatabase.IsOpenForEdit(artPath))
                    throw new InvalidOperationException("The generated prefab and art asset must be writable.");
            }
            else if (File.Exists(fullPrefabPath) || File.Exists(fullArtPath) || !string.IsNullOrEmpty(existingArtGuid))
            {
                throw new IOException("An asset already uses the chosen output path. Choose a different prefab name.");
            }

            var previousPrefabBytes = isUpdate ? File.ReadAllBytes(fullPrefabPath) : null;
            var previousArtBytes = isUpdate ? AsepriteMapCompanion.ReadFile(fullArtPath) : null;
            var previous = isUpdate ? AsepriteMapCompanion.Decode(previousArtBytes) : null;
            var priorMap = previous == null ? null : AsepriteMapDocument.Read(previous.Source);
            var artTouched = false;
            var prefabTouched = false;
            var preview = default(Scene);
            GameObject root = null;
            try
            {
                if (isUpdate)
                    root = PrefabUtility.LoadPrefabContents(prefabPath);
                else
                {
                    preview = EditorSceneManager.NewPreviewScene();
                    root = CreateObject(prefabName, preview);
                }

                var layers = ReconcileLayers(root, map, priorMap);
                var manualOverrides = GetManuallyDisabledColliders(map, priorMap, previous, layers);
                var sharedPages = AsepriteMapSharedTextures.EnsurePages(map, settings.sharedTilesetFolder);
                artTouched = true;
                AsepriteMapCompanion.Write(fullArtPath, input, sharedPages, settings.ImportPolicyKey(), settings.GridOnly,
                    settings.GridOnly ? manualOverrides.OrderBy(key => key, StringComparer.Ordinal).ToArray() : Array.Empty<string>());
                AssetDatabase.ImportAsset(artPath, ImportNow);
                if (AssetDatabase.LoadMainAssetAtPath(artPath) is not TextAsset)
                    throw new IOException("Unity failed to import the Aseprite map art asset.");

                var tiles = AssetDatabase.LoadAllAssetsAtPath(artPath).OfType<Tile>()
                    .ToDictionary(tile => tile.name, StringComparer.Ordinal);
                if (tiles.Values.Count(tile => tile.colliderType == Tile.ColliderType.None) !=
                    map.Tilesets.Values.Sum(tileset => tileset.TileCount - 1))
                    throw new IOException("Unity did not import all tiles from the Aseprite map.");
                ConfigureGrid(root, map, priorMap, layers, tiles, settings, previous?.GridOnly == true, manualOverrides);
                prefabTouched = true;
                var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var success);
                if (!success || saved == null)
                    throw new IOException("Unity failed to save the Aseprite map prefab.");

                if (!isUpdate)
                {
                    ownerLabel = OwnerLabelPrefix + AssetDatabase.AssetPathToGUID(artPath);
                    AssetDatabase.SetLabels(saved, new[] { ownerLabel });
                }
                return saved;
            }
            catch (Exception importError)
            {
                var errors = new List<Exception> { importError };
                if (artTouched)
                    RestoreAsset(artPath, fullArtPath, previousArtBytes, errors);
                if (prefabTouched)
                    RestoreAsset(prefabPath, fullPrefabPath, previousPrefabBytes, errors);
                if (errors.Count > 1)
                    throw new AggregateException("The import failed and Unity could not restore all output files.", errors);
                throw;
            }
            finally
            {
                if (isUpdate && root != null)
                    PrefabUtility.UnloadPrefabContents(root);
                if (preview.IsValid())
                    EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static string GetPrefabFilePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                assetPath.Contains('\\') || assetPath.Split('/').Any(part => part is "" or "." or "..") ||
                !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose an existing folder under Assets and a .prefab file name.", nameof(assetPath));

            if (!AssetDatabase.IsValidFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/')))
                throw new DirectoryNotFoundException("Choose an existing folder under Assets for the prefab.");
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
        }

        private static GameObject CreateObject(string name, Scene scene)
        {
            var created = new GameObject(name);
            if (created.scene != scene)
                SceneManager.MoveGameObjectToScene(created, scene);
            return created;
        }

        private static Transform[] ReconcileLayers(GameObject root, AsepriteMapDocument map, AsepriteMapDocument priorMap)
        {
            var existing = root.GetComponentsInChildren<Transform>(true);
            var existingByName = existing.ToLookup(node => node.name, StringComparer.Ordinal);

            var priorNodes = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var priorLayers = priorMap?.Layers;
            if (priorLayers != null)
            {
                var priorNames = GetLayerNames(priorLayers, root.name);
                for (var index = 0; index < priorLayers.Count; index++)
                {
                    var candidates = existingByName[priorNames[index]].ToArray();
                    if (candidates.Length != 1 ||
                        priorLayers[index].IsGroup == (candidates[0].GetComponent<Tilemap>() != null))
                        throw new InvalidOperationException($"The generated layer '{priorNames[index]}' is missing or ambiguous in the prefab.");
                    priorNodes.Add(priorLayers[index].Key, candidates[0]);
                }
            }

            var names = GetLayerNames(map.Layers, root.name);
            var result = new Transform[map.Layers.Count];
            var retained = new HashSet<string>(map.Layers.Select(layer => layer.Key), StringComparer.Ordinal);
            var managedNodes = new HashSet<Transform>(priorNodes.Values);
            var unmanagedNames = existing.Where(node => !managedNodes.Contains(node))
                .Select(node => node.name).ToHashSet(StringComparer.Ordinal);
            if (names.Any(unmanagedNames.Contains))
                throw new InvalidOperationException("An imported layer would use the name of an existing prefab object.");
            if (priorLayers != null)
            {
                foreach (var oldLayer in priorLayers)
                {
                    if (retained.Contains(oldLayer.Key))
                        continue;
                    foreach (Transform child in priorNodes[oldLayer.Key])
                    {
                        if (!managedNodes.Contains(child))
                            throw new InvalidOperationException($"Removing '{oldLayer.Name}' would remove an object added in Unity.");
                    }
                }
            }

            // Detach changed managed parents first so that a hierarchy reversal cannot form a cycle.
            for (var index = 0; index < map.Layers.Count; index++)
            {
                var layer = map.Layers[index];
                if (priorNodes.TryGetValue(layer.Key, out var node))
                {
                    if (layer.IsGroup == (node.GetComponent<Tilemap>() != null))
                        throw new InvalidDataException($"Layer '{layer.Name}' changed type while keeping its UUID.");
                    result[index] = node;
                    var expectedParent = layer.ParentIndex == -1 ? root.transform :
                        priorNodes.GetValueOrDefault(map.Layers[layer.ParentIndex].Key);
                    if (node.parent != expectedParent)
                        node.SetParent(root.transform, false);
                }
                else
                    result[index] = CreateObject(names[index], root.scene).transform;
            }

            var nextSibling = new Dictionary<Transform, int>();
            for (var index = 0; index < map.Layers.Count; index++)
            {
                var layer = map.Layers[index];
                var node = result[index];
                var parent = layer.ParentIndex == -1 ? root.transform : result[layer.ParentIndex];
                if (node.parent != parent)
                    node.SetParent(parent, false);
                node.name = names[index];
                node.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                node.localScale = Vector3.one;
                nextSibling.TryGetValue(parent, out var sibling);
                node.SetSiblingIndex(sibling);
                nextSibling[parent] = sibling + 1;
            }

            if (priorLayers != null)
            {
                for (var index = priorLayers.Count - 1; index >= 0; index--)
                {
                    var oldLayer = priorLayers[index];
                    if (!retained.Contains(oldLayer.Key))
                        Object.DestroyImmediate(priorNodes[oldLayer.Key].gameObject);
                }
            }
            return result;
        }

        private static string[] GetLayerNames(IReadOnlyList<AsepriteMapLayer> layers, string rootName)
        {
            var names = new string[layers.Count];
            var used = new HashSet<string>(StringComparer.Ordinal) { rootName };
            foreach (var index in Enumerable.Range(0, layers.Count).OrderBy(i => layers[i].Key, StringComparer.Ordinal))
            {
                var name = layers[index].Name;
                var suffix = 2;
                while (!used.Add(name))
                    name = $"{layers[index].Name} ({suffix++})";
                names[index] = name;
            }
            return names;
        }

        private static HashSet<string> GetManuallyDisabledColliders(AsepriteMapDocument map, AsepriteMapDocument priorMap,
            AsepriteMapCompanion previous, Transform[] layers)
        {
            if (previous?.GridOnly == true)
                return previous.ManuallyDisabledColliders;

            var previousSolid = priorMap?.Layers.Where(layer => layer.Solid == true).Select(layer => layer.Key).ToHashSet();
            var disabled = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < layers.Length; index++)
            {
                var layer = map.Layers[index];
                if (!layer.IsGroup && (layer.Solid == null || previousSolid?.Contains(layer.Key) == true) &&
                    layers[index].TryGetComponent(out TilemapCollider2D collider) && !collider.enabled)
                    disabled.Add(layer.Key);
            }
            return disabled;
        }

        private static void ConfigureGrid(GameObject root, AsepriteMapDocument map, AsepriteMapDocument previous,
            Transform[] layers, IReadOnlyDictionary<string, Tile> tiles, AsepriteMapSettings settings,
            bool wasGridOnly, HashSet<string> manualOverrides)
        {
            var size = map.TileSize;
            var grid = root.TryGetComponent(out Grid existingGrid) ? existingGrid : root.AddComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;
            grid.cellSize = new Vector3(1, 1, 0);
            grid.cellGap = Vector3.zero;
            var canvas = new Vector3Int(map.Width / size, map.Height / size, 1);
            var previousSolid = previous?.Layers.Where(layer => layer.Solid == true).Select(layer => layer.Key).ToHashSet();

            for (var index = 0; index < layers.Length; index++)
            {
                var layer = map.Layers[index];
                var node = layers[index].gameObject;
                node.SetActive(layer.Visible);
                if (layer.IsGroup)
                    continue;
                var tilemap = node.TryGetComponent(out Tilemap existingTilemap) ? existingTilemap : node.AddComponent<Tilemap>();
                var renderer = node.TryGetComponent(out TilemapRenderer existingRenderer)
                    ? existingRenderer : node.AddComponent<TilemapRenderer>();
                var rule = settings.RenderRule(layer.Name);
                if (rule != null)
                    renderer.mode = rule.role == "Static" ? TilemapRenderer.Mode.Chunk : TilemapRenderer.Mode.Individual;
                renderer.sortingLayerID = string.IsNullOrEmpty(rule?.sortingLayer) ? 0 : SortingLayer.NameToID(rule.sortingLayer);
                renderer.sortingOrder = rule?.overrideSortingOrder == true ? rule.sortingOrder : index;
                tilemap.tileAnchor = new Vector3(0.5f, 0.5f, 0);
                tilemap.color = new Color32(255, 255, 255, layer.Opacity);
                tilemap.ClearAllTiles();
                tilemap.origin = Vector3Int.zero;
                tilemap.size = canvas;
                tilemap.ResizeBounds();
                var emptyAnchors = layer.CollisionCells == null ? null : new HashSet<(int X, int Y)>(layer.CollisionCells);
                emptyAnchors?.ExceptWith(layer.Cells.Select(cell => (cell.X, cell.Y)));
                var positions = new Vector3Int[layer.Cells.Count + (emptyAnchors?.Count ?? 0)];
                var placedTiles = new TileBase[positions.Length];
                for (var tileIndex = 0; tileIndex < layer.Cells.Count; tileIndex++)
                {
                    var cell = layer.Cells[tileIndex];
                    positions[tileIndex] = new Vector3Int(cell.X, cell.Y, 0);
                    placedTiles[tileIndex] = tiles[AsepriteMapArtImporter.TileName(
                        layer.TilesetId, cell.TileId, layer.IsSolid(cell))];
                }
                if (emptyAnchors != null)
                {
                    var tileIndex = layer.Cells.Count;
                    foreach (var cell in emptyAnchors.OrderBy(cell => cell.Y).ThenBy(cell => cell.X))
                    {
                        positions[tileIndex] = new Vector3Int(cell.X, cell.Y);
                        placedTiles[tileIndex++] = tiles[AsepriteMapArtImporter.CollisionTileName];
                    }
                }
                if (positions.Length > 0)
                    tilemap.SetTiles(positions, placedTiles);

                var collider = node.GetComponent<TilemapCollider2D>();
                if (settings.GridOnly || layer.Solid == false)
                {
                    if (collider)
                        collider.enabled = false;
                }
                else if (layer.Solid == true && !collider)
                {
                    node.AddComponent<TilemapCollider2D>();
                }
                else if (collider && wasGridOnly)
                {
                    collider.enabled = !manualOverrides.Contains(layer.Key);
                }
                else if (collider && layer.Solid == true && previousSolid?.Contains(layer.Key) != true)
                {
                    collider.enabled = true;
                    collider.isTrigger = false;
                }
            }
        }

        private static void RestoreAsset(string assetPath, string fullPath, byte[] previous, List<Exception> errors)
        {
            try
            {
                if (previous != null)
                {
                    File.WriteAllBytes(fullPath, previous);
                    AssetDatabase.ImportAsset(assetPath, ImportNow);
                }
                else if (File.Exists(fullPath) && !AssetDatabase.DeleteAsset(assetPath))
                {
                    File.Delete(fullPath);
                    File.Delete(fullPath + ".meta");
                    AssetDatabase.Refresh(ImportNow);
                }
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
    }
}
