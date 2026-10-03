"""Font checks for SimHub dashboards.

Dash Studio (WPF) finds a font by the family name inside the font file; the tablet's web client only knows the
families declared in SimHub\\Web\\FontFaces.css and falls back to the browser's default serif for anything else.
So every family an item uses must be (1) the nameID-1 family of a file in SimHub\\DashFonts, and (2) a font-family in
FontFaces.css with a face for the item's weight. Also checks that the face has a glyph for every character shown.

    python tools/fontcheck.py                        # report on the families and files of interest
    python tools/fontcheck.py dash/X/X.djson         # check a generated dashboard (exit 1 on failure)
"""
import json
import re
import struct
import sys
from pathlib import Path

SIMHUB = Path(r'C:\Program Files (x86)\SimHub')
DASH_FONTS = SIMHUB / 'DashFonts'
FONT_FACES = SIMHUB / 'Web' / 'FontFaces.css'
WEB_FONTS = SIMHUB / 'Web' / 'Fonts'


def _tables(data):
    num = struct.unpack('>H', data[4:6])[0]
    tables = {}
    for i in range(num):
        tag, _, offset, length = struct.unpack('>4sIII', data[12 + 16 * i:28 + 16 * i])
        tables[tag.decode('latin-1')] = (offset, length)
    return tables


def family_name(path):
    """nameID 1 (font family), preferring the Windows Unicode record."""
    data = Path(path).read_bytes()
    offset, _ = _tables(data)['name']
    count, string_offset = struct.unpack('>HH', data[offset + 2:offset + 6])
    found = {}
    for i in range(count):
        platform, encoding, language, name_id, length, str_off = struct.unpack(
            '>HHHHHH', data[offset + 6 + 12 * i:offset + 18 + 12 * i])
        if name_id != 1:
            continue
        raw = data[offset + string_offset + str_off:offset + string_offset + str_off + length]
        found[platform] = raw.decode('utf-16-be' if platform in (0, 3) else 'latin-1', errors='replace')
    return found.get(3) or found.get(0) or found.get(1)


def codepoints(path):
    """Characters mapped by the cmap (formats 4 and 12)."""
    data = Path(path).read_bytes()
    offset, _ = _tables(data)['cmap']
    num = struct.unpack('>H', data[offset + 2:offset + 4])[0]
    cps = set()
    for i in range(num):
        platform, encoding, sub = struct.unpack('>HHI', data[offset + 4 + 8 * i:offset + 12 + 8 * i])
        base = offset + sub
        fmt = struct.unpack('>H', data[base:base + 2])[0]
        if fmt == 4:
            segx2 = struct.unpack('>H', data[base + 6:base + 8])[0]
            seg = segx2 // 2
            ends = struct.unpack(f'>{seg}H', data[base + 14:base + 14 + segx2])
            starts = struct.unpack(f'>{seg}H', data[base + 16 + segx2:base + 16 + 2 * segx2])
            deltas = struct.unpack(f'>{seg}h', data[base + 16 + 2 * segx2:base + 16 + 3 * segx2])
            ro_pos = base + 16 + 3 * segx2
            ranges = struct.unpack(f'>{seg}H', data[ro_pos:ro_pos + segx2])
            for k in range(seg):
                for c in range(starts[k], ends[k] + 1):
                    if c == 0xFFFF:
                        continue
                    if ranges[k] == 0:
                        glyph = (c + deltas[k]) & 0xFFFF
                    else:
                        addr = ro_pos + 2 * k + ranges[k] + 2 * (c - starts[k])
                        glyph = struct.unpack('>H', data[addr:addr + 2])[0]
                        glyph = (glyph + deltas[k]) & 0xFFFF if glyph else 0
                    if glyph:
                        cps.add(c)
        elif fmt == 12:
            groups = struct.unpack('>I', data[base + 12:base + 16])[0]
            for g in range(groups):
                start, end, _ = struct.unpack('>III', data[base + 16 + 12 * g:base + 28 + 12 * g])
                cps.update(range(start, end + 1))
    return cps

def _glyph_map(path):
    data = Path(path).read_bytes()
    t = _tables(data)
    offset, _ = t['cmap']
    num = struct.unpack('>H', data[offset + 2:offset + 4])[0]
    gmap = {}
    for i in range(num):
        platform, encoding, sub = struct.unpack('>HHI', data[offset + 4 + 8 * i:offset + 12 + 8 * i])
        base = offset + sub
        if struct.unpack('>H', data[base:base + 2])[0] != 4 or platform != 3:
            continue
        segx2 = struct.unpack('>H', data[base + 6:base + 8])[0]
        seg = segx2 // 2
        ends = struct.unpack(f'>{seg}H', data[base + 14:base + 14 + segx2])
        starts = struct.unpack(f'>{seg}H', data[base + 16 + segx2:base + 16 + 2 * segx2])
        deltas = struct.unpack(f'>{seg}h', data[base + 16 + 2 * segx2:base + 16 + 3 * segx2])
        ro_pos = base + 16 + 3 * segx2
        ranges = struct.unpack(f'>{seg}H', data[ro_pos:ro_pos + segx2])
        for k in range(seg):
            for c in range(starts[k], ends[k] + 1):
                if c == 0xFFFF:
                    continue
                if ranges[k] == 0:
                    g = (c + deltas[k]) & 0xFFFF
                else:
                    addr = ro_pos + 2 * k + ranges[k] + 2 * (c - starts[k])
                    g = struct.unpack('>H', data[addr:addr + 2])[0]
                    g = (g + deltas[k]) & 0xFFFF if g else 0
                if g:
                    gmap[c] = g
    units = struct.unpack('>H', data[t['head'][0] + 18:t['head'][0] + 20])[0]
    nh = struct.unpack('>H', data[t['hhea'][0] + 34:t['hhea'][0] + 36])[0]
    ho = t['hmtx'][0]
    adv = [struct.unpack('>H', data[ho + 4 * i:ho + 4 * i + 2])[0] for i in range(nh)]
    return gmap, adv, units


