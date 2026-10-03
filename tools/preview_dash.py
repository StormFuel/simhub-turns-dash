"""Approximate PNG preview of a generated dashboard, for layout review without SimHub.

Renders the .djson as absolutely positioned HTML with a sample race state (bindings are not evaluated; the
sample values below stand in for them) and screenshots it with headless Chrome/Edge. Not a substitute for
checking in Dash Studio / on the tablet.

    python tools/preview_dash.py dash/TurnTelemetryDashboard/TurnTelemetryDashboard.djson out.png --theme livery
"""
import base64
import io
import json
import math
import re
import subprocess
import sys
import zipfile

import fontcheck
from pathlib import Path

FONT_DIR = Path(r'C:\Program Files (x86)\SimHub\DashFonts')
FONT_FILES = {'DIN 1451 Std Mittelschrift': 'DINMittelschriftStd.ttf', 'EurasiaEx': 'EurasiaEx-Regular.ttf',
              'Futura': 'futurab.ttf', 'Oswald': 'Oswald-Bold.ttf',
              'Eurostar Black Extended': 'eurostarblackextended.ttf'}
BROWSERS = [Path(r'C:\Program Files\Google\Chrome\Application\chrome.exe'),
            Path(r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe')]

PLUGIN_VERSION = re.search(r'<Version>([^<]+)</Version>',
                           (Path(__file__).resolve().parent.parent / 'plugin' / 'Directory.Build.props').read_text()).group(1)

# Sample state: ACC-like session, inside turn 7, warm tyres, one tyre slightly hot.
TURNS = [(0.03, 0.06), (0.11, 0.14), (0.18, 0.20), (0.26, 0.30), (0.36, 0.38), (0.41, 0.45),
         (0.52, 0.57), (0.63, 0.66), (0.70, 0.73), (0.78, 0.82), (0.86, 0.89), (0.95, 0.02)]
CURRENT_TURN = 6
SECTORS = [(0, 0.33), (0.33, 0.69), (0.69, 1)]
CURRENT_SECTOR = 2
# Track limits: turn 3 this lap, turn 9 earlier in the session.
LIMITS_SAMPLE = {2: 'lap', 8: 'session'}
# Sector sample: S1 a personal best this lap, S2 in progress (last lap's yellow, dimmed), S3 last lap's purple, dimmed.
SECTOR_SAMPLE = {1: ('#FF2EE66B', '#FF000000', 'S1   35.188'), 2: ('#59FFD000', '#FFFFFFFF', 'S2   42.011'),
                 3: ('#59B44BFF', '#FFFFFFFF', 'S3   38.198')}
LAP_POS = 0.545
TYRE_COLORS = {'FL': '#FF2EE66B', 'FR': '#FF2EE66B', 'RL': '#FFFFD60A', 'RR': '#FF2EE66B'}
TEXT = {
    'Session': 'ACC  ·  SILVERSTONE  ·  FERRARI 296 GT3',
    'Lap number': '7', 'Current lap': '1:12.418', 'Delta': '-0.21', 'Best lap': '2:17.542',
    'Focal number': '7', 'Focal name': 'The Loop',
    'Gear': '4', 'Speed': '187', 'Speed unit': 'KM/H', 'Upcoming label': 'NEXT TURN', 'Upcoming': '8',
    'Turn source': 'TURNS · USER · 12',
    'FL temp': '190', 'FR temp': '196', 'RL temp': '202', 'RR temp': '188',
    'FL pressure': '27.1', 'FR pressure': '26.5', 'RL pressure': '26.6', 'RR pressure': '25.8',
    'Temp unit': '°F', 'Compound': 'DRY COMPOUND  ·  ACC-DRY', 'RPM': '7840',
    'TC cut value': '3', 'TC value': '3', 'ABS value': '2', 'BB value': '56.2', 'MAP value': '2',
    'Flag name': 'YELLOW', 'Wipers state': 'OFF', 'Lights state': 'ON', 'Air temp': '81°F', 'Track temp': '96°F',
 'Plugin info': f'TURN TELEMETRY {PLUGIN_VERSION}  ·  ACC', 'Standings count': '20 CARS', 'Limits count': 'TRACK LIMITS  2',
    'Row 1 Position': '1', 'Row 1 Name': 'M. ROSSI', 'Row 1 S1': '35.112', 'Row 1 S2': '41.870', 'Row 1 S3': '38.214', 'Row 1 Best': '1:55.196', 'Row 1 BestDelta': 'FASTEST', 'Row 1 Gap': 'LEADER', 'Row 2 Position': '2', 'Row 2 Name': 'K. TANAKA', 'Row 2 S1': '35.240', 'Row 2 S2': '41.802', 'Row 2 S3': '38.330', 'Row 2 Best': '1:55.402', 'Row 2 BestDelta': '+0.206', 'Row 2 Gap': '+1.8', 'Row 3 Position': '3', 'Row 3 Name': 'L. BAUER', 'Row 3 S1': '35.301', 'Row 3 S2': '42.011', 'Row 3 S3': '38.198', 'Row 3 Best': '1:55.611', 'Row 3 BestDelta': '+0.415', 'Row 3 Gap': '+4.3', 'Row 4 Position': '4', 'Row 4 Name': 'YOU', 'Row 4 S1': '35.188', 'Row 4 S2': '41.954', 'Row 4 S3': '38.402', 'Row 4 Best': '1:55.544', 'Row 4 BestDelta': '+0.348', 'Row 4 Gap': '+6.9', 'Row 5 Position': '5', 'Row 5 Name': 'A. SILVA', 'Row 5 S1': '35.402', 'Row 5 S2': '42.150', 'Row 5 S3': '38.511', 'Row 5 Best': '1:56.063', 'Row 5 BestDelta': '+0.867', 'Row 5 Gap': '+9.2', 'Row 6 Position': '6', 'Row 6 Name': 'J. MARTIN', 'Row 6 S1': '35.517', 'Row 6 S2': '42.098', 'Row 6 S3': '38.620', 'Row 6 Best': '1:56.235', 'Row 6 BestDelta': '+1.039', 'Row 6 Gap': '+12.4', 
}
COLOR_OVERRIDES = {'Row 4 player': '#402F7DFF', 'Row 4 Name': '#FF2F7DFF', 'Row 1 Best': '#FFB44BFF', 'Row 1 S1': '#FFB44BFF',
                   'Row 2 S2': '#FFB44BFF', 'Row 3 S3': '#FFB44BFF',
                   'Flag swatch': '#FFFFD000', 'Flag name': '#FF000000', 'Lights light': '#FFA8F000', 'Lights state': '#FFFFFFFF',
                   'Focal number': '#FF2F7DFF', 'Focal state': '#FF2F7DFF', 'Delta': '#FF2EE66B',
                   'RR pressure': '#FF3B82F6', 'FL pressure': '#FFFF3B30', 'TC light': '#FFFFB020'}
HIDDEN = re.compile(r'^(Plugin banner.*|No lap position|Unsupported notice|No steering|No standings|Limits unavailable|.* zone [IMO]|'
                    r'.* wear.*|.*wrap|Focal hint \d|Focal message|Focal set .*|Focal tap|Reset turns.*|From line.*|Band \d\d (prompt|tap))$')

# --set-mode: Set Turn 1 mode, prompting for Copse (currently numbered 9) with the band highlighted.
SET_MODE = {
    'TEXT': {'Focal state': 'SET TURN 1', 'Focal number': '1?', 'Focal name': 'COPSE',
             'Focal set hint': 'TAP HERE AT TURN 1  ·  NOW 9', 'Set T1 label': 'CANCEL', 'Upcoming': '10',
             'Turn source': 'SET TURN 1 · TAP THE TILE AT TURN 1, OR TAP ITS BAND HERE'},
    'COLOR': {'Focal state': '#FF59D7FF', 'Focal number': '#FF59D7FF', 'Focal name': '#FFF7F9FC',
              'Turn source': '#FF59D7FF'},
    'SHOW': re.compile(r'^(Focal set hint|Focal set frame|Reset turns.*|From line.*|Band 06 prompt)$'),
    'HIDE': re.compile(r'^(Focal current frame|Upcoming label)$'),
}
MODE = None

# --theme neon: sample colours in the Neon palette (themes.py / TyrePalette.Neon).
NEON_SAMPLE = {
    'TYRES': {'FL': '#FF39FF14', 'FR': '#FF39FF14', 'RL': '#FFFFEA00', 'RR': '#FF39FF14'},
    'COLOR': {'Focal number': '#FF2F7DFF', 'Focal state': '#FF2F7DFF', 'Delta': '#FF7CFF00',
              'RR pressure': '#FF00B3FF', 'FL pressure': '#FFFF1744', 'TC light': '#FFFFEA00'},
    'BAND_CURRENT': '#B32F7DFF', 'BAND': '#26FFFFFF', 'BAND_LABEL': '#FF2F7DFF', 'MUTED': '#FF9AA88F',
}
LIVERY_SAMPLE = {
    'TYRES': {'FL': '#FF39FF14', 'FR': '#FF39FF14', 'RL': '#FFFFEA00', 'RR': '#FF39FF14'},
    'COLOR': {'Focal number': '#FF2F7DFF', 'Delta': '#FFA8F000', 'RR pressure': '#FF00B3FF',
              'FL pressure': '#FFFF1744', 'TC light': '#FFFFD000'},
    'BAND_CURRENT': '#B32F7DFF', 'BAND': '#2EFFFFFF', 'BAND_LABEL': '#FF2F7DFF', 'MUTED': '#FFB4B4B4',
}
THEME = None


def css_color(argb):
    a, rgb = int(argb[1:3], 16) / 255, argb[3:]
    return f'rgba({int(rgb[0:2], 16)},{int(rgb[2:4], 16)},{int(rgb[4:6], 16)},{a:.3f})'


def chart_svg(item, n=180):
    name, w, h = item['Name'], item['Width'], item['Height']
    lo, hi = item['Minimum'], item['Maximum']
    pts = []
    for k in range(n):
        t = k / (n - 1) * 6.0
        if name.startswith('Throttle'):
            v = 100 if t < 1.6 or t > 4.2 else max(0, min(100, (t - 2.8) * 70))
        elif name.startswith('Brake'):
            v = 92 * math.exp(-((t - 1.95) / 0.35) ** 2) if 1.5 < t < 2.8 else 0
        elif name.startswith('Steering'):
            v = 0.55 * math.sin((t - 1.9) * 1.1) if 1.9 < t < 4.7 else 0.02
        elif name.startswith('TURN'):
            v = 1 if 1.8 < t < 4.4 else 0
        elif name.startswith('ABS'):
            v = 1 if 1.8 < t < 2.1 else 0
        elif name.startswith('TC'):
            v = 1 if 3.9 < t < 4.1 else 0
        else:
            v = 0
        pts.append(f'{k / (n - 1) * w:.1f},{h - (v - lo) / (hi - lo) * h:.1f}')
    return (f'<svg width="{w}" height="{h}" style="position:absolute;left:0;top:0;overflow:visible">'
            f'<polyline fill="none" stroke="{css_color(item["LineColor"])}" stroke-width="{item["LineTickness"]}" '
            f'points="{" ".join(pts)}"/></svg>')


def radius_css(style):
    r = [style.get(k, 0) for k in ('RadiusTopLeft', 'RadiusTopRight', 'RadiusBottomRight', 'RadiusBottomLeft')]
    return f'border-radius:{r[0]}px {r[1]}px {r[2]}px {r[3]}px;'


def map_svg(item):
    """SimHub draws GeneratedStaticMapItem itself; approximate it with the real ACC Silverstone SimHub map."""
    import trackmap
    pts = trackmap.load(trackmap.find_map('AssettoCorsaCompetizione', 'Silverstone'))
    xs, zs = [q[1] for q in pts], [q[2] for q in pts]
    w, h, pad = item['Width'], item['Height'], 14
    scale = min((w - 2 * pad) / (max(xs) - min(xs)), (h - 2 * pad) / (max(zs) - min(zs)))
    ox = (w - (max(xs) - min(xs)) * scale) / 2
    oz = (h - (max(zs) - min(zs)) * scale) / 2
    def xy(q):
        return ox + (q[1] - min(xs)) * scale, oz + (q[2] - min(zs)) * scale
    line = ' '.join(f'{x:.1f},{y:.1f}' for x, y in map(xy, pts[::4]))
    def at(frac):
        return xy(min(pts, key=lambda q: abs(q[0] - frac % 1)))
    car = at(LAP_POS - 0.465)  # sample position, ACC line offset
    start = xy(pts[0])
    opp = item['OpponentStyle']
    others = ''.join(
        f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{opp["DotRadius"] / 2}" fill="{css_color(opp["DotColor"])}" stroke="#000" stroke-width="1"/>'
        f'<text x="{x:.1f}" y="{y + opp["LabelFontSize"] / 3:.1f}" font-size="{opp["LabelFontSize"]}" text-anchor="middle" font-family="Oswald" font-weight="bold">{pos}</text>'
        for pos, (x, y) in ((pos, at(f)) for pos, f in ((1, 0.30), (2, 0.18), (4, 0.03), (5, 0.72), (6, 0.55))))
    return (f'<svg width="{w}" height="{h}" style="position:absolute;left:0;top:0">'
            f'<polyline fill="none" stroke="{css_color(item["TrackBorderColor"])}" stroke-width="{item["TrackWidth"] + 2 * item["TrackBorderWidth"]}" points="{line}"/>'
            f'<polyline fill="none" stroke="{css_color(item["TrackColor"])}" stroke-width="{item["TrackWidth"]}" points="{line}"/>'
            f'<circle cx="{start[0]:.1f}" cy="{start[1]:.1f}" r="4" fill="{css_color(item["StartLine"]["Color"])}"/>'
            f'{others}'
            f'<circle cx="{car[0]:.1f}" cy="{car[1]:.1f}" r="{item["PlayerStyle"]["DotRadius"] / 2}" fill="{css_color(item["PlayerStyle"]["DotColor"])}" stroke="#000" stroke-width="1"/>'
            f'<text x="{car[0]:.1f}" y="{car[1] + 4:.1f}" font-size="{item["PlayerStyle"]["LabelFontSize"]}" text-anchor="middle" font-family="Oswald" font-weight="bold">3</text>'
            '</svg>')


def render_item(item, images):
    kind = item['$type'].split(',')[0].split('.')[-1]
    name = item['Name']
    left, top, w, h = item['Left'], item['Top'], item['Width'], item['Height']
    if MODE and MODE['HIDE'].match(name):
        return ''
    if HIDDEN.match(name) and not (MODE and MODE['SHOW'].match(name)):
        return ''
    if name == 'Band 06 prompt':
        s0, e0 = TURNS[6]
        return (f'<div style="position:absolute;left:{420 + s0 * 1060}px;top:{top}px;width:{(e0 - s0) * 1060}px;'
                f'height:{h}px;background:#59D7FF"></div>')
    sx, sw = 420, 1060
    m = re.match(r'Band (\d\d) limits$', name)
    if m:
        i = int(m.group(1))
        if i not in LIMITS_SAMPLE:
            return ''
        s0, e0 = TURNS[i]
        color = '#FFFF2D2D' if LIMITS_SAMPLE[i] == 'lap' else '#66FF2D2D'
        return (f'<div style="position:absolute;left:{sx + s0 * sw}px;top:{top}px;width:{max(6, (e0 - s0) * sw)}px;'
                f'height:{h}px;background:{css_color(color)}"></div>')
    m = re.match(r'Band (\d\d)( label)?$', name)
    sx, sw = 420, 1060
    if m:
        i = int(m.group(1))
        if i >= len(TURNS):
            return ''
        s, e = TURNS[i]
        if m.group(2):
            centre = ((s + (e if e >= s else e + 1)) / 2) % 1
            color = (THEME['BAND_LABEL'] if THEME else '#FF2F7DFF') if i == CURRENT_TURN else (THEME['MUTED'] if THEME else '#FF8B97A8')
            if LIMITS_SAMPLE.get(i) == 'lap' and i != CURRENT_TURN:
                color = '#FFFF2D2D'
            return (f'<div style="position:absolute;left:{sx + centre * sw - 20}px;top:{top}px;width:40px;height:{h}px;'
                    f'font-size:{item["FontSize"]}px;color:{css_color(color)};display:flex;align-items:center;'
                    f'justify-content:center">{i + 1}</div>')
        width = (e - s if e >= s else 1 - s) * sw
        fill = (THEME['BAND_CURRENT'] if THEME else '#B32F7DFF') if i == CURRENT_TURN else (THEME['BAND'] if THEME else '#1FFFFFFF')
        return (f'<div style="position:absolute;left:{sx + s * sw}px;top:{top}px;width:{width}px;height:{h}px;'
                f'background:{css_color(fill)}"></div>')
    m = re.match(r'Sector (\d) (segment|label|divider)$', name)
    if m:
        n = int(m.group(1))
        if n > len(SECTORS):
            return ''
        s0, e0 = SECTORS[n - 1]
        current = n == CURRENT_SECTOR
        if m.group(2) == 'divider':
            left = sx + s0 * sw - 1
        elif m.group(2) == 'segment':
            item = dict(item, BackgroundColor=SECTOR_SAMPLE[n][0])
            left, w = sx + s0 * sw + 1, (e0 - s0) * sw - 2
        else:
            left = sx + (s0 + e0) / 2 * sw - 100
            item = dict(item, TextColor=SECTOR_SAMPLE[n][1], Text=SECTOR_SAMPLE[n][2])
    if name == 'Lap cursor':
        left = sx + LAP_POS * sw - 1
    if name.endswith(' single'):
        item = dict(item, BackgroundColor=(THEME['TYRES'] if THEME else TYRE_COLORS)[name.split()[0]])


    style = f'position:absolute;left:{left}px;top:{top}px;width:{w}px;height:{h}px;'
    border = item.get('BorderStyle') or {}
    style += radius_css(border)
    if border.get('BorderTop') and border.get('BorderColor'):
        style += f'box-sizing:border-box;border:{border["BorderTop"]}px solid {css_color(border["BorderColor"])};'
    overrides = dict(COLOR_OVERRIDES, **(THEME['COLOR'] if THEME else {}))
    bg = overrides.get(name, item.get('BackgroundColor')) if kind == 'RectangleItem' else item.get('BackgroundColor')
    if item.get('EnableBlur'):
        style += f'filter:blur({item.get("BlurRadius", 0) / 2}px);'
    if bg:
        style += f'background:{css_color(bg)};'

    if kind == 'ImageItem':
        return f'<img src="data:image/png;base64,{images[item["Image"]]}" style="{style}">'
    if kind == 'ChartItem':
        return f'<div style="{style}">{chart_svg(item)}</div>'
    if kind == 'GeneratedStaticMapItem':
        return f'<div style="{style}">{map_svg(item)}</div>'
    if item.get('IsTextItem'):
        texts = dict(TEXT, **(MODE['TEXT'] if MODE else {}))
        colors = {**COLOR_OVERRIDES, **(THEME['COLOR'] if THEME else {}), **(MODE['COLOR'] if MODE else {})}
        base = name[:-5] if name.endswith(' glow') else name  # glow layers mirror their item
        value = texts.get(base, item.get('Text') or item.get('DesignerText') or '')
        color = colors.get(base, item.get('TextColor') or item.get('GearTextColor') or '#FFFFFFFF')
        just = ['flex-start', 'center', 'flex-end'][item.get('HorizontalAlignment', 0)]
        style += (f'display:flex;align-items:center;justify-content:{just};font-size:{item["FontSize"]}px;'
                  f'color:{css_color(color)};white-space:nowrap;line-height:1;font-family:&quot;{item.get("Font", "DIN")}&quot;,serif;'
                  f'font-weight:{fontcheck.normalise_weight(item.get("FontWeight"))};'
                  f'font-style:{"italic" if item.get("FontStyle") == "Italic" else "normal"};')
        return f'<div style="{style}">{value}</div>'
    return f'<div style="{style}"></div>'


def main(djson, png):
    djson = Path(djson)
    dash = json.loads(djson.read_text(encoding='utf-8'))
    images = {}
    with zipfile.ZipFile(str(djson) + '.ressources') as z:
        for n in z.namelist():
            images[Path(n).stem] = base64.b64encode(z.read(n)).decode()
    w, h = dash['BaseWidth'], dash['BaseHeight']
    body = ''.join(render_item(i, images) for i in dash['Screens'][0]['Items'])
    # Like SimHub's web client: only families declared in Web/FontFaces.css exist; anything else falls back to serif.
    faces = ''.join(f'@font-face{{font-family:"{fam}";font-weight:{w};src:url("{(FONT_DIR / f).as_uri()}")}}'
                    for fam, weights in fontcheck.font_faces().items() for w, f in weights.items() if (FONT_DIR / f).exists())
    html = (f'<html><head><style>{faces}@font-face{{font-family:DIN;src:url("{(FONT_DIR / FONT_FILES["DIN 1451 Std Mittelschrift"]).as_uri()}")}}'
            f'body{{margin:0;width:{w}px;height:{h}px;overflow:hidden;font-family:DIN,sans-serif;background:#0A0B0D}}'
            f'</style></head><body>{body}</body></html>')
    html_path = Path(png).with_suffix('.html')
    html_path.write_text(html, encoding='utf-8')
    browser = next(b for b in BROWSERS if b.exists())
    subprocess.run([str(browser), '--headless=new', '--disable-gpu', '--hide-scrollbars', '--allow-file-access-from-files',
                    f'--window-size={w},{h}', f'--screenshot={Path(png).resolve()}', html_path.resolve().as_uri()],
                   check=True, capture_output=True, timeout=60)
    print('wrote', png)


if __name__ == '__main__':
    if '--theme' in sys.argv:
        i = sys.argv.index('--theme')
        THEME = {'neon': NEON_SAMPLE, 'livery': LIVERY_SAMPLE}.get(sys.argv[i + 1])
        del sys.argv[i:i + 2]
    if '--set-mode' in sys.argv:
        sys.argv.remove('--set-mode')
        MODE = SET_MODE
        if THEME:
            MODE = dict(SET_MODE, COLOR={k: '#FF7CFF00' for k in ('Focal state', 'Focal number', 'Turn source')})
    main(sys.argv[1], sys.argv[2])
