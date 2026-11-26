using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AsepriteTilemapEditor
{
    internal sealed class AsepriteMapDocument
    {
        private const int MaximumDecodedTilesetSize = 256 * 1024 * 1024;
        private const int MaximumTiles = 1024;
        private const string CollisionPrefix = "AsepriteTilemap:Collision:";
        internal const int MaximumFileSize = 90 * 1024 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public int TileSize { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public List<AsepriteMapLayer> Layers { get; } = new();

        internal static bool IsTilemapExport(byte[] bytes)
        {
            var data = bytes.AsSpan();
            if (data.Length < 144 || BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 0xA5E0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 1 ||
                BinaryPrimitives.ReadUInt16LittleEndian(data[12..]) != 32 ||
                BinaryPrimitives.ReadUInt16LittleEndian(data[132..]) != 0xF1FA)
                return false;

            var hasTilemap = false;
            var count = BinaryPrimitives.ReadUInt16LittleEndian(data[134..]);
            var chunks = count == ushort.MaxValue ? BinaryPrimitives.ReadUInt32LittleEndian(data[140..]) : count;
            var position = 144;
            for (var index = 0u; index < chunks; index++)
            {
                if (data.Length - position < 6)
                    return false;
                var size = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
                if (size < 6 || size > data.Length - position)
                    return false;
                if (BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 4)..]) == 0x2004)
                {
                    if (size < 10)
                        return false;
                    var type = BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 8)..]);
                    if (type != 1 && type != 2)
                        return false;
                    hasTilemap |= type == 2;
                }
                position += (int)size;
            }
            return hasTilemap;
        }
        public Dictionary<uint, AsepriteMapTileset> Tilesets { get; } = new();

        public static byte[] ReadFile(string path)
        {
            Require(new FileInfo(path).Length <= MaximumFileSize, "The map exceeds the 90 MB import limit.");
            return File.ReadAllBytes(path);
        }

        public static AsepriteMapDocument Read(byte[] bytes)
        {
            try
            {
                return ReadDocument(bytes, AsepriteMapSettings.Read());
            }
            catch (EndOfStreamException error)
            {
                throw new InvalidDataException("The Aseprite map ends inside a header or chunk.", error);
            }
            catch (DecoderFallbackException error)
            {
                throw new InvalidDataException("The Aseprite map contains an invalid UTF-8 name.", error);
            }
        }

        private static AsepriteMapDocument ReadDocument(byte[] bytes, AsepriteMapSettings settings)
        {
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            Require(bytes.Length >= 144 && bytes.Length <= MaximumFileSize &&
                    reader.ReadUInt32() == bytes.Length && reader.ReadUInt16() == 0xA5E0,
                "Invalid Aseprite file header.");
            Require(reader.ReadUInt16() == 1, "Only maps with one frame are supported.");

            var document = new AsepriteMapDocument { Width = reader.ReadUInt16(), Height = reader.ReadUInt16() };
            Require(reader.ReadUInt16() == 32, "Only RGBA Aseprite maps are supported.");
            var flags = reader.ReadUInt32();
            stream.Position = 128;
            Require(reader.ReadUInt32() == bytes.Length - 128 && reader.ReadUInt16() == 0xF1FA,
                "Invalid Aseprite frame size or header.");
            var shortChunkCount = reader.ReadUInt16();
            stream.Position += 4;
            var longChunkCount = reader.ReadUInt32();
            var chunkCount = shortChunkCount == ushort.MaxValue ? longChunkCount : shortChunkCount;
            Require(chunkCount <= (stream.Length - stream.Position) / 6, "Invalid chunk count.");

            var cels = new Dictionary<int, Cel>();
            var groups = new List<int>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var names = new Dictionary<(int, string), int>();
            var remainingTiles = MaximumTiles;
            var remainingDecodedBytes = MaximumDecodedTilesetSize;
            AsepriteMapLayer layerWithPendingUserData = null;
            for (var index = 0; index < chunkCount; index++)
            {
                var chunkStart = stream.Position;
                var chunkSize = reader.ReadUInt32();
                var kind = reader.ReadUInt16();
                var end = chunkStart + chunkSize;
                Require(chunkSize >= 6 && end <= stream.Length, "Invalid chunk size.");
                switch (kind)
                {
                    case 0x2004:
                        document.ReadLayer(reader, end, flags, groups, keys, names);
                        break;
                    case 0x2020 when layerWithPendingUserData != null && !layerWithPendingUserData.IsGroup:
                        Require(stream.Position + 4 <= end, "A layer user-data chunk is truncated.");
                        if ((reader.ReadUInt32() & 1) != 0)
                        {
                            var tag = ReadString(reader, end);
                            if (tag.StartsWith(CollisionPrefix, StringComparison.Ordinal))
                            {
                                Require(int.TryParse(tag.AsSpan(CollisionPrefix.Length), NumberStyles.None,
                                        CultureInfo.InvariantCulture, out var target),
                                    "Invalid collision helper target.");
                                layerWithPendingUserData.CollisionTarget = target;
                            }
                            else if (tag == "AsepriteTilemap:Solid" || tag == settings.legacySolidTag)
                                layerWithPendingUserData.Solid = true;
                            else if (tag == "AsepriteTilemap:Passable" || tag == settings.legacyPassableTag)
                                layerWithPendingUserData.Solid = false;
                        }
                        break;
                    case 0x2005:
                        var (layerIndex, cel) = ReadCel(reader, bytes, end);
                        Require(cels.TryAdd(layerIndex, cel), "A map layer has more than one cel.");
                        break;
                    case 0x2023:
                        var tileset = ReadTileset(reader, bytes, end, remainingTiles, remainingDecodedBytes);
                        Require(document.TileSize == 0 || document.TileSize == tileset.TileSize,
                            "All embedded tilesets in a map must use the same square grid.");
                        document.TileSize = tileset.TileSize;
                        remainingDecodedBytes -= tileset.Rgba.Length;
                        Require(document.Tilesets.TryAdd(tileset.Id, tileset), "Duplicate tileset ID.");
                        remainingTiles -= tileset.TileCount;
                        break;
                }
                layerWithPendingUserData = kind == 0x2004 ? document.Layers[^1] : null;
                Require(stream.Position <= end, "A chunk extends beyond its declared size.");
                stream.Position = end;
            }
            Require(stream.Position == stream.Length, "Unexpected data after the last frame chunk.");
            Require(document.Layers.Exists(layer => !layer.IsGroup), "The map contains no Tilemap layers.");
            Require(document.TileSize > 0 && document.Width >= document.TileSize && document.Height >= document.TileSize &&
                    document.Width % document.TileSize == 0 && document.Height % document.TileSize == 0,
                "Canvas dimensions must be positive multiples of the tileset grid size.");
            document.AssignCels(cels);
            document.ApplyCollisionHelpers();
            return document;
        }

        private void ApplyCollisionHelpers()
        {
            var start = Layers.FindIndex(layer => layer.CollisionTarget.HasValue);
            if (start < 0)
                return;

            for (var index = start; index < Layers.Count; index++)
            {
                var helper = Layers[index];
                var targetIndex = helper.CollisionTarget.GetValueOrDefault(-1);
                Require(targetIndex >= 0 && targetIndex < start && helper.ParentIndex < 0 && !helper.Visible,
                    "Collision helpers must be hidden top-level layers at the end of the export.");
                var target = Layers[targetIndex];
                Require(!target.IsGroup && target.CollisionCells == null,
                    "A collision helper must target one visual tilemap.");

                target.CollisionCells = new HashSet<(int, int)>();
                foreach (var cell in helper.Cells)
                    target.CollisionCells.Add((cell.X, cell.Y));
                target.Solid = true;
            }
            Layers.RemoveRange(start, Layers.Count - start);
        }

        private void ReadLayer(BinaryReader reader, long end, uint flags, List<int> groups,
            HashSet<string> keys, Dictionary<(int, string), int> names)
        {
            var layerFlags = reader.ReadUInt16();
            var kind = reader.ReadUInt16();
            Require(kind == 1 || kind == 2, "Only Tilemap layers and groups are supported. Export an Aseprite map.");
            Require((layerFlags & 64) == 0, "Reference layers are unsupported.");
            var depth = reader.ReadUInt16();
            Require(depth <= groups.Count && depth <= 64, "Invalid or excessively deep layer hierarchy.");
            if (depth < groups.Count)
                groups.RemoveRange(depth, groups.Count - depth);

            reader.BaseStream.Position += 4;
            var blendMode = reader.ReadUInt16();
            var opacity = reader.ReadByte();
            var isGroup = kind == 1;
            var opacityIsValid = isGroup ? (flags & 2) != 0 : (flags & 1) != 0;
            if (!opacityIsValid)
                opacity = byte.MaxValue;
            Require(blendMode == 0 && (!isGroup || opacity == byte.MaxValue),
                "Only normal blending and opaque groups are supported.");
            reader.BaseStream.Position += 3;

            var name = ReadString(reader, end);
            Require(!string.IsNullOrWhiteSpace(name), "A layer has an empty name.");
            var tilesetId = isGroup ? 0 : reader.ReadUInt32();
            var uuid = Guid.Empty;
            if ((flags & 4) != 0)
            {
                Require(reader.BaseStream.Position + 16 <= end, "A layer UUID is truncated.");
                uuid = new Guid(reader.ReadBytes(16));
            }
            var parent = groups.Count == 0 ? -1 : groups[groups.Count - 1];
            names.TryGetValue((parent, name), out var occurrence);
            names[(parent, name)] = occurrence + 1;
            var key = uuid == Guid.Empty
                ? (parent == -1 ? "root" : Layers[parent].Key) + $"/{name.Length}:{name}:{occurrence}"
                : uuid.ToString("N");
            Require(keys.Add(key), "Duplicate layer UUID.");

            if (isGroup)
                groups.Add(Layers.Count);
            Layers.Add(new AsepriteMapLayer
            {
                Name = name,
                Key = key,
                ParentIndex = parent,
                IsGroup = isGroup,
                Visible = (layerFlags & 1) != 0,
                Opacity = opacity,
                TilesetId = tilesetId
            });
        }

        private static (int Layer, Cel Cel) ReadCel(BinaryReader reader, byte[] bytes, long end)
        {
            var layer = reader.ReadUInt16();
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var opacity = reader.ReadByte();
            Require(reader.ReadUInt16() == 3, "Only compressed Tilemap cels are supported.");
            Require(reader.ReadInt16() == 0, "Custom cel Z-index values are unsupported.");
            reader.BaseStream.Position += 5;
            var width = reader.ReadUInt16();
            var height = reader.ReadUInt16();
            Require(width > 0 && height > 0 && (long)width * height <= 512 * 512,
                "Invalid or oversized Tilemap cel dimensions.");
            Require(reader.ReadUInt16() == 32 && reader.ReadUInt32() == 0x1fffffff &&
                    reader.ReadUInt32() == 0x80000000 && reader.ReadUInt32() == 0x40000000 &&
                    reader.ReadUInt32() == 0x20000000,
                "Unsupported Tilemap tile encoding.");
            reader.BaseStream.Position += 10;
            var start = (int)reader.BaseStream.Position;
            var data = Inflate(bytes, start, (int)end - start, width * height * 4);
            return (layer, new Cel(x, y, width, opacity, data));
        }

        private static AsepriteMapTileset ReadTileset(BinaryReader reader, byte[] bytes, long end, int remainingTiles, int remainingDecodedBytes)
        {
            var id = reader.ReadUInt32();
            var flags = reader.ReadUInt32();
            var count = reader.ReadUInt32();
            Require((flags & 7) == 6, "The tileset must be embedded and reserve tile 0 for empty cells.");
            Require(count > 0 && count <= remainingTiles, $"A map may contain up to {MaximumTiles} tiles.");
            var tileSize = reader.ReadUInt16();
            Require(tileSize > 0 && tileSize <= 512 && reader.ReadUInt16() == tileSize,
                "Use square tiles from 1 to 512 pixels.");
            var decodedSize = (long)count * tileSize * tileSize * 4;
            Require(decodedSize <= remainingDecodedBytes, "The map exceeds the decoded tileset size limit.");
            reader.BaseStream.Position += 16;
            var name = ReadString(reader, end);
            var compressedSize = reader.ReadUInt32();
            var start = (int)reader.BaseStream.Position;
            Require(compressedSize == end - start, "Unexpected embedded tileset data size.");
            var rgba = Inflate(bytes, start, (int)compressedSize, (int)decodedSize);
            return new AsepriteMapTileset(id, name, (int)count, tileSize, rgba);
        }

        private void AssignCels(Dictionary<int, Cel> cels)
        {
            foreach (var celIndex in cels.Keys)
                Require(celIndex < Layers.Count && !Layers[celIndex].IsGroup, "A cel refers to an invalid Tilemap layer.");

            for (var index = 0; index < Layers.Count; index++)
            {
                var layer = Layers[index];
                if (layer.IsGroup)
                    continue;

                Require(Tilesets.TryGetValue(layer.TilesetId, out var tileset),
                    $"Layer '{layer.Name}' refers to a missing embedded tileset.");
                if (!cels.TryGetValue(index, out var cel))
                    continue;

                Require(cel.X % TileSize == 0 && cel.Y % TileSize == 0, "Tilemap cels must align to the tileset grid.");
                layer.Opacity = (byte)((layer.Opacity * cel.Opacity + 127) / 255);
                for (var tileIndex = 0; tileIndex < cel.Data.Length / 4; tileIndex++)
                {
                    var id = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(cel.Data.AsSpan(tileIndex * 4, 4));
                    Require((id & 0xe0000000) == 0, $"Layer '{layer.Name}' contains flipped tiles.");
                    if (id == 0)
                        continue;

                    Require(id < tileset.TileCount, $"Layer '{layer.Name}' refers to a tile outside its tileset.");
                    var x = cel.X / TileSize + tileIndex % cel.Width;
                    var y = Height / TileSize - 1 - (cel.Y / TileSize + tileIndex / cel.Width);
                    Require(x >= 0 && x < Width / TileSize && y >= 0 && y < Height / TileSize,
                        $"Layer '{layer.Name}' contains a tile outside the canvas.");
                    layer.Cells.Add(new AsepriteMapCell(x, y, (int)id));
                }
            }
        }

        private static string ReadString(BinaryReader reader, long end)
        {
            var length = reader.ReadUInt16();
            Require(reader.BaseStream.Position + length <= end, "A name extends beyond its chunk.");
            return Utf8.GetString(reader.ReadBytes(length));
        }

        private static byte[] Inflate(byte[] bytes, int offset, int length, int expected)
        {
            Require(length >= 6 && offset >= 0 && length <= bytes.Length - offset, "Truncated zlib data.");
            var cmf = bytes[offset];
            var flg = bytes[offset + 1];
            Require((cmf & 15) == 8 && (cmf >> 4) <= 7 && ((cmf << 8) + flg) % 31 == 0 && (flg & 32) == 0,
                "Invalid zlib header.");
            using var stream = new MemoryStream(bytes, offset + 2, length - 6, false);
            using var inflater = new DeflateStream(stream, CompressionMode.Decompress);
            var result = new byte[expected];
            var total = 0;
            while (total < result.Length)
            {
                var read = inflater.Read(result, total, result.Length - total);
                Require(read > 0, "Compressed data is shorter than its declared dimensions.");
                total += read;
            }
            Require(inflater.ReadByte() == -1, "Compressed data exceeds its declared dimensions.");

            uint low = 1, high = 0;
            foreach (var value in result)
            {
                low += value;
                if (low >= 65521)
                    low -= 65521;
                high += low;
                if (high >= 65521)
                    high -= 65521;
            }
            var checksum = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + length - 4, 4));
            Require(checksum == ((high << 16) | low), "The embedded zlib checksum does not match.");
            return result;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidDataException(message);
        }

        private readonly struct Cel
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Width;
            public readonly byte Opacity;
            public readonly byte[] Data;

            public Cel(int x, int y, int width, byte opacity, byte[] data)
            {
                X = x;
                Y = y;
                Width = width;
                Opacity = opacity;
                Data = data;
            }
        }
    }

    internal sealed class AsepriteMapLayer
    {
        public string Key;
        public string Name;
        public int ParentIndex;
        public bool IsGroup;
        public bool Visible;
        public int? CollisionTarget;
        public bool? Solid;
        public byte Opacity;
        public uint TilesetId;
        public readonly List<AsepriteMapCell> Cells = new();
        public HashSet<(int X, int Y)> CollisionCells;

        public bool IsSolid(AsepriteMapCell cell) =>
            Solid == true && (CollisionCells == null || CollisionCells.Contains((cell.X, cell.Y)));
    }

    internal readonly struct AsepriteMapCell
    {
        public readonly int X;
        public readonly int Y;
        public readonly int TileId;

        public AsepriteMapCell(int x, int y, int tileId)
        {
            X = x;
            Y = y;
            TileId = tileId;
        }
    }

    internal sealed class AsepriteMapTileset
    {
        public uint Id { get; }
        public string Name { get; }
        public int TileCount { get; }
        public int TileSize { get; }
        public byte[] Rgba { get; }

        public AsepriteMapTileset(uint id, string name, int tileCount, int tileSize, byte[] rgba)
        {
            Id = id;
            Name = name;
            TileCount = tileCount;
            TileSize = tileSize;
            Rgba = rgba;
        }
    }
}
