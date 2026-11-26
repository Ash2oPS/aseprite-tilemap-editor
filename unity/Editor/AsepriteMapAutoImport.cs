using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AsepriteTilemapEditor
{
    internal sealed class AsepriteMapAutoImport : AssetPostprocessor
    {
        private const string SourceLabelPrefix = "AsepriteMapSource_";
        private static readonly HashSet<string> Pending = new(StringComparer.Ordinal);

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
                Queue(path);
            foreach (var path in moved)
                Queue(path);
        }

        internal static void Queue(string assetPath)
        {
            if (!assetPath.StartsWith("Assets/") || !AsepriteMapImporter.IsAsepritePath(assetPath))
                return;
            if (Pending.Add(assetPath) && Pending.Count == 1)
                EditorApplication.update += ProcessPending;
        }

        private static void ProcessPending()
        {
            if (!AsepriteMapImporter.CanImport() || BuildPipeline.isBuildingPlayer ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
                return;

            foreach (var path in Pending.OrderBy(path => path, StringComparer.Ordinal).ToArray())
            {
                Pending.Remove(path);
                try
                {
                    var settings = AsepriteMapSettings.Read();
                    if (settings.autoImport && settings.IncludesSource(path))
                        Sync(path, settings, out _);
                }
                catch (Exception error)
                {
                    Debug.LogError($"Aseprite map import failed for '{path}': {error.Message}");
                }
            }
            if (Pending.Count == 0)
                EditorApplication.update -= ProcessPending;
        }

        internal static GameObject Sync(string assetPath, out bool deferred) =>
            Sync(assetPath, AsepriteMapSettings.Read(), out deferred);

        internal static GameObject Sync(string assetPath, AsepriteMapSettings settings, out bool deferred)
        {
            deferred = false;
            var guid = AssetDatabase.AssetPathToGUID(assetPath, AssetPathToGUIDOptions.OnlyExistingAssets);
            if (string.IsNullOrEmpty(guid))
                return null;

            var sourceFile = AsepriteMapImporter.ProjectFilePath(assetPath);
            if (!File.Exists(sourceFile))
                return null;

            var input = AsepriteMapDocument.ReadFile(sourceFile);
            if (!AsepriteMapDocument.IsTilemapExport(input))
                return null;

            var label = SourceLabelPrefix + guid;
            var linked = AssetDatabase.FindAssets($"t:Prefab l:{label}", new[] { "Assets" });
            if (linked.Length > 1)
                throw new InvalidOperationException("Multiple prefabs are linked to this source. Keep one source label.");

            var name = AsepriteMapImporter.SuggestedPrefabName(assetPath);
            var existing = linked.Length == 1
                ? AssetDatabase.GUIDToAssetPath(linked[0])
                : FindLegacyPair(assetPath, name, settings.outputFolder, input);
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == existing && existing != null)
            {
                deferred = true;
                return null;
            }

            var companion = existing != null ? ReadOwnedPair(existing) : null;
            var sameData = companion?.SharedPages != null && input.AsSpan().SequenceEqual(companion.Source) &&
                companion.PolicyKey == settings.ImportPolicyKey() &&
                companion.SharedPages.Values.SelectMany(guids => guids)
                    .All(guid => AssetDatabase.GUIDToAssetPath(guid)?.EndsWith(
                        "." + AsepriteMapTextureImporter.Extension, StringComparison.Ordinal) == true);
            var map = sameData ? null : AsepriteMapDocument.Read(input);
            var folder = settings.outputFolder;
            AsepriteMapSettings.EnsureFolder(folder);

            var destination = existing != null && existing.StartsWith(folder + "/", StringComparison.Ordinal)
                ? existing
                : AllocatePath(folder, existing == null ? name : Path.GetFileNameWithoutExtension(existing));
            var oldArt = existing == null ? null : AsepriteMapImporter.ArtPath(existing);
            var newArt = AsepriteMapImporter.ArtPath(destination);
            var moving = existing != null && existing != destination;
            var movedArt = false;
            var movedPrefab = false;
            try
            {
                if (moving)
                {
                    if (!IsOwnedPair(existing))
                        throw new InvalidOperationException("The linked prefab's Aseprite art asset is missing.");
                    Move(oldArt, newArt);
                    movedArt = true;
                    Move(existing, destination);
                    movedPrefab = true;
                }

                var prefab = sameData
                    ? AssetDatabase.LoadAssetAtPath<GameObject>(destination)
                    : AsepriteMapImporter.ImportPrepared(input, map, destination, settings);
                var labels = AssetDatabase.GetLabels(prefab);
                if (!labels.Contains(label))
                    AssetDatabase.SetLabels(prefab, labels.Append(label).ToArray());
                return prefab;
            }
            catch
            {
                if (movedPrefab)
                    Move(destination, existing);
                if (movedArt)
                    Move(newArt, oldArt);
                throw;
            }
        }

        private static string FindLegacyPair(string source, string name, string outputFolder, byte[] input)
        {
            var folder = Path.GetDirectoryName(source).Replace('\\', '/');
            var oldPath = $"{folder}/{name}.prefab";
            if (IsUnlinkedPair(oldPath, input))
                return oldPath;
            var resourcePath = $"{outputFolder}/{name}.prefab";
            return IsUnlinkedPair(resourcePath, input) ? resourcePath : null;
        }

        private static bool IsUnlinkedPair(string path, byte[] input)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab && !AssetDatabase.GetLabels(prefab)
                .Any(label => label.StartsWith(SourceLabelPrefix, StringComparison.Ordinal)) && PairMatches(path, input);
        }

        private static bool PairMatches(string prefabPath, byte[] input)
        {
            var companion = ReadOwnedPair(prefabPath);
            return companion != null && input.AsSpan().SequenceEqual(companion.Source);
        }

        private static AsepriteMapCompanion ReadOwnedPair(string prefabPath) =>
            IsOwnedPair(prefabPath) ? AsepriteMapCompanion.Decode(AsepriteMapCompanion.ReadFile(
                AsepriteMapImporter.ProjectFilePath(AsepriteMapImporter.ArtPath(prefabPath)))) : null;

        private static bool IsOwnedPair(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var artPath = AsepriteMapImporter.ArtPath(prefabPath);
            var artGuid = AssetDatabase.AssetPathToGUID(artPath, AssetPathToGUIDOptions.OnlyExistingAssets);
            return prefab && !string.IsNullOrEmpty(artGuid) &&
                   AssetDatabase.GetLabels(prefab).Contains(AsepriteMapImporter.OwnerLabelPrefix + artGuid);
        }

        private static string AllocatePath(string folder, string name)
        {
            var path = $"{folder}/{name}.prefab";
            for (var suffix = 2; AssetExists(path) || AssetExists(AsepriteMapImporter.ArtPath(path)); suffix++)
                path = $"{folder}/{name}_{suffix}.prefab";
            return path;
        }

        private static bool AssetExists(string assetPath) =>
            File.Exists(AsepriteMapImporter.ProjectFilePath(assetPath)) ||
            !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath, AssetPathToGUIDOptions.OnlyExistingAssets));

        private static void Move(string from, string to)
        {
            var error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error))
                throw new IOException($"Could not move '{from}' to '{to}': {error}");
        }
    }
}
