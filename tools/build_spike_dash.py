"""Generate the Phase 0 live-trace spike dashboard (docs/plan.md, Phase 0).

Writes dash/LiveTraceSpike/{LiveTraceSpike.djson, .djson.ressources, .djson.metadata}.
With --install, also copies the folder into SimHub's DashTemplates directory.

Item JSON shapes mirror dashboards saved by SimHub 9.11 (e.g. the ChartItem
pair in the "Button Box" template), so Dash Studio can open and edit the result.
"""
import argparse
import hashlib
import io
import json
import shutil
import uuid
import zipfile
from pathlib import Path

import carbon

ROOT = Path(__file__).resolve().parent.parent
NAME = 'LiveTraceSpike'
OUT = ROOT / 'dash' / NAME
SIMHUB_TEMPLATES = Path(r'C:\Program Files (x86)\SimHub\DashTemplates')

W, H = 1920, 1080
FONT = 'DIN 1451 Std Mittelschrift'
NS = uuid.UUID('6f1d2a52-8a51-4c64-9a52-0d9a7c1e5b10')

# Colour tokens (docs/design.md), as SimHub #AARRGGBB.
PANEL = '#D10B0E13'
HAIRLINE = '#FF2A313C'
GUIDE = '#332A313C'
TEXT = '#FFF7F9FC'
MUTED = '#FF8B97A8'
ACCENT = '#FF59D7FF'
BRAKE = '#FFFF4B3E'
THROTTLE = '#FF2EE66B'
STEER = '#FF3BA7FF'
TC = '#FFFFB020'
ABS = '#FFB98CFF'
LIGHT_OFF = '#FF2A313C'

# Flip to -1 if the spike shows right-hand steering drawn downward.
STEER_SIGN = 1

GD = 'DataCorePlugin.GameData.NewData.'
STEER_RAW = 'DataCorePlugin.GameRawData.Physics.SteerAngle'  # AC + ACC shared memory


def sid(*parts):
    return str(uuid.uuid5(NS, '/'.join(map(str, parts))))


def ncalc(expr):
    return {'Formula': {'Expression': expr}, 'Mode': 2}


def js(expr):
    return {'Formula': {'JSExt': 3, 'Interpreter': 1, 'Expression': expr}, 'Mode': 2}


def border(color=HAIRLINE, width=1, radius=6):
    return {
        'BorderColor': color,
        'BorderTop': width, 'BorderBottom': width, 'BorderLeft': width, 'BorderRight': width,
        'RadiusTopLeft': radius, 'RadiusTopRight': radius,
        'RadiusBottomLeft': radius, 'RadiusBottomRight': radius,
    }


def _box(kind, name, left, top, width, height, **extra):
    item = {
        '$type': f'SimHub.Plugins.OutputPlugins.GraphicalDash.Models.{kind}, SimHub.Plugins',
        'Left': float(left), 'Top': float(top), 'Width': float(width), 'Height': float(height),
        'Visible': True, 'Name': name,
    }
    item.update(extra)
    return item


def rect(name, l, t, w, h, color, bstyle=None, color_expr=None):
    item = _box('RectangleItem', name, l, t, w, h, IsRectangleItem=True,
                BackgroundColor=color, BorderStyle=bstyle or {})
    if color_expr:
        item['Bindings'] = {'BackgroundColor': ncalc(color_expr)}
    return item


def text(name, l, t, w, h, value, size, color=TEXT, halign=0, expr=None, js_expr=None, fmt=None):
    item = _box('TextItem', name, l, t, w, h, IsTextItem=True, Font=FONT, FontWeight='Normal',
                FontStyle='Normal', FontSize=float(size), Text=value, TextColor=color,
                HorizontalAlignment=halign, VerticalAlignment=1,
                TextPadding={}, BackgroundColor='#00FFFFFF', BorderStyle={})
    if expr or js_expr:
        binding = ncalc(expr) if expr else js(js_expr)
        if fmt:
            binding['FormatString'] = fmt
        item['Bindings'] = {'Text': binding}
    return item


def chart(name, l, t, w, h, expr, color, lo, hi, points, interval_ms, thickness=2):
    return _box('ChartItem', name, l, t, w, h,
                ChartSuspended=False, ChartEnabled=True, CurrentValue=0.0,
                Minimum=float(lo), UseMinimum=True, Maximum=float(hi), UseMaximum=True,
                LineColor=color, LineTickness=thickness, PointsCount=float(points),
                BackgroundColor='#00FFFFFF', BorderStyle={},
                RenderingSkip=0, MinimumRefreshIntervalMS=float(interval_ms),
                Bindings={'CurrentValue': ncalc(expr)})


def image(name, l, t, w, h, image_name):
    return _box('ImageItem', name, l, t, w, h, Image=image_name, AutoSize=False,
                BackgroundColor='#00FFFFFF')


