"""Livery-style background art: lime angular shards in the top corners and a neon-magenta paint splatter.

Inspired by action-sports race liveries in general; deliberately uses no logos, marks, names or race numbers of any
brand or person. Drawn as SVG and rasterised with headless Chrome (no imaging libraries needed), seeded so the
output is reproducible.

    python tools/livery.py out.png      # writes the 1920x1080 background
"""
import base64
import math
import random
import subprocess
import sys
import tempfile
from pathlib import Path

import carbon

BROWSERS = [Path(r'C:\Program Files\Google\Chrome\Application\chrome.exe'),
            Path(r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe')]

LIME = '#A8F000'
MAGENTA = '#FF1ED2'   # replaced orange (2026-10-03) to move away from a brand's signature colour
WHITE = '#F4F4F4'


def _shards(w):
    """Angular lime wedges sweeping in from both top corners, with black pinstripe cuts."""
    left = [
        f'<polygon points="0,0 520,0 300,150 160,150 0,300" fill="{LIME}"/>',
        f'<polygon points="0,330 190,170 260,170 0,420" fill="{LIME}" opacity="0.85"/>',
        '<polygon points="380,0 430,0 250,130 200,130" fill="#050505"/>',
        f'<polygon points="560,0 640,0 470,96 420,96" fill="{LIME}" opacity="0.9"/>',
    ]
    right = [p.replace('<polygon', f'<polygon transform="translate({w},0) scale(-1,1)"') for p in left]
    return ''.join(left + right)


def _splatter(cx, cy, size, color, rng):
    """A paint splat: a lumpy core, radiating spikes with blobbed ends, and scattered droplets."""
    parts = []
    for _ in range(14):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(0, size * 0.35)
        parts.append(f'<circle cx="{cx + d * math.cos(a):.1f}" cy="{cy + d * math.sin(a):.1f}" '
                     f'r="{rng.uniform(size * 0.22, size * 0.42):.1f}" fill="{color}"/>')
    for _ in range(22):
        a = rng.uniform(0, 2 * math.pi)
        reach = rng.uniform(size * 0.6, size * 1.5)
        width = rng.uniform(size * 0.03, size * 0.09)
        ex, ey = cx + reach * math.cos(a), cy + reach * math.sin(a)
        parts.append(f'<line x1="{cx:.1f}" y1="{cy:.1f}" x2="{ex:.1f}" y2="{ey:.1f}" stroke="{color}" '
                     f'stroke-width="{width:.1f}" stroke-linecap="round"/>')
        parts.append(f'<circle cx="{ex:.1f}" cy="{ey:.1f}" r="{width * rng.uniform(0.8, 1.6):.1f}" fill="{color}"/>')
    for _ in range(40):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(size * 0.9, size * 2.0)
        parts.append(f'<circle cx="{cx + d * math.cos(a):.1f}" cy="{cy + d * math.sin(a):.1f}" '
                     f'r="{rng.uniform(1.5, size * 0.05):.1f}" fill="{color}"/>')
    return ''.join(parts)


def background_svg(w=1920, h=1080, seed=7):
    rng = random.Random(seed)
    texture = base64.b64encode(carbon.render(w, h, (0x04, 0x04, 0x04), (0x12, 0x12, 0x12))).decode()
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}">'
            f'<image href="data:image/png;base64,{texture}" width="{w}" height="{h}"/>'
            + _shards(w)
            + _splatter(w - 120, h - 90, 120, MAGENTA, rng)
            + _splatter(150, h - 40, 55, LIME, random.Random(seed + 1))
            + '</svg>')


def render_png(svg, w, h):
    browser = next(b for b in BROWSERS if b.exists())
    with tempfile.TemporaryDirectory() as tmp:
        page = Path(tmp) / 'art.html'
        out = Path(tmp) / 'art.png'
        page.write_text(f'<html><body style="margin:0;background:#000">{svg}</body></html>', encoding='utf-8')
        subprocess.run([str(browser), '--headless=new', '--disable-gpu', '--hide-scrollbars',
                        f'--window-size={w},{h}', f'--screenshot={out}', page.as_uri()],
                       check=True, capture_output=True, timeout=60)
        return out.read_bytes()


def background(w=1920, h=1080):
    return render_png(background_svg(w, h), w, h)


if __name__ == '__main__':
    Path(sys.argv[1] if len(sys.argv) > 1 else 'livery.png').write_bytes(background())
    print('ok')
