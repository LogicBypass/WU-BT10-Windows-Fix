"""Build and validate the executable's multi-resolution Windows icon."""

from __future__ import annotations

import struct
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets" / "app-icon.png"
OUTPUT = ROOT / "assets" / "app.ico"
SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)]


def build() -> None:
    with Image.open(SOURCE) as source:
        rgba = source.convert("RGBA")
        rgba.save(
            OUTPUT,
            format="ICO",
            sizes=SIZES,
            bitmap_format="bmp",
        )


def validate() -> None:
    data = OUTPUT.read_bytes()
    reserved, file_type, image_count = struct.unpack_from("<HHH", data)
    if (reserved, file_type, image_count) != (0, 1, len(SIZES)):
        raise ValueError("ICO directory header is invalid.")

    found_sizes: list[tuple[int, int]] = []
    for index in range(image_count):
        entry = struct.unpack_from("<BBBBHHII", data, 6 + index * 16)
        width = entry[0] or 256
        height = entry[1] or 256
        bits_per_pixel = entry[5]
        image_size = entry[6]
        image_offset = entry[7]
        payload = data[image_offset : image_offset + image_size]

        if bits_per_pixel != 32:
            raise ValueError(f"{width}x{height} frame is not 32-bit.")
        if payload.startswith(b"\x89PNG\r\n\x1a\n"):
            raise ValueError(f"{width}x{height} frame is unexpectedly PNG-compressed.")
        if len(payload) != image_size:
            raise ValueError(f"{width}x{height} frame is truncated.")

        dib_header_size = struct.unpack_from("<I", payload)[0]
        dib_width, doubled_height = struct.unpack_from("<ii", payload, 4)
        if dib_header_size < 40 or dib_width != width or doubled_height != height * 2:
            raise ValueError(f"{width}x{height} DIB header is invalid.")

        found_sizes.append((width, height))

    if found_sizes != SIZES:
        raise ValueError(f"Unexpected ICO frames: {found_sizes!r}")

    with Image.open(OUTPUT) as icon:
        decoded_sizes = sorted(icon.ico.sizes())
        if decoded_sizes != SIZES:
            raise ValueError(f"Decoder returned unexpected frames: {decoded_sizes!r}")

    print(f"Created {OUTPUT} ({len(data):,} bytes)")
    print("Frames:", ", ".join(f"{width}x{height} RGBA DIB" for width, height in found_sizes))


if __name__ == "__main__":
    build()
    validate()
