"""Circuit templates: curated official turns, re-used for the same circuit in other sims.

    python tools/circuits.py build      # data/turns curated files -> data/circuits/<id>.json templates
    python tools/circuits.py validate   # re-create curated ACC files from AC templates and compare

A template stores each official turn as a fraction of the reference lap plus its direction. For another sim's track,
the plugin detects corners on that sim's SimHub map, finds the start/finish offset that best lines the template's
corners up with the detected ones (directions must agree), and accepts it only when most turns match
(plugin/TurnTelemetry.Core/Turns/CircuitTemplates.cs; keep the two in step).
"""
import json
import math
import sys
from pathlib import Path

import trackmap

ROOT = Path(__file__).resolve().parent.parent
TURNS = ROOT / 'data' / 'turns'
OUT = ROOT / 'data' / 'circuits'

# Curated reference file -> template id. The reference sim's file defines the turns for every other sim.
REFERENCES = {
    'silverstone-gp': ('AssettoCorsa', 'ks_silverstone-gp'),
    'monza': ('AssettoCorsa', 'monza'),
    'spa': ('AssettoCorsa', 'fn_spa-gp'),
    'imola': ('AssettoCorsa', 'imola'),
    'nurburgring-gp': ('AssettoCorsa', 'ks_nurburgring-layout_gp_a'),
    'barcelona-2007': ('AssettoCorsa', 'ks_barcelona-layout_gp'),
    'laguna-seca': ('AssettoCorsa', 'ks_laguna_seca'),
    'mugello': ('AssettoCorsa', 'mugello'),
    'cota': ('AssettoCorsa', 'acu_unitedstates-a'),
}

LENGTH_TOLERANCE = 0.03   # candidate templates must be within 3% of the map length
MATCH_WINDOW = 0.012      # a template turn matches a detected corner of the same direction within this lap fraction
MIN_SCORE = 0.8           # accept a template at this score (right circuits >= 0.97, wrong <= 0.60 on validation)


def turning(samples, k, start, end):
    """(direction, degrees) from the net curvature over [start, end] (wrapping)."""
    total = sum(k[i] for i, s in enumerate(samples) if (start <= s[0] <= end if start <= end else s[0] >= start or s[0] <= end))
    return ('R' if total > 0 else 'L'), abs(total) * trackmap.STEP_M * 180 / math.pi


