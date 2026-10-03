"""Read SimHub track maps (.shtl) and find corners from track geometry.

SimHub stores recorded/downloaded maps under PluginsData/<GameName>/MapRecords[Cloud]/<TrackIdWithConfig>[-<length>].shtl:
gzipped JSON whose CarCoordinates are world positions [x, y, z] tagged with p, the lap fraction in that game's own
TrackPositionPercent scale. Corners found here line up exactly with what the plugin sees in that sim.

This is the authoring prototype of plugin/TurnTelemetry.Core/Turns/TrackGeometry.cs; keep the two in step.

    python tools/trackmap.py AssettoCorsaCompetizione Silverstone
"""
import gzip
import json
import math
import sys
from pathlib import Path

SIMHUB = Path(r'C:\Program Files (x86)\SimHub')

STEP_M = 2.0            # resample spacing along the lap
SMOOTH_M = 24.0         # curvature smoothing window (full width)
ENTER_RADIUS_M = 350.0  # a corner starts where the radius drops below this...
EXIT_RADIUS_M = 700.0   # ...and continues until it rises above this (hysteresis)
MIN_TURN_DEG = 18.0     # total heading change for a bend to count as a turn
MERGE_GAP_M = 25.0      # same-direction corners closer than this are one corner
SPLIT_RATIO = 0.45      # split a same-direction corner where curvature dips below this share of both apexes
SPLIT_MIN_M = 40.0      # ...and each part spans at least this far


def find_map(game, track):
    """Prefer the cloud map (averaged over many laps), then the locally recorded one."""
    base = SIMHUB / 'PluginsData' / game
    # Cloud files can be empty (seen: mugello-5196.84.shtl, 0 bytes); skip them.
    cloud = sorted(p for p in (base / 'MapRecordsCloud').glob(f'{track}-*.shtl')
                   if p.stat().st_size > 0 and _is_number(p.stem[len(track) + 1:]))
    if cloud:
        return cloud[0]
    local = base / 'MapRecords' / f'{track}.shtl'
    return local if local.exists() else None


def _is_number(text):
    try:
        float(text)
        return True
    except ValueError:
        return False


def load(path):
    data = json.loads(gzip.open(path).read())
    pts = sorted(((c['p'], c['Value'][0], c['Value'][2]) for c in data['CarCoordinates']), key=lambda t: t[0])
    return pts


def resample(pts):
    """Even spacing along the path; returns lists of (p, x, z) and total length."""
    dist = [0.0]
    for a, b in zip(pts, pts[1:]):
        dist.append(dist[-1] + math.hypot(b[1] - a[1], b[2] - a[2]))
    total = dist[-1] + math.hypot(pts[0][1] - pts[-1][1], pts[0][2] - pts[-1][2])
    out = []
    j = 0
    s = 0.0
    while s < dist[-1]:
        while dist[j + 1] < s:
            j += 1
        t = (s - dist[j]) / max(1e-9, dist[j + 1] - dist[j])
        a, b = pts[j], pts[j + 1]
        out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t))
        s += STEP_M
    return out, total


def curvature(samples):
    n = len(samples)
    heading = []
    for i in range(n):
        a, b = samples[i - 1], samples[(i + 1) % n]
        heading.append(math.atan2(b[2] - a[2], b[1] - a[1]))
    k = []
    for i in range(n):
        d = heading[(i + 1) % n] - heading[i - 1]
        d = (d + math.pi) % (2 * math.pi) - math.pi
        k.append(d / (2 * STEP_M))
    w = max(1, int(SMOOTH_M / STEP_M / 2))
    return [sum(k[(i + j) % n] for j in range(-w, w + 1)) / (2 * w + 1) for i in range(n)]


def corners(samples, k):
    """Regions of |curvature| above 1/ENTER (extended to 1/EXIT), split at direction changes."""
    n = len(samples)
    enter, leave = 1 / ENTER_RADIUS_M, 1 / EXIT_RADIUS_M
    regions = []
    i = 0
    while i < n:
        if abs(k[i]) > enter:
            sign = 1 if k[i] > 0 else -1
            a = i
            while a > 0 and k[a - 1] * sign > leave and (not regions or a - 1 > regions[-1][1]):
                a -= 1
            b = i
            while b + 1 < n and k[b + 1] * sign > leave:
                b += 1
            regions.append([a, b, sign])
            i = b + 1
        else:
            i += 1
    merged = []
    gap = int(MERGE_GAP_M / STEP_M)
    for r in regions:
        if merged and merged[-1][2] == r[2] and r[0] - merged[-1][1] <= gap:
            merged[-1][1] = r[1]
        else:
            merged.append(r)
    split = []
    min_len = int(SPLIT_MIN_M / STEP_M)
    for a, b, sign in merged:
        split.extend(_split_double_apex(k, a, b, sign, min_len))
    out = []
    for a, b, sign in split:
        turn = abs(sum(k[a:b + 1])) * STEP_M
        if math.degrees(turn) < MIN_TURN_DEG:
            continue
        rmin = 1 / max(abs(x) for x in k[a:b + 1])
        apex = max(range(a, b + 1), key=lambda j: abs(k[j]))
        out.append({
            'start': samples[a][0], 'end': samples[b][0], 'apex': samples[apex][0],
            # SimHub world coordinates are left-handed: positive heading change is a right turn
            # (validated against AC Silverstone's named sections).
            'dir': 'R' if sign > 0 else 'L',
            'deg': math.degrees(turn), 'rmin': rmin,
            'apex_m': apex * STEP_M,
        })
    return out


def _split_double_apex(k, a, b, sign, min_len):
    """Split at the deepest curvature dip between two apexes (e.g. The Loop / Aintree, Club 17/18)."""
    if b - a < 2 * min_len:
        return [(a, b, sign)]
    best = None
    for m in range(a + min_len, b - min_len + 1):
        left = max(abs(x) for x in k[a:m])
        right = max(abs(x) for x in k[m + 1:b + 1])
        dip = abs(k[m])
        if dip < SPLIT_RATIO * min(left, right) and (best is None or dip < abs(k[best])):
            best = m
    if best is None:
        return [(a, b, sign)]
    return _split_double_apex(k, a, best, sign, min_len) + _split_double_apex(k, best + 1, b, sign, min_len)


def detect(game, track):
    path = find_map(game, track)
    if not path:
        raise SystemExit(f'no SimHub map for {game}/{track}')
    samples, total = resample(load(path))
    return corners(samples, curvature(samples)), total, path


if __name__ == '__main__':
    found, length, path = detect(sys.argv[1], sys.argv[2])
    print(f'{path.name}: {length:.0f} m, {len(found)} corners')
    for i, c in enumerate(found, 1):
        print(f"{i:2d}  {c['dir']}  apex {c['apex']:.3f} ({c['apex_m']:5.0f} m)  {c['start']:.3f}-{c['end']:.3f}"
              f"  {c['deg']:5.0f} deg  rmin {c['rmin']:5.0f} m")
