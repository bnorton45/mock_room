#!/usr/bin/env python3
"""Generate MockRoom app icons (PNG set + Windows .ico) with no third-party deps.

The icon is a stylised top-down floor plan that mirrors the app itself: slate
walls with a door gap, a blue free-floor fill, and a single grey furniture box.
Rendered procedurally (4x supersampled) so it stays crisp at every size.

Usage:  python3 packaging/make-icons.py [output_dir]
Writes: mockroom-<size>.png for size in {16,32,48,64,128,256,512} and mockroom.ico
"""
import os
import sys
import struct
import zlib

SIZES = [16, 32, 48, 64, 128, 256, 512]
SS = 4  # supersampling factor for anti-aliasing

# Palette (RGBA)
TRANSPARENT = (0, 0, 0, 0)
WALL = (51, 65, 85, 255)        # slate-700
FLOOR = (59, 130, 246, 255)     # blue-500
FURNITURE = (148, 163, 184, 255)  # slate-400
BG = (248, 250, 252, 255)       # slate-50 interior backing


def _blend(dst, src):
    """Alpha-composite src over dst."""
    sa = src[3] / 255.0
    if sa == 0:
        return dst
    da = dst[3] / 255.0
    out_a = sa + da * (1 - sa)
    if out_a == 0:
        return (0, 0, 0, 0)
    out = [
        round((src[i] * sa + dst[i] * da * (1 - sa)) / out_a)
        for i in range(3)
    ]
    return (out[0], out[1], out[2], round(out_a * 255))


def render(size):
    """Render the icon at `size` px, supersampled then box-downsampled."""
    n = size * SS
    px = [[TRANSPARENT for _ in range(n)] for _ in range(n)]

    margin = n * 0.10
    wall = n * 0.075
    inner0 = margin + wall
    inner1 = n - margin - wall
    # Door gap along the bottom wall (centre-right).
    door_lo = n * 0.55
    door_hi = n * 0.78
    # Furniture box (lower-left quadrant of the interior).
    f0x = inner0 + (inner1 - inner0) * 0.10
    f1x = inner0 + (inner1 - inner0) * 0.42
    f0y = inner0 + (inner1 - inner0) * 0.50
    f1y = inner0 + (inner1 - inner0) * 0.88

    for y in range(n):
        for x in range(n):
            in_room = margin <= x <= n - margin and margin <= y <= n - margin
            in_inner = inner0 <= x <= inner1 and inner0 <= y <= inner1
            if not in_room:
                continue
            if in_inner:
                px[y][x] = BG
                px[y][x] = _blend(px[y][x], FLOOR)
                if f0x <= x <= f1x and f0y <= y <= f1y:
                    px[y][x] = _blend(px[y][x], FURNITURE)
            else:
                # Wall ring — punch out the door gap on the bottom edge.
                on_bottom = y >= n - margin - wall
                if on_bottom and door_lo <= x <= door_hi:
                    continue
                px[y][x] = WALL

    # Downsample (box filter) to target size.
    out = [[TRANSPARENT for _ in range(size)] for _ in range(size)]
    for oy in range(size):
        for ox in range(size):
            r = g = b = a = 0
            for dy in range(SS):
                for dx in range(SS):
                    p = px[oy * SS + dy][ox * SS + dx]
                    pa = p[3]
                    r += p[0] * pa
                    g += p[1] * pa
                    b += p[2] * pa
                    a += pa
            count = SS * SS
            if a == 0:
                out[oy][ox] = TRANSPARENT
            else:
                out[oy][ox] = (round(r / a), round(g / a), round(b / a), round(a / count))
    return out


def to_png_bytes(img):
    size = len(img)
    raw = bytearray()
    for row in img:
        raw.append(0)  # filter type 0
        for (r, g, b, a) in row:
            raw += bytes((r, g, b, a))

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data +
                struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff))

    sig = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    idat = zlib.compress(bytes(raw), 9)
    return sig + chunk(b"IHDR", ihdr) + chunk(b"IDAT", idat) + chunk(b"IEND", b"")


def to_ico(png_map):
    """Pack a {size: png_bytes} map into a PNG-compressed .ico (Vista+)."""
    sizes = sorted(s for s in png_map if s <= 256)
    count = len(sizes)
    header = struct.pack("<HHH", 0, 1, count)
    entries = bytearray()
    blobs = bytearray()
    offset = 6 + count * 16
    for s in sizes:
        data = png_map[s]
        w = 0 if s == 256 else s
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset)
        blobs += data
        offset += len(data)
    return header + bytes(entries) + bytes(blobs)


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
    os.makedirs(out_dir, exist_ok=True)
    png_map = {}
    for s in SIZES:
        img = render(s)
        data = to_png_bytes(img)
        png_map[s] = data
        path = os.path.join(out_dir, f"mockroom-{s}.png")
        with open(path, "wb") as f:
            f.write(data)
        print(f"wrote {path} ({len(data)} bytes)")
    ico_path = os.path.join(out_dir, "mockroom.ico")
    with open(ico_path, "wb") as f:
        f.write(to_ico(png_map))
    print(f"wrote {ico_path}")


if __name__ == "__main__":
    main()