# ---------------------------------------------------------------- layout ---

X0, XW = 24, W - 48             # panel span
LABEL_W = 150                   # lane label column
CX = X0 + LABEL_W               # chart left
CW = X0 + XW - 16 - CX          # chart width


def lane(items, key, label, top, height, guides):
    """Panel + label + horizontal guides. guides = list of (fraction_from_top, caption)."""
    items.append(rect(f'{key} panel', X0, top, XW, height, PANEL, border()))
    items.append(text(f'{key} label', X0 + 20, top + 12, LABEL_W - 30, 28, label, 20, MUTED))
    inner_top, inner_h = top + 16, height - 32
    for frac, caption in guides:
        y = inner_top + inner_h * frac
        items.append(rect(f'{key} guide {caption}', CX, y, CW, 1, GUIDE))
        items.append(text(f'{key} guide label {caption}', CX - 60, y - 11, 52, 22, caption, 15,
                          MUTED, halign=2))
    return inner_top, inner_h


def screen(index, hz, interval_ms, points):
    items = [image('Carbon background', 0, 0, W, H, 'carbon')]
    seconds = points * interval_ms / 1000

    # Top bar
    items.append(rect('Top bar', X0, 20, XW, 72, PANEL, border()))
    items.append(text('Title', X0 + 24, 30, 300, 52, 'LIVE TRACE', 30, TEXT))
    items.append(text('Session', X0 + 300, 30, 900, 52, '', 22, MUTED, js_expr=(
        "var g=$prop('DataCorePlugin.CurrentGame')||'NO GAME';"
        f"var t=$prop('{GD}TrackName')||'';var c=$prop('{GD}CarModel')||'';"
        "return [g,t,c].filter(function(s){return s;}).join('  \u00b7  ').toUpperCase();")))
    items.append(text('Screen mode', X0 + 1180, 30, 330, 52,
                      f'SPIKE {index}  \u00b7  {hz} Hz  \u00b7  {seconds:.0f} s', 20, ACCENT, halign=2))
    items.append(text('Speed', X0 + 1540, 26, 180, 60, '0', 44, TEXT, halign=2,
                      expr=f'isnull([{GD}SpeedLocal],0)', fmt='0'))
    items.append(text('Gear', X0 + 1740, 26, 100, 60, 'N', 44, ACCENT, halign=1,
                      expr=f"isnull([{GD}Gear],'N')"))

    # Pedals lane: throttle + brake overlaid on the same rectangle.
    top, h = lane(items, 'Pedals', 'PEDALS', 108, 440, [(0, '100'), (0.5, '50'), (1, '0')])
    for nm, field, col in (('Throttle', 'Throttle', THROTTLE), ('Brake', 'Brake', BRAKE)):
        items.append(chart(f'{nm} chart', CX, top, CW, h,
                           f'isnull([{GD}{field}],0)', col, 0, 100, points, interval_ms, 3))
    items.append(text('Throttle key', X0 + 20, top + 40, 120, 24, 'THROTTLE', 16, THROTTLE))
    items.append(text('Brake key', X0 + 20, top + 66, 120, 24, 'BRAKE', 16, BRAKE))

    # Steering lane: zero centred, right turn up (STEER_SIGN).
    top, h = lane(items, 'Steering', 'STEERING', 564, 270, [(0, 'R'), (0.5, '0'), (1, 'L')])
    items.append(chart('Steering chart', CX, top, CW, h,
                       f'isnull([{STEER_RAW}],0) * {STEER_SIGN}', STEER, -1, 1, points, interval_ms, 3))

    # Events lane: two thin 0/1 sub-lanes.
    items.append(rect('Events panel', X0, 850, XW, 110, PANEL, border()))
    for row, (nm, field, col) in enumerate((('TC', 'TCActive', TC), ('ABS', 'ABSActive', ABS))):
        y = 862 + row * 46
        items.append(text(f'{nm} lane label', X0 + 20, y + 4, 120, 30, nm, 20, col))
        items.append(rect(f'{nm} lane base', CX, y + 38, CW, 1, GUIDE))
        items.append(chart(f'{nm} chart', CX, y, CW, 40,
                           f'if(isnull([{GD}{field}],false),1,0)', col, 0, 1, points, interval_ms, 2))

    # Time axis
    for frac, caption, align in ((0, f'\u2212{seconds:.0f} s', 0), (0.5, f'\u2212{seconds / 2:.0f} s', 1),
                                 (1, 'NOW', 2)):
        left = CX + CW * frac - 120 * align / 2   # anchor left / centre / right edge
        items.append(text(f'Axis {caption}', left, 962, 120, 22, caption, 15, MUTED, halign=align))

    # Status bar
    items.append(rect('Status bar', X0, 990, XW, 70, PANEL, border()))
    x = X0 + 24
    for nm, active, level, col in (('TC', 'TCActive', 'TCLevel', TC), ('ABS', 'ABSActive', 'ABSLevel', ABS)):
        items.append(text(f'{nm} status label', x, 1000, 60, 50, nm, 24, MUTED))
        items.append(text(f'{nm} level', x + 60, 1000, 60, 50, '\u2014', 30, TEXT,
                          expr=f"isnull([{GD}{level}],'\u2014')"))
        items.append(rect(f'{nm} light', x + 124, 1013, 24, 24, LIGHT_OFF, border(HAIRLINE, 1, 12),
                          color_expr=f"if(isnull([{GD}{active}],false),'{col}','{LIGHT_OFF}')"))
        x += 200
    items.append(text('BB label', x, 1000, 60, 50, 'BB', 24, MUTED))
    items.append(text('BB value', x + 50, 1000, 120, 50, '\u2014', 30, TEXT,
                      expr=f'isnull([{GD}BrakeBias],0)', fmt='0.0'))
    items.append(text('Diagnostics', X0 + 900, 1000, XW - 924, 50, '', 18, MUTED, halign=2, js_expr=(
        f"var s=$prop('{STEER_RAW}');"
        "return 'STEER RAW  ' + (s==null ? 'n/a' : s.toFixed(3)) + '      TC '"
        f" + $prop('{GD}TCActive') + '   ABS ' + $prop('{GD}ABSActive');")))

    return {
        'RenderingSkip': 0, 'Name': f'Spike {hz} Hz', 'InGameScreen': True, 'IdleScreen': True,
        'PitScreen': False, 'ScreenId': sid('screen', index), 'AllowOverlays': True,
        'IsForegroundLayer': False, 'IsOverlayLayer': False,
        'OverlayTriggerExpression': {'Expression': ''}, 'ScreenEnabledExpression': {'Expression': ''},
        'OverlayMaxDuration': 0, 'OverlayMinDuration': 0, 'IsBackgroundLayer': False,
        'BackgroundColor': '#FF0A0B0D', 'Items': items, 'MinimumRefreshIntervalMS': 0.0,
    }


