using System.IO;
using System.IO.Compression;
using UnityEditor.AssetImporters;

namespace AsepriteTilemapEditor
{
    [ScriptedImporter(1, Extension)]
    public sealed class AsepriteMapTextureImporter : ScriptedImporter
    {
        internal const string Extension = "asepriteatlas";
        private const uint Magic = 0x31505441; // ATP1

        internal static void Write(string path, AsepriteMapTileset tileset, AsepriteMapAtlas.Page page)
        {
            var pixels = AsepriteMapAtlas.BuildPixels(tileset, page);
            var temporary = Path.Combine(Path.GetDirectoryName(path), "." + Path.GetRandomFileName() + ".tmp");
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write);
            try
            {
                using (stream)
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Magic);
                    writer.Write((ushort)page.Width);
                    writer.Write((ushort)page.Height);
                    using var compressed = new DeflateStream(stream, CompressionLevel.Optimal, true);
                    compressed.Write(pixels, 0, pixels.Length);
                }
                File.Move(temporary, path);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        public override void OnImportAsset(AssetImportContext context)
        {
            using var stream = File.OpenRead(context.assetPath);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Unrecognized Aseprite atlas page.");
            var width = reader.ReadUInt16();
            var height = reader.ReadUInt16();
            if (width == 0 || height == 0 || width > 2048 || height > 2048)
                throw new InvalidDataException("Invalid Aseprite atlas page dimensions.");

            var pixels = new byte[width * height * 4];
            using var compressed = new DeflateStream(stream, CompressionMode.Decompress);
            var offset = 0;
            while (offset < pixels.Length)
            {
                var read = compressed.Read(pixels, offset, pixels.Length - offset);
                if (read == 0)
                    throw new InvalidDataException("The Aseprite atlas page is incomplete.");
                offset += read;
            }
            if (compressed.ReadByte() != -1)
                throw new InvalidDataException("The Aseprite atlas page contains too many pixels.");
            var texture = AsepriteMapAtlas.CreateTexture(width, height, pixels, Path.GetFileNameWithoutExtension(context.assetPath));
            context.AddObjectToAsset("Texture", texture);
            context.SetMainObject(texture);
        }
    }
}
