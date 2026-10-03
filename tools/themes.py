"""Dashboard themes: token sets that build_dash.py and dashlib.py read at build time.

Classic follows docs/design.md. Neon is a high-energy look (black carbon, acid lime, hot pink, electric data
colours, wide display type, glow) inspired by motocross / action-sports styling. It deliberately uses no logos,
marks, or names of any brand.
"""

CLASSIC = {
    'NAME': 'TurnTelemetry', 'TITLE': 'Turn Telemetry',
    'PANEL': '#D10B0E13', 'HAIRLINE': '#FF2A313C', 'GUIDE': '#332A313C',
    'TEXT': '#FFF7F9FC', 'MUTED': '#FF8B97A8', 'INK': '#FF0B0E13',
    'ACCENT': '#FF59D7FF',
    # Neon blue marks "you / now" in every theme (current turn, your car, your standings row). Purple, green and
    # yellow are reserved for pace (session best / personal best / slower), so nothing else uses them (2026-10-03).
    'CURRENT': '#FF2F7DFF',
    'BRAKE': '#FFFF4B3E', 'THROTTLE': '#FF2EE66B', 'STEER': '#FF3BA7FF', 'TC': '#FFFFB020', 'ABS': '#FFB98CFF',
    'LANE_TURN': '#FFC4CBD4', 'GOOD': '#FF2EE66B', 'BAD': '#FFFF4B3E', 'LIGHT_OFF': '#FF2A313C',
    'BAND': '#1FFFFFFF', 'BAND_CURRENT': '#B32F7DFF', 'STRIP': '#14FFFFFF',
    'NODATA': '#FF3A414C', 'TYRE_OUTLINE': '#40FFFFFF', 'WEAR_TRACK': '#33FFFFFF',
    'TYRE_COLOR_SUFFIX': '',        # plugin properties Tyre.<c>.Color.<z>, .PressureColor, .WearColor
    'FONT': 'DIN 1451 Std Mittelschrift', 'FONT_WEIGHT': 'Normal',
    'FONT_DISPLAY': 'DIN 1451 Std Mittelschrift', 'DISPLAY_WEIGHT': 'Normal', 'DISPLAY_STYLE': 'Normal',
    'FOCAL_SIZE': 115, 'GEAR_SIZE': 180, 'STATE_SIZE': 30, 'SPEED_SIZE': 90,   # turn tile halved for the map (2026-10-03)
    'GLOW': 0,                      # blur radius of the glow layer behind key elements; 0 = none
    'LABEL': '#FF8B97A8',           # section labels (PEDALS, STEERING, LAP, TYRES...)
    'CHART_WIDTH': 3,
    'SLASH': None,                  # RGB of the angled-stripe accent, or None
    'MAP_TRACK': '#FF3A414C', 'MAP_BORDER': '#FF8B97A8', 'MAP_PLAYER': '#FF2F7DFF', 'MAP_START': '#FFFFFFFF',
    'MAP_OPPONENT': '#FFF7F9FC',
    'CARBON': ((0x0A, 0x0B, 0x0D), (0x17, 0x19, 0x1D)),
    'BACKGROUND': 'carbon',
    'LIMITER': '#FFFF1A1A',         # RPM text on the limiter
    'FASTEST': '#FFB44BFF',         # session-fastest lap / sector (the usual timing-screen purple)
    'PACE_PB': '#FF2EE66B',         # green: personal best sector
    'PACE_SLOWER': '#FFFFD000',     # yellow: slower than your best
    'PACE_EMPTY': '#14FFFFFF',      # not driven yet this lap
    'FONT_NUMBER': None, 'NUMBER_WEIGHT': None, 'NUMBER_STYLE': None,
}

