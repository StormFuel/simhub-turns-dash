"""Procedural 2x2 twill carbon-fibre background (pure Python, no PIL).

Low-contrast by design: tones stay within #0A0B0D..#17191D so the texture never
competes with telemetry traces drawn on top of it (see docs/design.md).
"""
import math
import struct
import zlib

TOW = 7          # tow width in px
PERIOD = 4 * TOW  # a 2x2 twill repeats every 4 tows in both directions


def _tile():
    """Return one PERIOD x PERIOD tile of brightness values (0..1)."""
    tile = []
    for y in range(PERIOD):
        row = []
        for x in range(PERIOD):
            i, j = x // TOW, y // TOW
            warp_on_top = (i - j) % 4 < 2
            if warp_on_top:
                # Vertical fibres: rounded across x, slight sheen along y.
                across = (x % TOW + 0.5) / TOW
                base = 0.62
            else:
                across = (y % TOW + 0.5) / TOW
                base = 0.38
            crown = math.sin(math.pi * across)        # rounded tow profile
            row.append(base * (0.55 + 0.45 * crown))
        tile.append(row)
    return tile


def render(width, height, lo=(0x0A, 0x0B, 0x0D), hi=(0x17, 0x19, 0x1D)):
    tile = _tile()
    cx, cy = width / 2, height / 2
    max_r = math.hypot(cx, cy)
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # PNG filter: none
        trow = tile[y % PERIOD]
        for x in range(width):
            v = trow[x % PERIOD]
            # Faint diagonal sheen band and vignette.
            sheen = 0.10 * math.exp(-(((x - y * 1.4) - width * 0.25) / (width * 0.35)) ** 2)
            vign = 1 - 0.45 * (math.hypot(x - cx, y - cy) / max_r) ** 2
            t = max(0.0, min(1.0, (v + sheen) * vign))
            raw += bytes(int(lo[k] + (hi[k] - lo[k]) * t) for k in range(3))
    return _png(width, height, bytes(raw))


def _png(width, height, raw, alpha=False):
    def chunk(tag, data):
        body = tag + data
        return struct.pack('>I', len(data)) + body + struct.pack('>I', zlib.crc32(body) & 0xFFFFFFFF)

    header = struct.pack('>IIBBBBB', width, height, 8, 6 if alpha else 2, 0, 0, 0)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', header)
            + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def slashes(width, height, rgb, count=5, slant=0.6, gap=0.55):
    """Angled stripes on a transparent background (RGBA PNG): a generic "speed slash" accent, fading left to right."""
    raw = bytearray()
    period = width / count
    for y in range(height):
        raw.append(0)
        for x in range(width):
            u = (x + (height - y) * slant) % period / period
            on = u < (1 - gap)
            fade = 1 - 0.75 * x / width
            raw += bytes(rgb) + bytes([int(255 * fade) if on else 0])
    return _png(width, height, bytes(raw), alpha=True)


if __name__ == '__main__':
    import sys
    out = sys.argv[1] if len(sys.argv) > 1 else 'carbon.png'
    with open(out, 'wb') as f:
        f.write(render(1920, 1080))
    print('wrote', out)
