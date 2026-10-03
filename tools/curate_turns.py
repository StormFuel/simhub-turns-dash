"""Build curated turn files (official turn numbers) from SimHub track maps.

Each spec in data/curation/<GameName>/<track>.json lists the circuit's official turns in lap order and says where each
one is, in terms of the corners tools/trackmap.py detects on SimHub's map for that game:

    {"label": "4", "name": "The Loop", "dir": "L", "corner": 4}          detected corner 4 (1-based)
    {"label": "17", "name": "Club", "corner": 16, "part": [0, 0.5]}      first half of detected corner 16
    {"label": "10", "name": "Maggotts", "range": [0.592, 0.603]}         explicit lap fractions (missed flick)

"dir" (optional) is checked against the detected direction. Output: data/turns/<GameName>/<track>.json, which the
plugin embeds and prefers over its automatic base.

    python tools/curate_turns.py report AssettoCorsa ks_silverstone-gp   # detected corners + AC section names
    python tools/curate_turns.py build                                   # all specs -> data/turns
"""
import json
import sys
from pathlib import Path

import trackmap

ROOT = Path(__file__).resolve().parent.parent
SPECS = ROOT / 'data' / 'curation'
OUT = ROOT / 'data' / 'turns'
AC_TRACKS = Path(r'C:\Program Files (x86)\Steam\steamapps\common\assettocorsa\content\tracks')


def ac_sections(track_id):
    """AC sections.ini (name, start, end) for a SimHub track id such as ks_barcelona-layout_gp."""
    folder, layout = track_id, None
    if not (AC_TRACKS / folder).exists() and '-' in track_id:
        folder, layout = track_id.split('-', 1)
    for path in ([AC_TRACKS / folder / layout / 'data' / 'sections.ini'] if layout else []) + [AC_TRACKS / folder / 'data' / 'sections.ini']:
        if path.exists():
            out, cur = [], {}
            for line in path.read_text(encoding='utf-8', errors='replace').splitlines():
                line = line.strip()
                if line.startswith('['):
                    cur = {}
                    out.append(cur)
                elif '=' in line:
                    k, v = line.split('=', 1)
                    cur[k.strip().upper()] = v.strip()
            return [(s.get('TEXT', ''), float(s['IN']), float(s['OUT'])) for s in out if 'IN' in s and 'OUT' in s]
    return []


def overlaps(a0, a1, b0, b1):
    return min(a1, b1) - max(a0, b0) > 0


def report(game, track):
    corners, length, path = trackmap.detect(game, track)
    sections = ac_sections(track) if game == 'AssettoCorsa' else []
    print(f'{game}/{track}: {path.name}, {length:.0f} m, {len(corners)} corners')
    for i, c in enumerate(corners, 1):
        names = [n for n, s, e in sections if overlaps(c['start'], c['end'], s, e)]
        print(f"{i:2d} {c['dir']} apex {c['apex']:.3f} ({c['apex_m']:5.0f} m) {c['start']:.3f}-{c['end']:.3f} "
              f"{c['deg']:4.0f} deg r{c['rmin']:4.0f}  {' / '.join(names)}")
    if sections:
        print('sections:', '; '.join(f'{n} {s:.3f}-{e:.3f}' for n, s, e in sections))


