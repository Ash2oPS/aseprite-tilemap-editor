using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AsepriteTilemapEditor
{
    internal sealed class AsepriteMapCompanion
    {
        private const uint LegacySharedMagic = 0x314D5441; // ATM1
        private const uint SharedMagic = 0x324D5441; // ATM2
        private const int GuidLength = 32;
        public readonly byte[] Source;
        public readonly Dictionary<uint, string[]> SharedPages;
        public readonly string PolicyKey;
        public readonly bool GridOnly;
        public readonly HashSet<string> ManuallyDisabledColliders;

        private AsepriteMapCompanion(byte[] source, Dictionary<uint, string[]> sharedPages,
            string policyKey = null, bool gridOnly = false, HashSet<string> manuallyDisabled = null)
        {
            Source = source;
            SharedPages = sharedPages;
            PolicyKey = policyKey;
            GridOnly = gridOnly;
            ManuallyDisabledColliders = manuallyDisabled;
        }

        public static byte[] ReadFile(string path)
        {
            using var stream = File.OpenRead(path);
            var length = stream.Length;
            if (length > AsepriteMapDocument.MaximumFileSize + 128 * 1024)
                throw new InvalidDataException("The Aseprite companion exceeds the import limit.");
            using var reader = new BinaryReader(stream);
            var bytes = reader.ReadBytes((int)length);
            if (bytes.Length != length)
                throw new EndOfStreamException("The Aseprite companion is incomplete.");
            return bytes;
        }

        public static AsepriteMapCompanion Decode(byte[] contents)
        {
            var magic = contents.Length < 4 ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(contents);
            if (magic != SharedMagic && magic != LegacySharedMagic)
                return new AsepriteMapCompanion(contents, null);

            using var stream = new MemoryStream(contents, false);
            using var reader = new BinaryReader(stream);
            stream.Position = 4;
            var count = reader.ReadUInt16();
            if (count == 0 || count > 1024)
                throw new InvalidDataException("The Aseprite companion contains an invalid tileset count.");
            var pages = new Dictionary<uint, string[]>(count);
            for (var index = 0; index < count; index++)
            {
                var id = reader.ReadUInt32();
                var pageCount = reader.ReadUInt16();
                if (pageCount == 0 || pageCount > 128)
                    throw new InvalidDataException("The Aseprite companion contains an invalid page count.");
                var guids = new string[pageCount];
                for (var page = 0; page < guids.Length; page++)
                {
                    var guid = Encoding.ASCII.GetString(reader.ReadBytes(GuidLength));
                    if (!Guid.TryParseExact(guid, "N", out _))
                        throw new InvalidDataException("The Aseprite companion contains an invalid page GUID.");
                    guids[page] = guid;
                }
                if (!pages.TryAdd(id, guids))
                    throw new InvalidDataException("The Aseprite companion contains a duplicate tileset ID.");
            }
            string policyKey = null;
            var gridOnly = false;
            HashSet<string> manuallyDisabled = null;
            if (magic == SharedMagic)
            {
                policyKey = reader.ReadString();
                gridOnly = reader.ReadBoolean();
                manuallyDisabled = new HashSet<string>(StringComparer.Ordinal);
                var disabledCount = reader.ReadUInt16();
                for (var i = 0; i < disabledCount; i++)
                    manuallyDisabled.Add(reader.ReadString());
            }
            return new AsepriteMapCompanion(contents.AsSpan((int)stream.Position).ToArray(), pages,
                policyKey, gridOnly, manuallyDisabled);
        }

        public static void Write(string path, byte[] source, IReadOnlyDictionary<uint, string[]> sharedPages,
            string policyKey, bool gridOnly, IReadOnlyList<string> manuallyDisabled)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write(SharedMagic);
            writer.Write((ushort)sharedPages.Count);
            foreach (var pair in sharedPages)
            {
                writer.Write(pair.Key);
                writer.Write((ushort)pair.Value.Length);
                foreach (var guid in pair.Value)
                    writer.Write(Encoding.ASCII.GetBytes(guid));
            }
            writer.Write(policyKey);
            writer.Write(gridOnly);
            writer.Write((ushort)manuallyDisabled.Count);
            foreach (var layer in manuallyDisabled)
                writer.Write(layer);
            writer.Write(source);
        }
    }
}