def text_width(path, text, size):
    """Advance width of text in pixels at font size (no kerning)."""
    gmap, adv, units = _glyph_map(path)
    total = 0
    for ch in text:
        g = gmap.get(ord(ch), 0)
        total += adv[min(g, len(adv) - 1)]
    return total * size / units


def font_faces(css_path=FONT_FACES):
    """{family: {weight: file name}} from FontFaces.css (weights normalised: bold=700, normal=400)."""
    css = Path(css_path).read_text(encoding='utf-8', errors='replace')
    faces = {}
    for block in re.findall(r'@font-face\s*{([^}]*)}', css):
        fam = re.search(r"font-family\s*:\s*['\"]?([^;'\"]+)", block)
        src = re.search(r"url\(['\"]?([^)'\"]+)", block)
        weight = re.search(r'font-weight\s*:\s*([^;]+)', block)
        style = re.search(r'font-style\s*:\s*([^;]+)', block)
        if not fam or not src or (style and 'italic' in style.group(1)):
            continue
        w = normalise_weight(weight.group(1) if weight else '400')
        faces.setdefault(fam.group(1).strip(), {})[w] = Path(src.group(1)).name
    return faces


def normalise_weight(w):
    w = str(w or '400').strip().lower()
    return {'normal': 400, 'regular': 400, 'bold': 700, 'black': 900, 'medium': 500, 'semibold': 600,
            'light': 300}.get(w, int(w) if w.isdigit() else 400)


def dash_families():
    """{family name inside the file: [file names]} for SimHub\\DashFonts."""
    out = {}
    for f in sorted(DASH_FONTS.glob('*.[tToO][tT][fF]')):
        try:
            out.setdefault(family_name(f), []).append(f.name)
        except Exception:
            pass
    return out


def check_dash(djson_path):
    """Problems with the fonts a generated dashboard uses: unknown to the web client, not a DashFonts family,
    or missing glyphs for characters it displays. Returns a list of messages (empty = OK)."""
    dash = json.loads(Path(djson_path).read_text(encoding='utf-8-sig'))
    faces, families = font_faces(), dash_families()
    uses = {}

    def walk(o):
        if isinstance(o, dict):
            for key in ('Font', 'LabelFont'):
                fam = o.get(key)
                if isinstance(fam, str):
                    weight = normalise_weight(o.get('FontWeight') or ('700' if key == 'LabelFont' else '400'))
                    text = ''.join(str(o.get(k) or '') for k in ('Text', 'DesignerText'))
                    text += ''.join(str(b.get('Formula', {}).get('Expression') or b.get('Formula', {}).get('JSExt') or '')
                                    for b in (o.get('Bindings') or {}).values() if isinstance(b, dict))
                    uses.setdefault((fam, weight), []).append((o.get('Name'), text))
            for v in o.values():
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)

    walk(dash)
    problems = []
    for (fam, weight), items in sorted(uses.items()):
        names = ', '.join(sorted({n for n, _ in items if n})[:4])
        if fam not in families:
            problems.append(f'{fam!r}: not the family name of any font file in DashFonts (used by {names})')
        web = faces.get(fam)
        if not web:
            problems.append(f'{fam!r}: not in Web/FontFaces.css, so the tablet shows a serif (used by {names})')
            continue
        file = web.get(weight) or web.get(min(web, key=lambda w: abs(w - weight)))
        path = WEB_FONTS / file if (WEB_FONTS / file).exists() else DASH_FONTS / file
        if path.exists():
            have = codepoints(path)
            for name, text in items:
                for ch in set(_displayed(text)):
                    if ord(ch) >= 0x20 and ord(ch) not in have:
                        problems.append(f'{fam!r} ({file}) has no glyph for {ch!r} U+{ord(ch):04X} (item {name})')
    return problems


def _displayed(text):
    """Characters an item can display: its static text plus quoted string literals in its formulas."""
    shown = []
    for literal in re.findall(r"'((?:[^'\\]|\\.)*)'", text):
        literal = re.sub(r'\\u([0-9a-fA-F]{4})', lambda m: chr(int(m.group(1), 16)), literal)
        if literal.startswith('#') or literal.startswith('DataCorePlugin') or literal.startswith('TurnTelemetry'):
            continue   # colours and property names
        shown.append(literal)
    plain = re.sub(r"'(?:[^'\\]|\\.)*'", '', text)
    if not re.search(r'[\[\]$(){};=]', plain):   # static text, not a formula
        shown.append(plain)
    return ''.join(shown) + '0123456789'


if __name__ == '__main__':
    if len(sys.argv) > 1:
        issues = check_dash(sys.argv[1])
        print('\n'.join(issues) if issues else 'fonts OK')
        sys.exit(1 if issues else 0)
    faces, families = font_faces(), dash_families()
    for file in ['futurab.ttf', 'eurostarblackextended.ttf', 'Oswald-Bold.ttf', 'EurasiaEx-Bold.ttf',
                 'EurasiaEx-Regular.ttf', 'DINMittelschriftStd.ttf']:
        path = DASH_FONTS / file
        fam = family_name(path) if path.exists() else '(missing)'
        print(f'{file:30} file family={fam!r:32} in FontFaces.css={fam in faces}  css weights={sorted(faces.get(fam, {}))}')