def build_one(spec_path):
    spec = json.loads(spec_path.read_text(encoding='utf-8'))
    game, track = spec['game'], spec['track']
    corners, length, path = trackmap.detect(game, track)
    turns, problems = [], []
    if spec.get('fromSections'):
        # The sim's own sections.ini already carries official numbers ("Turn 7"): take them as they are.
        import re
        for name, s0, s1 in ac_sections(track):
            m = re.match(r'^(?:turn|t|corner)\s*[-#]?\s*(\d+)\s*([a-z]?)', name.strip(), re.I)
            if m:
                spec['turns'].append({'label': str(int(m.group(1))) + m.group(2).upper(), 'name': name.strip(), 'range': [s0, s1]})
    for t in spec['turns']:
        if 'range' in t:
            start, end = t['range']
        else:
            c = corners[t['corner'] - 1]
            start, end = c['start'], c['end']
            span = (end - start) % 1.0
            if 'part' in t:
                a, b = t['part']
                start, end = (start + span * a) % 1.0, (start + span * b) % 1.0
            if t.get('dir') and t['dir'] != c['dir']:
                problems.append(f"turn {t['label']} {t.get('name')}: expected {t['dir']}, detected {c['dir']}")
        turns.append({'label': t['label'], 'name': t.get('name'), 'start': round(start, 4), 'end': round(end, 4)})

    # Lap order must follow the official numbering (cyclically, starting wherever the sim's line is).
    ordered = sorted(turns, key=lambda t: t['start'])
    first = min(range(len(turns)), key=lambda i: turns[i]['start'])
    if [t['label'] for t in turns[first:] + turns[:first]] != [t['label'] for t in ordered]:
        problems.append('turns are not in lap order: ' + ', '.join(t['label'] for t in ordered))
    for a, b in zip(ordered, ordered[1:]):
        if b['start'] < a['end'] - 1e-6:
            problems.append(f"turns {a['label']} and {b['label']} overlap")
    if problems:
        raise SystemExit(f'{spec_path.name}:\n  ' + '\n  '.join(problems))

    out = {
        'schema': 1, 'game': game, 'track': track, 'trackName': spec.get('circuit'),
        'lengthMeters': round(length),
        'source': f"official numbering ({'; '.join(spec.get('sources', []))}); positions from SimHub map {path.name}",
        'turns': ordered,
    }
    target = OUT / game / f'{track}.json'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(out, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f'{game}/{track}: {len(ordered)} turns -> {target.relative_to(ROOT)}')


if __name__ == '__main__':
    if sys.argv[1] == 'report':
        report(sys.argv[2], sys.argv[3])
    elif sys.argv[1] == 'build':
        specs = sorted(SPECS.glob('*/*.json')) if len(sys.argv) < 3 else [Path(a) for a in sys.argv[2:]]
        for spec in specs:
            build_one(spec)


def probe(game, track, lo=0.0, hi=1.0):
    """Every bend of 5+ degrees with radius under 1500 m in [lo, hi], unmerged: for locating flat-out kinks."""
    path = trackmap.find_map(game, track)
    samples, _ = trackmap.resample(trackmap.load(path))
    k = trackmap.curvature(samples)
    n = len(samples)
    i = 0
    while i < n:
        if abs(k[i]) > 1 / 1500:
            sign = 1 if k[i] > 0 else -1
            a = i
            while i + 1 < n and k[i + 1] * sign > 1 / 3000:
                i += 1
            seg = k[a:i + 1]
            deg = abs(sum(seg)) * trackmap.STEP_M * 57.2958
            apex = a + max(range(len(seg)), key=lambda j: abs(seg[j]))
            if deg >= 5 and lo <= samples[apex][0] <= hi:
                print(f"  {'R' if sign > 0 else 'L'} {samples[a][0]:.3f}-{samples[i][0]:.3f} apex {samples[apex][0]:.3f} "
                      f"{deg:4.0f} deg r{1 / max(abs(x) for x in seg):5.0f}")
        i += 1


if __name__ == '__main__' and sys.argv[1] == 'probe':
    probe(sys.argv[2], sys.argv[3], *(float(a) for a in sys.argv[4:6]))


def dips(game, track, lo, hi):
    """Curvature profile (radius) in [lo, hi] every ~10 m, to choose split points inside merged corners."""
    path = trackmap.find_map(game, track)
    samples, _ = trackmap.resample(trackmap.load(path))
    k = trackmap.curvature(samples)
    row = []
    for i in range(0, len(samples), 5):
        if lo <= samples[i][0] <= hi:
            row.append(f"{samples[i][0]:.3f}:{min(9999, 1 / max(1e-6, abs(k[i]))):4.0f}")
    print('  '.join(row))


if __name__ == '__main__' and sys.argv[1] == 'dips':
    dips(sys.argv[2], sys.argv[3], float(sys.argv[4]), float(sys.argv[5]))
