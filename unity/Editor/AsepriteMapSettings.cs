using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

[assembly: InternalsVisibleTo("AsepriteTilemapEditor.Editor.Tests")]

namespace AsepriteTilemapEditor
{
    [Serializable]
    internal sealed class AsepriteMapSettings
    {
        private const string SettingsFile = "ProjectSettings/AsepriteTilemapEditor.json";
        public bool autoImport = true;
        public string sourceFolder = "Assets";
        public string outputFolder = "Assets/Resources/Tilemaps";
        public string sharedTilesetFolder = "Assets/AsepriteTilemapEditor/SharedTilesets";
        public string legacySolidTag;
        public string legacyPassableTag;
        public string collisionMode = "Physics2D";
        public AsepriteMapRenderRule[] renderRules = Array.Empty<AsepriteMapRenderRule>();

        internal bool GridOnly => collisionMode == "Grid";

        internal string ImportPolicyKey()
        {
            using var hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(this))));
        }

        internal static AsepriteMapSettings Read()
        {
            var settings = new AsepriteMapSettings();
            var path = AsepriteMapImporter.ProjectFilePath(SettingsFile);
            if (File.Exists(path))
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), settings);

            settings.sourceFolder = AssetFolder(settings.sourceFolder);
            settings.outputFolder = AssetFolder(settings.outputFolder);
            settings.sharedTilesetFolder = AssetFolder(settings.sharedTilesetFolder);
            if (settings.outputFolder == "Assets")
                throw new InvalidDataException("Choose a dedicated output folder under Assets.");
            if (settings.collisionMode is not ("Physics2D" or "Grid"))
                throw new InvalidDataException("Aseprite collisionMode must be Physics2D or Grid.");
            settings.renderRules ??= Array.Empty<AsepriteMapRenderRule>();
            if (settings.renderRules.Any(rule => rule == null || string.IsNullOrEmpty(rule.layer) ||
                    rule.role is not ("Static" or "DepthSorted")) ||
                settings.renderRules.Select(rule => rule.layer).Distinct(StringComparer.Ordinal).Count() !=
                settings.renderRules.Length)
                throw new InvalidDataException("Aseprite render rules need unique layer names and Static or DepthSorted roles.");
            foreach (var rule in settings.renderRules)
                if (!string.IsNullOrEmpty(rule.sortingLayer) && !SortingLayer.layers.Any(layer => layer.name == rule.sortingLayer))
                    throw new InvalidDataException($"Unknown Unity sorting layer '{rule.sortingLayer}'.");
            return settings;
        }

        internal AsepriteMapRenderRule RenderRule(string name)
        {
            var exact = renderRules.FirstOrDefault(rule => rule.layer == name);
            if (exact != null)
                return exact;
            const string overlap = " · Overlap ";
            var separator = name.LastIndexOf(overlap, StringComparison.Ordinal);
            if (separator < 0 || !int.TryParse(name.AsSpan(separator + overlap.Length), out _))
                return null;
            var owner = name.Substring(0, separator);
            return renderRules.FirstOrDefault(rule => rule.includeOverlaps && rule.layer == owner);
        }

        internal bool IncludesSource(string path) =>
            path.StartsWith(sourceFolder + "/", StringComparison.Ordinal);

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
                throw new IOException($"Could not create the Aseprite output folder '{path}'.");
        }

        private static string AssetFolder(string path)
        {
            var normalized = path?.TrimEnd('/');
            if (string.IsNullOrEmpty(normalized) || normalized.Contains('\\') ||
                (normalized != "Assets" && !normalized.StartsWith("Assets/", StringComparison.Ordinal)) ||
                normalized.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException("Aseprite project folders must be relative paths under Assets.");
            return normalized;
        }
    }

    [Serializable]
    internal sealed class AsepriteMapRenderRule
    {
        public string layer;
        public string role;
        public bool includeOverlaps;
        public string sortingLayer;
        public bool overrideSortingOrder;
        public int sortingOrder;
    }
}
