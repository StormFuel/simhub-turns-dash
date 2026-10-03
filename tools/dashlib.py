"""Helpers for generating SimHub Dash Studio .djson files.

Item JSON shapes mirror dashboards saved by SimHub 9.11/9.12 (installed templates), so Dash Studio can open and
edit the output. build_spike_dash.py predates this module and is intentionally left as-is (docs/plan.md, Phase 5).
"""
import hashlib
import io
import json
import shutil
import uuid
import zipfile
from pathlib import Path

SIMHUB_TEMPLATES = Path(r'C:\Program Files (x86)\SimHub\DashTemplates')
FONT = 'DIN 1451 Std Mittelschrift'
FONT_WEIGHT = 'Normal'
FONT_DISPLAY = FONT
DISPLAY_WEIGHT = 'Normal'
DISPLAY_STYLE = 'Normal'
FONT_NUMBER = None       # race-number face for the turn number, gear and speed; None = the display face
NUMBER_WEIGHT = None
NUMBER_STYLE = None


def _font(display, font, number=False):
    """(family, weight, style) for body text, display text (display=True) or race numbers (number=True)."""
    if font:
        return font, FONT_WEIGHT, 'Normal'
    if number and FONT_NUMBER:
        return FONT_NUMBER, NUMBER_WEIGHT or 'Normal', NUMBER_STYLE or 'Normal'
    return (FONT_DISPLAY, DISPLAY_WEIGHT, DISPLAY_STYLE) if display or number else (FONT, FONT_WEIGHT, 'Normal')

# Colour tokens (docs/design.md), as SimHub #AARRGGBB.
PANEL = '#D10B0E13'
HAIRLINE = '#FF2A313C'
GUIDE = '#332A313C'
TEXT = '#FFF7F9FC'
MUTED = '#FF8B97A8'
ACCENT = '#FF59D7FF'
CURRENT = '#FFFFB020'
BRAKE = '#FFFF4B3E'
THROTTLE = '#FF2EE66B'
STEER = '#FF3BA7FF'
TC = '#FFFFB020'
ABS = '#FFB98CFF'
LIGHT_OFF = '#FF2A313C'
INK = '#FF0B0E13'
CLEAR = '#00FFFFFF'

LEFT, CENTER, RIGHT = 0, 1, 2

_ns = uuid.UUID('6f1d2a52-8a51-4c64-9a52-0d9a7c1e5b11')


def sid(*parts):
    return str(uuid.uuid5(_ns, '/'.join(map(str, parts))))


def ncalc(expr, target=None):
    binding = {'Formula': {'Expression': expr}, 'Mode': 2}
    if target:
        binding['TargetPropertyName'] = target
    return binding


def js(expr):
    return {'Formula': {'JSExt': 3, 'Interpreter': 1, 'Expression': expr}, 'Mode': 2}


def border(color=None, width=1, radius=6, corners='tl tr bl br'):
    style = {'BorderColor': color or HAIRLINE, 'BorderTop': width, 'BorderBottom': width, 'BorderLeft': width, 'BorderRight': width}
    names = {'tl': 'RadiusTopLeft', 'tr': 'RadiusTopRight', 'bl': 'RadiusBottomLeft', 'br': 'RadiusBottomRight'}
    for c in corners.split():
        style[names[c]] = radius
    return style


def radius_only(radius, corners='tl tr bl br'):
    style = border(CLEAR, 0, radius, corners)
    return style


class Item(dict):
    """A dash item; chain .bind()/.show_if() to add bindings."""

    def bind(self, prop, expr, fmt=None):
        binding = ncalc(expr, prop)
        if fmt:
            binding['FormatString'] = fmt
        self.setdefault('Bindings', {})[prop] = binding
        return self

    def bind_js(self, prop, expr):
        self.setdefault('Bindings', {})[prop] = js(expr)
        return self

    def show_if(self, expr):
        return self.bind('Visible', expr)


def _box(kind, name, left, top, width, height, **extra):
    item = Item({
        '$type': f'SimHub.Plugins.OutputPlugins.GraphicalDash.Models.{kind}, SimHub.Plugins',
        'Left': float(left), 'Top': float(top), 'Width': float(width), 'Height': float(height),
        'Visible': True, 'Name': name,
    })
    item.update(extra)
    return item


def rect(name, l, t, w, h, color, style=None):
    return _box('RectangleItem', name, l, t, w, h, IsRectangleItem=True, BackgroundColor=color, BorderStyle=style or {})


def panel(name, l, t, w, h):
    return rect(name, l, t, w, h, PANEL, border())


