using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace AsepriteTilemapEditor
{
    internal static class AsepriteMapSharedTextures
    {
        internal static Dictionary<uint, string[]> EnsurePages(AsepriteMapDocument map, string folder)
        {
            AsepriteMapSettings.EnsureFolder(folder);
            var pages = new Dictionary<uint, string[]>();
            foreach (var tileset in map.Tilesets.Values)
            {
                var paths = PagePaths(tileset, folder);
                var guids = new string[paths.Length];
                for (var index = 0; index < paths.Length; index++)
                {
                    var path = paths[index];
                    var page = AsepriteMapAtlas.GetPage(tileset, index);
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (!texture)
                    {
                        if (AssetDatabase.LoadMainAssetAtPath(path) || File.Exists(AsepriteMapImporter.ProjectFilePath(path)))
                            throw new IOException($"Cannot create a shared tileset page over the existing asset '{path}'.");
                        AsepriteMapTextureImporter.Write(AsepriteMapImporter.ProjectFilePath(path), tileset, page);
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                        texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    }
                    if (!texture || texture.width != page.Width || texture.height != page.Height)
                        throw new InvalidDataException($"Shared tileset page '{path}' has unexpected dimensions.");
                    guids[index] = AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(guids[index]))
                        throw new IOException($"Could not register the shared tileset page '{path}'.");
                }
                pages.Add(tileset.Id, guids);
            }
            return pages;
        }

        internal static string[] PagePaths(AsepriteMapTileset tileset, string folder)
        {
            // Hash the ordered pixel data, size, and packing format. Map cells, layers, and tileset names
            // are deliberately excluded, so maps with the same art resolve to the same texture assets.
            Span<byte> header = stackalloc byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(header, 1);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..], tileset.TileSize);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], tileset.TileCount);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(header);
            hash.AppendData(tileset.Rgba);
            var key = BitConverter.ToString(hash.GetHashAndReset()).Replace("-", "").ToLowerInvariant();
            var paths = new string[AsepriteMapAtlas.PageCount(tileset)];
            for (var index = 0; index < paths.Length; index++)
                paths[index] = $"{folder}/T_Aseprite_{key}_Page{index}.{AsepriteMapTextureImporter.Extension}";
            return paths;
        }
    }
}
