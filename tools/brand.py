"""Turn Telemetry brand mark and assets.

The mark is a piece of track: in along a straight with the car (blue) near the start, a hairpin with red/white kerbs
inside the entry and outside the exit, the exit kerb running on unbroken into the apex of a 90-degree turn down.
Colours are the dashboard's: black, livery lime, the "you" neon blue. Chosen by the user on 2026-10-03 (concept H1).

    python tools/brand.py        # writes docs/brand/*.svg / *.png (needs Chrome and SimHub's DashFonts)
"""
import base64
import math
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'docs' / 'brand'
CHROME = r'C:\Program Files\Google\Chrome\Application\chrome.exe'
FONTS = Path(r'C:\Program Files (x86)\SimHub\DashFonts')

LIME, LIME_DARK, BLUE, BLACK, WHITE, RED = '#A8F000', '#4C7A00', '#2F7DFF', '#060606', '#F4F4F4', '#E3262A'

# Geometry in a 512 x 512 box (also used by the plugin's menu icon: plugin/TurnTelemetry/TurnTelemetry.cs).
W, H, R = 50, 46, 52            # track width, hairpin radius, 90-degree corner radius (centre line)
TOP, LEFT, HAIR_X, STEM_X, BOTTOM = 150, 56, 372, 222, 448
KERB, RUN, EXIT_DEG = 18, 34, 55
CAR_X, CAR_R = LEFT + 58, 32


def geometry():
    """Track path, kerb paths and the car position, as SVG path data."""
    lane = TOP + 2 * H
    track = (f'M {LEFT} {TOP} L {HAIR_X} {TOP} A {H} {H} 0 0 1 {HAIR_X} {lane} L {STEM_X + R} {lane} '
             f'A {R} {R} 0 0 0 {STEM_X} {lane + R} L {STEM_X} {BOTTOM}')
    d = W / 2 + KERB / 2 - 1
    rh, rc, ro, mid = max(4, H - d), R - d, H + d, TOP + H
    ex = HAIR_X + ro * math.cos(math.radians(EXIT_DEG))
    ey = mid + ro * math.sin(math.radians(EXIT_DEG))
    kerbs = [
        # inside the hairpin, from the entry to halfway
        f'M {HAIR_X - RUN} {TOP + d} L {HAIR_X} {TOP + d} A {rh} {rh} 0 0 1 {HAIR_X + rh} {mid}',
        # outside the hairpin on the way out, unbroken into the apex of the 90-degree turn
        f'M {ex:.1f} {ey:.1f} A {ro} {ro} 0 0 1 {HAIR_X} {lane + d} L {STEM_X + R} {lane + d} '
        f'A {rc} {rc} 0 0 0 {STEM_X + d} {lane + R} L {STEM_X + d} {lane + R + RUN}',
    ]
    return track, kerbs


def mark_svg():
    track, kerbs = geometry()
    k = ''.join(f'<path d="{p}" fill="none" stroke="{WHITE}" stroke-width="{KERB}"/>'
                f'<path d="{p}" fill="none" stroke="{RED}" stroke-width="{KERB}" stroke-dasharray="10 10"/>' for p in kerbs)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">'
            f'<rect width="512" height="512" rx="104" fill="{BLACK}"/>{k}'
            f'<path d="{track}" fill="none" stroke="{LIME}" stroke-width="{W}" stroke-linecap="round" stroke-linejoin="round"/>'
            f'<circle cx="{CAR_X}" cy="{TOP}" r="{CAR_R}" fill="{BLUE}" stroke="{BLACK}" stroke-width="12"/></svg>')


FACES = (f'@font-face{{font-family:"EurasiaEx";font-weight:700;src:url("{(FONTS / "EurasiaEx-Bold.ttf").as_uri()}")}}'
         f'@font-face{{font-family:"Oswald";font-weight:700;src:url("{(FONTS / "Oswald-Bold.ttf").as_uri()}")}}')


def _img(svg, size):
    return f'<img src="data:image/svg+xml;base64,{base64.b64encode(svg.encode()).decode()}" width="{size}" height="{size}" style="display:block">'


def _render(html, path, w, h, transparent):
    page = path.with_suffix('.html')
    page.write_text(f'<html><head><style>{FACES}html,body{{margin:0;width:{w}px;height:{h}px;overflow:hidden;'
                    f'background:{"transparent" if transparent else "#000"}}}</style></head><body>{html}</body></html>',
                    encoding='utf-8')
    args = [CHROME, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--allow-file-access-from-files',
            f'--window-size={w},{h}', f'--screenshot={path}', page.as_uri()]
    if transparent:
        args.insert(3, '--default-background-color=00000000')
    subprocess.run(args, check=True, capture_output=True, timeout=60)
    page.unlink()


def _logo(svg, light):
    return (f'<div style="display:flex;align-items:center;gap:34px;height:200px;padding:0 24px">{_img(svg, 168)}<div>'
            f'<div style="font:700 74px EurasiaEx;color:{"#111" if light else "#fff"};letter-spacing:2px;line-height:1">TURN</div>'
            f'<div style="font:700 74px EurasiaEx;color:{LIME_DARK if light else LIME};letter-spacing:2px;line-height:1;'
            f'margin-top:8px">TELEMETRY</div></div></div>')


def _social(svg):
    wedges = (f'<svg width="1280" height="640" style="position:absolute;left:0;top:0">'
              f'<polygon points="0,0 360,0 200,110 100,110 0,200" fill="{LIME}"/>'
              f'<polygon points="1280,0 920,0 1080,110 1180,110 1280,200" fill="{LIME}"/></svg>')
    return (f'<div style="position:relative;width:1280px;height:640px;background:#060606">{wedges}'
            f'<div style="position:absolute;left:100px;top:190px;display:flex;align-items:center;gap:50px">{_img(svg, 260)}<div>'
            f'<div style="font:700 80px EurasiaEx;color:#fff;letter-spacing:3px;line-height:1">TURN</div>'
            f'<div style="font:700 80px EurasiaEx;color:{LIME};letter-spacing:3px;line-height:1;margin-top:10px">TELEMETRY</div>'
            f'<div style="font:700 34px Oswald;color:#B4B4B4;margin-top:28px;letter-spacing:1px">'
            f'TURN-BY-TURN TELEMETRY DASHBOARD FOR SIMHUB</div></div></div>'
            f'<div style="position:absolute;left:100px;bottom:58px;font:700 26px Oswald;color:#6E6E6E;letter-spacing:1px">'
            f'CORNER DELTAS · SECTORS · TRACK LIMITS · TYRES &amp; BRAKES · AC / ACC</div></div>')


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    svg = mark_svg()
    (OUT / 'mark.svg').write_text(svg, encoding='utf-8')
    assets = {
        'icon-24.png': (_img(svg, 24), 24, 24, True), 'icon-64.png': (_img(svg, 64), 64, 64, True),
        'icon-256.png': (_img(svg, 256), 256, 256, True), 'avatar-512.png': (_img(svg, 512), 512, 512, True),
        'logo.png': (_logo(svg, False), 980, 200, True), 'logo-light.png': (_logo(svg, True), 980, 200, True),
        'social-preview.png': (_social(svg), 1280, 640, False),
    }
    for name, (html, w, h, transparent) in assets.items():
        _render(html, OUT / name, w, h, transparent)
        print('wrote', OUT / name)


if __name__ == '__main__':
    main()