def text(name, l, t, w, h, value, size, color=None, align=LEFT, font=None, display=False, number=False):
    family, weight, style = _font(display, font, number)
    return _box('TextItem', name, l, t, w, h, IsTextItem=True, Font=family, FontWeight=weight, FontStyle=style,
                FontSize=float(size), Text=value, TextColor=color or TEXT, HorizontalAlignment=align, VerticalAlignment=1,
                TextPadding={}, BackgroundColor=CLEAR, BorderStyle={})


def chart(name, l, t, w, h, expr, color, lo, hi, points=180, interval_ms=33, thickness=3):
    return _box('ChartItem', name, l, t, w, h,
                ChartSuspended=False, ChartEnabled=True, CurrentValue=0.0,
                Minimum=float(lo), UseMinimum=True, Maximum=float(hi), UseMaximum=True,
                LineColor=color, LineTickness=thickness, PointsCount=float(points),
                BackgroundColor=CLEAR, BorderStyle={},
                RenderingSkip=0, MinimumRefreshIntervalMS=float(interval_ms)).bind('CurrentValue', expr)


def image(name, l, t, w, h, image_name):
    return _box('ImageItem', name, l, t, w, h, Image=image_name, AutoSize=False, BackgroundColor=CLEAR)


# SimHub built-in text items (formats copied from installed templates).
def builtin(kind, name, l, t, w, h, size, color=None, align=CENTER, font=None, display=False, number=False, **extra):
    family, weight, style = _font(display, font, number)
    return _box(f'BuiltIn.{kind}', name, l, t, w, h, IsTextItem=True, Font=family, FontWeight=weight, FontStyle=style,
                FontSize=float(size),
                TextColor=color or TEXT, HorizontalAlignment=align, VerticalAlignment=1, BackgroundColor=CLEAR, **extra)


def lap_time(kind, name, l, t, w, h, size, color=None, align=CENTER):
    return builtin(kind, name, l, t, w, h, size, color, align, display=True,
                   TimeFormat='m\\:ss\\.fff', EmptyTimeText='-:--.---')


def screen(name, items, index=0, background='#FF0A0B0D'):
    return {
        'RenderingSkip': 0, 'Name': name, 'InGameScreen': True, 'IdleScreen': True, 'PitScreen': False,
        'ScreenId': sid('screen', name, index), 'AllowOverlays': True, 'IsForegroundLayer': False,
        'IsOverlayLayer': False, 'OverlayTriggerExpression': {'Expression': ''},
        'ScreenEnabledExpression': {'Expression': ''}, 'OverlayMaxDuration': 0, 'OverlayMinDuration': 0,
        'IsBackgroundLayer': False, 'BackgroundColor': background, 'Items': items, 'MinimumRefreshIntervalMS': 0.0,
    }


def write_dash(out_dir, name, title, description, screens, images, width, height, version, simhub='9.12.8'):
    """images: {name: png bytes}. Writes {name}.djson, .djson.metadata and .djson.ressources into out_dir."""
    import struct
    image_meta = [{'Name': n, 'Extension': '.png', 'Modified': False, 'Optimized': True,
                   'Width': struct.unpack('>I', b[16:20])[0], 'Height': struct.unpack('>I', b[20:24])[0],
                   'Length': len(b), 'MD5': hashlib.md5(b).hexdigest()} for n, b in images.items()]
    metadata = {
        'Title': title, 'Description': description, 'Author': 'StormFuel', 'ScreenCount': float(len(screens)),
        'InGameScreensIndexs': list(range(len(screens))), 'IdleScreensIndexs': list(range(len(screens))),
        'PitScreensIndexs': [], 'MainPreviewIndex': 0, 'IsOverlay': False, 'OverlaySizeWarning': True,
        'MetadataVersion': 2.0, 'EnableOnDashboardMessaging': True, 'PreferredTouchMode': 0,
        'SimHubVersion': simhub, 'Width': float(width), 'Height': float(height), 'DashboardVersion': version,
    }
    dash = {
        'Version': 2, 'Id': sid('dash', name), 'BaseHeight': height, 'BaseWidth': width,
        'BackgroundColor': '#FF0A0B0D', 'Screens': screens,
        'SnapToGrid': False, 'HideLabels': True, 'ShowForeground': True, 'ForegroundOpacity': 100.0,
        'ShowBackground': True, 'BackgroundOpacity': 100.0, 'ShowBoundingRectangles': False,
        'GridSize': 10, 'Images': image_meta, 'Metadata': metadata, 'ShowOnScreenControls': False,
        'IsOverlay': False, 'EnableClickThroughOverlay': False, 'EnableOnDashboardMessaging': True,
    }
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    (out / f'{name}.djson').write_text(json.dumps(dash, indent=2, ensure_ascii=False), encoding='utf-8')
    (out / f'{name}.djson.metadata').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as z:
        for n, b in images.items():
            z.writestr(n + '.png', b)
    (out / f'{name}.djson.ressources').write_bytes(buf.getvalue())
    return dash


