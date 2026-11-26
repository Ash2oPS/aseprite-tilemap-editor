using System;
using UnityEngine;

namespace AsepriteTilemapEditor
{
    internal static class AsepriteMapAtlas
    {
        private static int TilesPerRow(AsepriteMapTileset tileset) => 2048 / (tileset.TileSize + 2);

        public static int PageCount(AsepriteMapTileset tileset)
        {
            var row = TilesPerRow(tileset);
            var tilesPerPage = row * row;
            return Math.Max(1, (tileset.TileCount - 2 + tilesPerPage) / tilesPerPage);
        }

        public static Page GetPage(AsepriteMapTileset tileset, int pageIndex)
        {
            var stride = tileset.TileSize + 2;
            var tilesPerRow = TilesPerRow(tileset);
            var tilesPerPage = tilesPerRow * tilesPerRow;
            var firstTile = pageIndex * tilesPerPage + 1;
            var endTile = Math.Min(firstTile + tilesPerPage, tileset.TileCount);
            var count = Math.Max(1, endTile - firstTile);
            var width = Math.Min(count, tilesPerRow) * stride;
            var height = ((count + tilesPerRow - 1) / tilesPerRow) * stride;
            return new Page(firstTile, endTile, width, height, stride, tilesPerRow);
        }

        public static Texture2D BuildTexture(AsepriteMapTileset tileset, Page page, string name)
            => CreateTexture(page.Width, page.Height, BuildPixels(tileset, page), name);

        public static byte[] BuildPixels(AsepriteMapTileset tileset, Page page)
        {
            var pixels = new byte[page.Width * page.Height * 4];
            for (var tile = page.FirstTile; tile < page.EndTile; tile++)
            {
                var (x, y) = page.GetTileOrigin(tile);
                CopyTile(tileset, tile, pixels, page.Width, x, y);
            }
            return pixels;
        }

        public static Texture2D CreateTexture(int width, int height, byte[] pixels, string name)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.LoadRawTextureData(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static void CopyTile(AsepriteMapTileset tileset, int tile, byte[] pixels, int width, int x, int y)
        {
            var tileSize = tileset.TileSize;
            var source = tileset.Rgba;
            var rowBytes = tileSize * 4;
            var tileOffset = tile * tileSize * rowBytes;
            for (var row = -1; row <= tileSize; row++)
            {
                var sourceRow = tileSize - 1 - Math.Clamp(row, 0, tileSize - 1);
                var sourceOffset = tileOffset + sourceRow * rowBytes;
                var destination = ((y + row) * width + x) * 4;
                Buffer.BlockCopy(source, sourceOffset, pixels, destination, rowBytes);
                Buffer.BlockCopy(source, sourceOffset, pixels, destination - 4, 4);
                Buffer.BlockCopy(source, sourceOffset + rowBytes - 4, pixels, destination + rowBytes, 4);
            }
        }

        internal readonly struct Page
        {
            public readonly int FirstTile;
            public readonly int EndTile;
            public readonly int Width;
            public readonly int Height;
            private readonly int stride;
            private readonly int tilesPerRow;

            public Page(int firstTile, int endTile, int width, int height, int stride, int tilesPerRow)
            {
                FirstTile = firstTile;
                EndTile = endTile;
                Width = width;
                Height = height;
                this.stride = stride;
                this.tilesPerRow = tilesPerRow;
            }

            public (int X, int Y) GetTileOrigin(int tile)
            {
                var slot = tile - FirstTile;
                return (slot % tilesPerRow * stride + 1, slot / tilesPerRow * stride + 1);
            }
        }
    }
}