NEON = dict(CLASSIC, **{
    'NAME': 'TurnTelemetryNeon', 'TITLE': 'Turn Telemetry Neon',
    'PANEL': '#E6040504', 'HAIRLINE': '#737CFF00', 'GUIDE': '#26FFFFFF',
    'TEXT': '#FFFFFFFF', 'MUTED': '#FF9AA88F', 'INK': '#FF000000',
    'ACCENT': '#FF7CFF00',          # acid lime: the signature colour

    'BRAKE': '#FFFF1744', 'THROTTLE': '#FF00FFA3', 'STEER': '#FF00E5FF', 'TC': '#FFFFEA00', 'ABS': '#FFC042FF',
    'LANE_TURN': '#FFFFFFFF', 'GOOD': '#FF7CFF00', 'BAD': '#FFFF1744', 'LIGHT_OFF': '#FF1C211C',
    'BAND': '#26FFFFFF', 'STRIP': '#14FFFFFF',
    'NODATA': '#FF262B26', 'TYRE_OUTLINE': '#997CFF00', 'WEAR_TRACK': '#33FFFFFF',
    'TYRE_COLOR_SUFFIX': 'Neon',    # Tyre.<c>.ColorNeon.<z>, .PressureColorNeon, .WearColorNeon
    # Skate-style type, both shipped with SimHub (DashFonts): fat Futura Bold Oblique for big numbers and headers,
    # fat condensed Oswald Bold for labels and data so small text stays legible (user request 2026-10-03).
    'FONT': 'Oswald', 'FONT_WEIGHT': 'Bold',
    'FONT_DISPLAY': 'Futura', 'DISPLAY_WEIGHT': 'Bold', 'DISPLAY_STYLE': 'Italic',
    'FOCAL_SIZE': 100, 'GEAR_SIZE': 160, 'STATE_SIZE': 32, 'SPEED_SIZE': 78,  # turn tile halved for the map; italics overhang
    'GLOW': 0,      # blur glows off: WPF BlurEffect is GPU shader work; a GPU hang on the test PC followed launching this dash (2026-10-03)
    'LABEL': '#FF7CFF00',
    'CHART_WIDTH': 4,
    'SLASH': (0x7C, 0xFF, 0x00),
    'MAP_TRACK': '#FF1A2410', 'MAP_BORDER': '#FF7CFF00', 'MAP_START': '#FFFFFFFF',
    'MAP_OPPONENT': '#FFFFEA00',
    'CARBON': ((0x03, 0x04, 0x03), (0x10, 0x12, 0x0F)),
})

# Livery: race-livery style (black, acid lime corner wedges, neon-magenta splatter, squared race-number type).
# Style only: no logos, marks, names, or race numbers of any brand, team or driver (user request 2026-10-03).
LIVERY = dict(NEON, **{
    # The released dashboard (v0.2.0): Classic and Neon remain as optional build themes, not shipped.
    'NAME': 'TurnTelemetryDashboard', 'TITLE': 'Turn Telemetry Dashboard',
    'PANEL': '#F5060606', 'HAIRLINE': '#80A8F000', 'GUIDE': '#26FFFFFF',   # near-opaque: art must not cross the data
    'TEXT': '#FFFFFFFF', 'MUTED': '#FFB4B4B4', 'INK': '#FF000000',
    'ACCENT': '#FFA8F000',          # livery lime
    'BRAKE': '#FFFF2D2D', 'THROTTLE': '#FFA8F000', 'STEER': '#FFFFFFFF', 'TC': '#FFFFD000', 'ABS': '#FF00D0FF',
    'LANE_TURN': '#FFB4B4B4', 'GOOD': '#FFA8F000', 'BAD': '#FFFF2D2D',
    'BAND': '#2EFFFFFF',
    'LABEL': '#FFA8F000',
    'FONT': 'Oswald', 'FONT_WEIGHT': 'Bold',
    'FONT_DISPLAY': 'Futura', 'DISPLAY_WEIGHT': 'Bold', 'DISPLAY_STYLE': 'Normal',
    # Squared, extended race-number face shipped with SimHub; wide, so sizes are trimmed to fit "18", "8A", "287".
    'FONT_NUMBER': 'Eurostar Black Extended', 'NUMBER_WEIGHT': 'Normal', 'NUMBER_STYLE': 'Normal',
    'FOCAL_SIZE': 92, 'GEAR_SIZE': 130, 'SPEED_SIZE': 56,
    'GLOW': 0,
    'SLASH': None,                  # the artwork lives in the background
    'BACKGROUND': 'livery',
    'MAP_TRACK': '#FF1A1A1A', 'MAP_BORDER': '#FFA8F000', 'MAP_START': '#FFFFFFFF',
    'MAP_OPPONENT': '#FFFFFFFF',
})

THEMES = {'classic': CLASSIC, 'neon': NEON, 'livery': LIVERY}