def build():
    OUT.mkdir(parents=True, exist_ok=True)
    for cid, (game, track) in REFERENCES.items():
        curated = json.loads((TURNS / game / f'{track}.json').read_text(encoding='utf-8'))
        samples, length = trackmap.resample(trackmap.load(trackmap.find_map(game, track)))
        k = trackmap.curvature(samples)
        turns = []
        for t in curated['turns']:
            mid = (t['start'] + ((t['end'] - t['start']) % 1.0) / 2) % 1.0
            d, deg = turning(samples, k, t['start'], t['end'])
            turns.append({'label': t['label'], 'name': t.get('name'), 'dir': d, 'deg': round(deg),
                          'start': t['start'], 'end': t['end'], 'mid': round(mid, 4)})
        out = {'schema': 1, 'id': cid, 'circuit': curated.get('trackName'), 'lengthMeters': round(length),
               'reference': f'{game}/{track}', 'source': curated.get('source'), 'turns': turns}
        (OUT / f'{cid}.json').write_text(json.dumps(out, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
        print(f'{cid}: {len(turns)} turns from {game}/{track}')


def cyc(d):
    return (d + 0.5) % 1.0 - 0.5


def similar(t, c):
    """Same direction and comparable angle (within a factor of 2, ignoring small kinks)."""
    if c['dir'] != t['dir']:
        return False
    big, small = max(t['deg'], c['deg']), min(t['deg'], c['deg'])
    return big < 30 or big <= 2 * small


def inside(c, target, margin=0.004):
    """Target lap fraction lies within the detected corner (wrapping), with a small margin."""
    span = (c['end'] - c['start']) % 1.0
    return (target - c['start'] + margin) % 1.0 <= span + 2 * margin


def score_at(template, corners, offset):
    """A template turn matches a same-direction corner that contains it, or a similar corner whose apex is within
    MATCH_WINDOW (curated turns can be parts of one detected corner, e.g. COTA 16-18)."""
    matched, used, residuals = 0, set(), []
    for u in template['turns']:
        target = (u['mid'] - offset) % 1.0
        options = []
        for i, c in enumerate(corners):
            if c['dir'] != u['dir']:
                continue
            d = abs(cyc(c['apex'] - target))
            if inside(c, target) or (similar(u, c) and d < MATCH_WINDOW):
                options.append((d, i))
        if options:
            d, i = min(options)
            matched += 1
            used.add(i)
            if similar(u, corners[i]) and d < MATCH_WINDOW:
                residuals.append(cyc(corners[i]['apex'] - target))
    recall = matched / len(template['turns'])
    precision = len(used) / max(1, len(corners))
    f1 = 0 if matched == 0 else 2 * recall * precision / (recall + precision)
    return f1, recall, precision, residuals


def align(template, corners):
    """Best start/finish offset (template fraction f maps to (f - offset) % 1). Returns (f1, recall, precision, offset)."""
    best = None
    for t in template['turns']:
        for c in corners:
            if not similar(t, c):
                continue
            offset = cyc(t['mid'] - c['apex'])
            f1, recall, precision, residuals = score_at(template, corners, offset)
            if best is None or f1 > best[0]:
                refined = cyc(offset - (sum(residuals) / len(residuals) if residuals else 0))
                best = (f1, recall, precision, refined)
    return best


def apply(template, game, track):
    corners, length, _ = trackmap.detect(game, track)
    if abs(length - template['lengthMeters']) > LENGTH_TOLERANCE * template['lengthMeters']:
        return None, 0, 0
    share, _, _, offset = align(template, corners)
    turns = [{'label': t['label'], 'name': t['name'],
              'start': round((t['start'] - offset) % 1.0, 4), 'end': round((t['end'] - offset) % 1.0, 4)}
             for t in template['turns']]
    return sorted(turns, key=lambda t: t['start']), share, offset


def validate():
    cases = [('silverstone-gp', 'AssettoCorsaCompetizione', 'Silverstone'), ('monza', 'AssettoCorsaCompetizione', 'monza')]
    for cid, game, track in cases:
        template = json.loads((OUT / f'{cid}.json').read_text(encoding='utf-8'))
        turns, share, offset = apply(template, game, track)
        curated = {t['label']: t for t in json.loads((TURNS / game / f'{track}.json').read_text(encoding='utf-8'))['turns']}
        errors = []
        for t in turns:
            c = curated[t['label']]
            mid_t = (t['start'] + ((t['end'] - t['start']) % 1) / 2) % 1
            mid_c = (c['start'] + ((c['end'] - c['start']) % 1) / 2) % 1
            errors.append(abs(cyc(mid_t - mid_c)))
        print(f'{cid} -> {game}/{track}: score {share:.2f}, offset {offset:.4f}, '
              f'turn-centre error mean {sum(errors) / len(errors) * 100:.2f}% max {max(errors) * 100:.2f}% of lap')
    # Negative checks: every template against every map of similar length that is a different circuit.
    maps = [('AssettoCorsaCompetizione', 'Silverstone', 'silverstone-gp'), ('AssettoCorsaCompetizione', 'monza', 'monza')] +            [(g, t, cid) for cid, (g, t) in REFERENCES.items()]
    worst = 0
    for game, track, own in maps:
        corners, length, _ = trackmap.detect(game, track)
        for path in sorted(OUT.glob('*.json')):
            template = json.loads(path.read_text(encoding='utf-8'))
            if template['id'] == own or abs(length - template['lengthMeters']) > LENGTH_TOLERANCE * template['lengthMeters']:
                continue
            f1, recall, precision, _ = align(template, corners)
            worst = max(worst, f1)
            print(f'  {template["id"]} on {game}/{track}: score {f1:.2f} (recall {recall:.0%}, precision {precision:.0%})')
    print(f'worst wrong-circuit score {worst:.2f}; acceptance threshold must be above this')


if __name__ == '__main__':
    {'build': build, 'validate': validate}[sys.argv[1]]()