def build():
    png = carbon.render(W, H)
    images = [{'Name': 'carbon', 'Extension': '.png', 'Modified': False, 'Optimized': True,
               'Width': W, 'Height': H, 'Length': len(png), 'MD5': hashlib.md5(png).hexdigest()}]
    screens = [screen(1, 30, 33, 180), screen(2, 60, 16, 360)]
    metadata = {
        'Title': 'Live Trace Spike', 'Description': 'Phase 0: native ChartItem live window test',
        'Author': 'Turn Telemetry', 'ScreenCount': float(len(screens)),
        'InGameScreensIndexs': [0, 1], 'IdleScreensIndexs': [0, 1], 'PitScreensIndexs': [],
        'MainPreviewIndex': 0, 'IsOverlay': False, 'OverlaySizeWarning': True,
        'MetadataVersion': 2.0, 'EnableOnDashboardMessaging': True, 'PreferredTouchMode': 0,
        'SimHubVersion': '9.11.21', 'Width': float(W), 'Height': float(H), 'DashboardVersion': '0.0.1',
    }
    dash = {
        'Version': 2, 'Id': sid('dash'), 'BaseHeight': H, 'BaseWidth': W,
        'BackgroundColor': '#FF0A0B0D', 'Screens': screens,
        'SnapToGrid': False, 'HideLabels': True, 'ShowForeground': True, 'ForegroundOpacity': 100.0,
        'ShowBackground': True, 'BackgroundOpacity': 100.0, 'ShowBoundingRectangles': False,
        'GridSize': 10, 'Images': images, 'Metadata': metadata, 'ShowOnScreenControls': True,
        'IsOverlay': False, 'EnableClickThroughOverlay': False, 'EnableOnDashboardMessaging': True,
    }

    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / f'{NAME}.djson').write_text(json.dumps(dash, indent=2, ensure_ascii=False), encoding='utf-8')
    (OUT / f'{NAME}.djson.metadata').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('carbon.png', png)
    (OUT / f'{NAME}.djson.ressources').write_bytes(buf.getvalue())
    print(f'built {OUT} ({sum(len(s["Items"]) for s in screens)} items, carbon {len(png) // 1024} KB)')


def install():
    target = SIMHUB_TEMPLATES / NAME
    target.mkdir(exist_ok=True)
    for f in OUT.glob(f'{NAME}.djson*'):
        shutil.copy2(f, target / f.name)
    print(f'installed to {target}')


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--install', action='store_true', help='copy into SimHub DashTemplates')
    args = p.parse_args()
    build()
    if args.install:
        install()