def install(out_dir, name):
    target = SIMHUB_TEMPLATES / name
    target.mkdir(exist_ok=True)
    for f in Path(out_dir).glob(f'{name}.djson*'):
        shutil.copy2(f, target / f.name)
    return target


def glow(item, radius, color=None):
    """Blurred copy of a text or outline item, to place underneath it for a neon glow. Bindings are kept, so the
    glow follows the item's text, colour and visibility."""
    import copy
    g = Item(copy.deepcopy(dict(item)))
    g['Name'] = item['Name'] + ' glow'
    g['EnableBlur'] = True
    g['BlurRadius'] = float(radius)
    if color and 'TextColor' in g:
        g['TextColor'] = color
    return g


MAP_TRACK = '#FF3A414C'
MAP_BORDER = '#FF8B97A8'
MAP_PLAYER = '#FF59D7FF'
MAP_START = '#FFFFB020'
MAP_OPPONENT = '#FFF7F9FC'


def track_map(name, l, t, w, h):
    """SimHub's GeneratedStaticMapItem: the whole track from SimHub's map, every car as a dot labelled with its race
    position (yours in the player colour, others in MAP_OPPONENT, or SimHub's class colours in multi-class), and the
    start line.

    The tablet's web client draws each car as an HTML circle `DotRadius` *dashboard pixels across* with the label at
    `LabelFontSize` px (SimHub Web/converters.js, ApplyPlayerStyle). On 2026-10-03, dots of 38/48 were bigger than the
    whole track at this size, and a render image of twice the item size coincided with a tiny track; both now follow
    the item size."""
    def dot(color, diameter, label):
        return {'LabelFont': FONT, 'LabelFontSize': float(label), 'LabelColor': INK, 'DotColor': color,
                'DotBorderThickness': 1.0, 'DotBordercolor': '#FF000000', 'DotRadius': float(diameter)}
    return _box('GeneratedStaticMapItem', name, l, t, w, h,
                AlternateTrackSectorColor=MAP_TRACK, CursorColor=MAP_PLAYER, DisplayScale=1.0, MapShadow=False,
                OverrideColorsWithCarClassColors=True, DisplayPerClassPosition=False,
                KeepMapDefinedClassColorsInSingleClass=True, DisableAutomaticPlayerClassStyle=True,
                MinimumTrackBorderWidth=0.0, MinimumTrackWidth=4.0,
                OpponentStyle=dot(MAP_OPPONENT, 14, 9), PlayerStyle=dot(MAP_PLAYER, 18, 11),
                RenderImageWidth=float(w), RenderImageHeight=float(h),
                StartLine={'Color': MAP_START, 'Enabled': True, 'Height': 20.0, 'Width': 4.0},
                TrackBorderColor=MAP_BORDER, TrackBorderWidth=1.5, TrackColor=MAP_TRACK, TrackWidth=7.0,
                BackgroundColor=CLEAR, RenderingSkip=3, MinimumRefreshIntervalMS=50.0)


def apply_theme(theme):
    """Point this module's tokens at a theme (themes.py) before building."""
    globals().update({k: v for k, v in theme.items() if k.isupper()})


def check_bounds(dash, width, height):
    bad = []
    for s in dash['Screens']:
        for i in s['Items']:
            if i['Left'] < 0 or i['Top'] < 0 or i['Left'] + i['Width'] > width + 0.5 or i['Top'] + i['Height'] > height + 0.5:
                bad.append(i['Name'])
    return bad


def button(name, l, t, w, h, action):
    """Touch area that calls a SimHub action (e.g. 'TurnTelemetry.SetTurn1Toggle') when tapped.

    Uses ButtonItem.TriggerAction, so users don't have to map anything in Controls and events. The background is
    almost-transparent rather than fully transparent so the area still receives taps.
    """
    return _box('ButtonItem', name, l, t, w, h, SimulateKey=False, TriggerAction=action, TriggerSimHubInputName='',
                ButtonCommand={'$type': 'SimHub.Plugins.OutputPlugins.GraphicalDash.Models.RelayCommand, SimHub.Plugins'},
                Image='', AutoSize=False, AutoSizeScale=1.0, BackgroundColor='#01FFFFFF', BorderStyle={},
                Opacity=100.0)


def pill(items, name, l, t, w, h, label, action, color, visible, text_expr=None):
    """Rounded outlined button: outline, label, and the touch area on top."""
    caption = text(f'{name} label', l, t, w, h, label, 16, color, CENTER)
    if text_expr:
        caption.bind('Text', text_expr)
    for item in (rect(f'{name} outline', l, t, w, h, '#B30B0E13', border(color, 1, h // 2)), caption,
                 button(f'{name} button', l, t, w, h, action)):
        items.append(item.show_if(visible))
