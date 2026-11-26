"""Generate the original 16 px example atlas and its brush definitions."""

import json
from pathlib import Path
import struct
import zlib


SIZE = 16
COLUMNS = 8
DESTINATION = Path(__file__).resolve().parents[1] / "packs/minimal/resources"


def valid_mask(mask):
    for corner in range(4):
        sides = (1 << corner) | (1 << ((corner + 1) % 4))
        if mask & (16 << corner) and mask & sides != sides:
            return False
    return True


def tile_pixel(mask, x, y):
    distances = (y, SIZE - 1 - x, SIZE - 1 - y, x)
    edge = min((value for side, value in enumerate(distances) if not mask & (1 << side)), default=SIZE)
    for corner in range(4):
        sides = (1 << corner) | (1 << ((corner + 1) % 4))
        if mask & sides == sides and not mask & (16 << corner):
            edge = min(edge, sum(distances[side] for side in (corner, (corner + 1) % 4)))
    if edge < 2:
        return (0, 0, 0, 0)
    noise = (x * 17 + y * 23) % 13
    return (69 + noise, (113 if edge < 4 else 153) + noise, 62 + noise, 255)


def png_chunk(name, data):
    return struct.pack(">I", len(data)) + name + data + struct.pack(">I", zlib.crc32(name + data))


def main():
    masks = [mask for mask in range(256) if valid_mask(mask)]
    assert len(masks) == 47
    width, height = COLUMNS * SIZE, ((len(masks) + COLUMNS) // COLUMNS) * SIZE
    pixels = bytearray(width * height * 4)
    for index, mask in enumerate([*masks, None]):
        for y in range(SIZE):
            for x in range(SIZE):
                offset = 4 * ((index // COLUMNS * SIZE + y) * width + index % COLUMNS * SIZE + x)
                color = tile_pixel(mask, x, y) if mask is not None else (
                    (123, 131, 150, 255) if (x - 7)**2 + (y - 9)**2 < 35 else (0, 0, 0, 0))
                pixels[offset:offset + 4] = bytes(color)
    rows = b"".join(b"\x00" + pixels[y * width * 4:(y + 1) * width * 4] for y in range(height))
    png = b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += png_chunk(b"IDAT", zlib.compress(rows)) + png_chunk(b"IEND", b"")
    catalog = {
        "schemaVersion": 2, "tileSize": SIZE, "columns": COLUMNS, "brushOrder": ["grass", "stone"],
        "brushes": {
            "grass": {"label": "Grass", "color": "#52943E", "sprites": {str(mask): i for i, mask in enumerate(masks)}},
            "stone": {"label": "Stone", "color": "#7B8396", "parts": [{"x": 0, "y": 0, "index": len(masks)}]},
        },
    }
    (DESTINATION / "tiles.png").write_bytes(png)
    (DESTINATION / "tiles.json").write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
