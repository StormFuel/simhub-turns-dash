"""Generate the main Turn Telemetry dashboard (docs/plan.md, Phase 2).

Writes dash/TurnTelemetryDashboard/{TurnTelemetryDashboard.djson, .djson.ressources, .djson.metadata}
(other themes: dash/<theme NAME>/);
--install also copies it into SimHub's DashTemplates. Layout and tokens: docs/design.md.
Everything sim-specific comes from the TurnTelemetry plugin (property contract v1, docs/architecture.md).
"""
import argparse
import shutil
from pathlib import Path

import carbon
import dashlib
import fontcheck
import livery
import themes
from dashlib import (track_map, ABS, ACCENT, BRAKE, CENTER, CLEAR, CURRENT, GUIDE, HAIRLINE, INK, LEFT, LIGHT_OFF, MUTED,
                     RIGHT, STEER, TC, TEXT, THROTTLE, border, builtin, button, chart, check_bounds, image, install,
                     lap_time, panel, pill, radius_only, rect, screen, text, write_dash)

ROOT = Path(__file__).resolve().parent.parent
NAME = 'TurnTelemetry'
OUT = ROOT / 'dash' / NAME
VERSION = '0.2.6'
CONTRACT = 2

W, H = 1920, 1080
TT = 'TurnTelemetry.'
GD = 'DataCorePlugin.GameData.NewData.'
MAX_BANDS = 40

# Columns (docs/design.md): focal 360 | centre 1100 | tyres 380, 16 px gutters.
TOP, BOTTOM = 108, 974
LX, LW = 24, 360
CX, CW = 400, 1100
RX, RW = 1516, 380

# Live window timing: Phase 0 showed 30 Hz is smooth on the tablet.
POINTS, INTERVAL_MS = 180, 33
SECONDS = POINTS * INTERVAL_MS / 1000


def p(name):
    return f'[{TT}{name}]'


def top_bar(items):
    items.append(panel('Top bar', 24, 20, 1872, 72))
    session_x = 48
    if SLASH:
        items.append(image('Top slash', 32, 28, 120, 56, 'slash'))
        session_x = 168
    items.append(text('Session', session_x, 30, 960 - session_x, 52, '', 22, MUTED).bind_js('Text', (
        "var names={AssettoCorsa:'AC',AssettoCorsaCompetizione:'ACC',IRacing:'IRACING',LMU:'LE MANS ULTIMATE',"
        "RFactor2:'RFACTOR 2',Automobilista2:'AMS2',RRRE:'RACEROOM',FH4:'FORZA HORIZON 4',FH5:'FORZA HORIZON 5',"
        "FH6:'FORZA HORIZON 6'};"
        "var code=$prop('DataCorePlugin.CurrentGame'); var g=names[code]||code||'NO GAME';"
        f"var t=$prop('{GD}TrackName')||'';var c=$prop('{GD}CarModel')||'';"
        "return [g,t,c].filter(function(s){return s;}).join('  ·  ').toUpperCase();")))
    x = 980
    items.append(text('Lap label', x, 30, 60, 52, 'LAP', 18, MUTED))
    items.append(text('Lap number', x + 50, 30, 70, 52, '0', 32, TEXT, display=True).bind('Text', f'isnull([{GD}CurrentLap],0)'))
    items.append(lap_time('CurrentLapTime', 'Current lap', x + 130, 26, 230, 60, TIME_SIZE, TEXT))
    items.append(builtin('LiveDeltaToBestText', 'Delta', x + 370, 26, 170, 60, DELTA_SIZE, TEXT, display=True, Format='+0.00;-0.00',
                         NoDataText='').bind(
        'TextColor', f"if(isnull([PersistantTrackerPlugin.SessionBestLiveDeltaSeconds],0) <= 0, '{GOOD}', '{BAD}')"))
    items.append(text('Best label', x + 560, 30, 70, 52, 'BEST', 18, MUTED))
    items.append(lap_time('BestLapTime', 'Best lap', x + 620, 26, 260, 60, BEST_SIZE, ACCENT, align=LEFT))


def focal_column(items):
    state = p('Turn.State')
    # Turn tile: just the number of the corner ahead, or the one you're in (pink + frame). No NEXT/CURRENT label:
    # it read as part of the number (user feedback 2026-10-03).
    # Half-height tile (user feedback 2026-10-03: the map matters more than a huge number).
    tile_h = 165
    items.append(panel('Focal panel', LX, TOP, LW, tile_h))
    # No placeholder glyph: DIN's dash renders as a heavy bar at this size (seen on the tablet 2026-10-03).
    number = (text('Focal number', LX, TOP + 4, LW, 124, '', FOCAL_SIZE, TEXT, CENTER, number=True)
              .bind('Text', f"isnull({p('Turn.Focal.Label')},'')")
              .bind('TextColor', f"if({state}='CURRENT','{CURRENT}','{TEXT}')"))
    if GLOW:
        items.append(dashlib.glow(number, GLOW))
    items.append(number)
    message = p('StartLine.MessageVisible')
    items.append(text('Focal name', LX, TOP + 124, LW, 34, '', 22, MUTED, CENTER)
                 .show_if(f'!{message}')
                 .bind('Text', f"isnull({p('Turn.Focal.Name')},'')"))
    items.append(text('Focal message', LX + 10, TOP + 124, LW - 20, 34, '', 16, ACCENT, CENTER)
                 .show_if(message)
                 .bind('Text', f"isnull({p('StartLine.Message')},'')"))
    no_turns = f"!{p('Turn.Supported')}"
    items.append(text('Focal hint 1', LX + 20, TOP + 46, LW - 40, 30, '', 18, MUTED, CENTER)
                 .show_if(no_turns)
                 .bind('Text', f"if({p('Caps.LapPosition')},'NO TURN DATA YET','NO LAP POSITION')"))
    items.append(text('Focal hint 2', LX + 20, TOP + 78, LW - 40, 30, 'TURN TELEMETRY \u203a TURN EDITOR', 18, MUTED, CENTER)
                 .show_if(f"{no_turns} && {p('Caps.LapPosition')}"))
    current_frame = rect('Focal current frame', LX, TOP, LW, tile_h, CLEAR, border(CURRENT, 2, 6)) \
        .show_if(f"{state}='CURRENT'")
    if GLOW:
        items.append(dashlib.glow(current_frame, GLOW / 2))
    items.append(current_frame)

    # Track map (SimHub's own map of this track) with SET LINE: tap where numbering should begin, normally as you
    # cross the start/finish line, and the next corner ahead becomes Turn 1 (docs/plan.md D15).
    map_top, map_h = TOP + tile_h + 16, 714 - (TOP + tile_h + 16)
    items.append(panel('Map panel', LX, map_top, LW, map_h))
    items.append(text('Map label', LX + 20, map_top + 10, 120, 30, 'TRACK', 18, LABEL))
    items.append(track_map('Track map', LX + 12, map_top + 48, LW - 24, map_h - 58))
    can_set = f"{p('Turn.Supported')} && {p('Caps.LapPosition')}"
    undo = p('StartLine.UndoAvailable')
    pill(items, 'Set line', LX + LW - 118, map_top + 8, 104, 34, 'SET LINE', 'TurnTelemetry.SetStartLine', ACCENT, can_set,
         text_expr=f"if({undo},'UNDO','SET LINE')")
    pill(items, 'Reset turns', LX + LW - 232, map_top + 8, 104, 34, 'RESET', 'TurnTelemetry.ResetTurns', MUTED,
         f"{p('Turns.UserNumbering')} && !{undo}")

    items.append(panel('Speed panel', LX, 730, LW, 244))
    gear = builtin('GearText', 'Gear', LX + 10, 740, 150, 156, GEAR_SIZE, ACCENT, CENTER, number=True,
                   DesignerText='N', NoDataText='N', IgnoreNeutralGear=False, GearBlinkText=False,
                   GearTextColor=ACCENT, GearBackgoundColor=CLEAR, TextMask='0000000000')
    if GLOW:
        items.append(dashlib.glow(gear, GLOW))
    items.append(gear)
    items.append(builtin('SpeedText', 'Speed', LX + 160, 770, 170, 120, SPEED_SIZE, TEXT, RIGHT, number=True, Format='0', NoDataText='0'))
    items.append(text('Speed unit', LX + 160, 885, 180, 36, 'KM/H', 20, MUTED, RIGHT)
                 .bind('Text', f"if(isnull([{GD}SpeedLocalUnit],'KMH')='MPH','MPH','KM/H')"))
    # RPM under the gear: white, bright red while SimHub reports the redline/limiter reached.
    items.append(text('RPM', LX + 10, 896, 150, 40, '0', RPM_SIZE, TEXT, CENTER, display=True)
                 .bind_js('Text', f"var r=$prop('{GD}Rpms'); return r ? Math.round(r) : '0';")
                 .bind('TextColor', f"if(isnull([{GD}CarSettings_RPMRedLineReached],0) >= 1,'{LIMITER}','{TEXT}')"))
    items.append(text('RPM label', LX + 10, 934, 150, 22, 'RPM', 14, MUTED, CENTER))


MAX_SECTORS = 6       # SectorMap.MaxSectors in the plugin
STRIP_H = 154


SECTOR_BG = '#14FFFFFF'
BRAKE_TRACK = '#FF0E100E'   # the Button Box's brake track


def limits_dim():
    return '#66' + BAD[3:]
SECTOR_LINE = '#99FFFFFF'


def pace_color(state, dim=False):
    """NCalc colour for a published sector pace state (SESSIONBEST / PERSONALBEST / SLOWER / INVALID / NONE).
    INVALID (track limits in that sector) uses the track-limits red."""
    def c(argb):
        return '#59' + argb[3:] if dim else argb
    return (f"if({state}='SESSIONBEST','{c(FASTEST)}',if({state}='PERSONALBEST','{c(PACE_PB)}',"
            f"if({state}='SLOWER','{c(PACE_SLOWER)}',if({state}='INVALID','{c(BAD)}','{PACE_EMPTY}'))))")


def lap_strip(items):
    """Whole-lap strip: sectors rated by pace, turn bands from the plugin (current turn highlighted), track-limit
    excursions per turn, and the car."""
    global LIMITS_DIM
    LIMITS_DIM = limits_dim()
    items.append(panel('Lap strip panel', CX, TOP, CW, STRIP_H))
    items.append(text('Lap strip label', CX + 20, TOP + 10, 300, 26, 'TURNS THIS LAP', 18, LABEL))
    items.append(text('Limits count', CX + 200, TOP + 10, 200, 26, '', 16, BAD)
                 .show_if(f"isnull({p('Limits.SessionTotal')},0) > 0 && isnull({p('Limits.Available')},true)")
                 .bind_js('Text', f"return 'TRACK LIMITS  ' + ($prop('{TT}Limits.SessionTotal')||0);"))
    # ACC races report no excursions at all (tyres-out is never filled, laps aren't invalidated): say so.
    items.append(text('Limits unavailable', CX + 200, TOP + 10, 260, 26, 'TRACK LIMITS N/A IN RACE', 16, MUTED)
                 .show_if(f"!isnull({p('Limits.Available')},true)"))
    # RESET LAPS (issue: manual game resets): two taps within 3 s; the first turns it into CONFIRM?.
    armed = p('Reset.Armed')
    pill(items, 'Reset laps', CX + CW - 132, TOP + 6, 116, 28, 'RESET LAPS', 'TurnTelemetry.ResetSession', MUTED,
         f"isnull({p('Contract')},0) > 0", text_expr=f"if({armed},'CONFIRM?','RESET LAPS')")
    items[-2].bind('TextColor', f"if({armed},'{TEXT}','{MUTED}')")   # the pill's caption: white while armed
    items.append(text('Turn source', CX + 400, TOP + 10, CW - 556, 26, '', 16, MUTED, RIGHT).bind_js('Text', (
        f"if(!$prop('{TT}Caps.LapPosition')) return '';"
        f"var src=$prop('{TT}Turn.Source')||'none'; var n=$prop('{TT}Turn.Count')||0;"
        "if(src==='none') return 'NO TURN DATA · USE THE TURN EDITOR IN THE PLUGIN SETTINGS';"
        "if(src.indexOf('auto')===0) return 'TURNS · AUTO-NUMBERED · ' + n + ' · REVIEW IN THE TURN EDITOR';"
        "return 'TURNS · ' + src.toUpperCase() + ' · ' + n;")))

    sx, sw, sy, sh = CX + 20, CW - 40, TOP + 88, 50
    sector_y, sector_h = TOP + 38, 22
    items.append(rect('Lap strip track', sx, sy, sw, sh, STRIP, radius_only(3)))

    # Sectors: a segment per sector above the bands, filled with its pace colour (purple session best, green
    # personal best, yellow slower; dimmed = last lap's result until you reach it), with a divider running down
    # through the bands so each turn visibly sits in its sector. Boundaries are learned by the plugin.
    # Positions are on the strip, which reads Turn 1 -> max (StripLayout). When the sim's line is just before Turn 1 the
    # sectors line up with the turns and are drawn over them. When it isn't (ACC Silverstone: the line is before
    # Copse, T9) they'd read S2 | S3 | S1 | S2, so instead (user decision 2026-10-03) they get their own row of separate
    # boxes in game order S1 . S2 . S3, distinct from the turns, and the turns strip shows only neutral dividers with
    # S-tags where each sector starts.
    rotated = f"isnull({p('Strip.Rotated')},false)"
    count = f"max(1, isnull({p('Sector.Count')},0))"
    gap = 12
    box_w = f"({sw} - {gap} * ({count} - 1)) / {count}"
    for n in range(1, MAX_SECTORS + 1):
        state = f"isnull({p(f'Sector.{n}.State')},'NONE')"
        last = f"isnull({p(f'Sector.{n}.FromLastLap')},false)"
        fill = f"if({state}='NONE','{SECTOR_BG}',if({last},{pace_color(state, True)},{pace_color(state)}))"
        visible = f"{rotated} && {n} <= isnull({p('Sector.Count')},0)"
        items.append(rect(f'Sector {n} box', sx, sector_y, 2, sector_h, SECTOR_BG, border(SECTOR_LINE, 1, 4))
                     .show_if(visible)
                     .bind('Left', f'{sx} + {n - 1} * ({box_w} + {gap})')
                     .bind('Width', box_w)
                     .bind('BackgroundColor', fill))
        items.append(text(f'Sector {n} box label', sx, sector_y, 2, sector_h, f'S{n}', 15, MUTED, CENTER)
                     .show_if(visible)
                     .bind('Left', f'{sx} + {n - 1} * ({box_w} + {gap})')
                     .bind('Width', box_w)
                     .bind_js('Text', f"var t=$prop('{TT}Sector.{n}.Time'); return t ? 'S{n}   ' + t : 'S{n}';")
                     .bind('TextColor', f"if({state}='NONE','{MUTED}',if({last},'{TEXT}','{INK}'))"))
        items.append(text(f'Sector {n} tag', sx, sy + 2, 30, 16, f'S{n}', 12, MUTED, LEFT)
                     .show_if(f"{rotated} && isnull({p(f'Sector.{n}.StripStart')},-1) >= 0")
                     .bind('Left', f"{sx} + isnull({p(f'Sector.{n}.StripStart')},0) * {sw} + 4"))

    for n in range(1, MAX_SECTORS + 1):
        start, end = f"isnull({p(f'Sector.{n}.StripStart')},-1)", f"isnull({p(f'Sector.{n}.StripEnd')},-1)"
        state = f"isnull({p(f'Sector.{n}.State')},'NONE')"
        last = f"isnull({p(f'Sector.{n}.FromLastLap')},false)"
        fill = f"if({state}='NONE','{SECTOR_BG}',if({last},{pace_color(state, True)},{pace_color(state)}))"
        items.append(rect(f'Sector {n} segment', sx, sector_y, 2, sector_h, SECTOR_BG, radius_only(3))
                     .show_if(f'!{rotated} && {start} >= 0')
                     .bind('Left', f'{sx} + {start} * {sw} + 1')
                     .bind('Width', f'max(2, if({end} >= {start}, {end} - {start}, 1 - {start}) * {sw} - 2)')
                     .bind('BackgroundColor', fill))
        items.append(rect(f'Sector {n} wrap', sx, sector_y, 2, sector_h, SECTOR_BG, radius_only(3))
                     .show_if(f'!{rotated} && {start} >= 0 && {end} < {start}')
                     .bind('Width', f'max(2, {end} * {sw} - 1)')
                     .bind('BackgroundColor', fill))
        items.append(text(f'Sector {n} label', sx, sector_y, 200, sector_h, f'S{n}', 15, MUTED, CENTER)
                     .show_if(f'!{rotated} && {start} >= 0')
                     .bind('Left', f"{sx} + isnull({p(f'Sector.{n}.StripCentre')},0) * {sw} - 100")
                     .bind_js('Text', f"var t=$prop('{TT}Sector.{n}.Time'); return t ? 'S{n}   ' + t : 'S{n}';")
                     .bind('TextColor', f"if({state}='NONE','{MUTED}',if({last},'{TEXT}','{INK}'))"))
        # A divider at every sector start except the strip's left edge (S1's start is mid-strip when rotated).
        items.append(rect(f'Sector {n} divider', sx, sector_y, 2, sy + sh - sector_y, SECTOR_LINE)
                     .show_if(f'!{rotated} && {start} > 0.002')
                     .bind('Left', f'{sx} + {start} * {sw} - 1'))
        items.append(rect(f'Sector {n} strip divider', sx, sy, 2, sh, SECTOR_LINE)
                     .show_if(f'{rotated} && {start} > 0.002')
                     .bind('Left', f'{sx} + {start} * {sw} - 1'))

    for i in range(MAX_BANDS):
        b = f'Turn.Band.{i:02d}'
        start, end = f"isnull({p(b + '.StripStart')},-1)", f"isnull({p(b + '.StripEnd')},-1)"
        # Per-turn delta vs the best lap (issue #12): green = time gained in this corner, yellow = lost; dimmed =
        # last lap's result until the corner is driven again. The corner you're in stays blue.
        delta = f"isnull({p(b + '.DeltaState')},'NONE')"
        delta_last = f"isnull({p(b + '.DeltaFromLastLap')},false)"
        def delta_fill(dim):
            def c(argb):
                return '#59' + argb[3:] if dim else argb
            return f"if({delta}='GAIN','{c(PACE_PB)}',if({delta}='LOSS','{c(PACE_SLOWER)}','{BAND}'))"
        fill = (f"if({p(b + '.IsCurrent')},'{BAND_CURRENT}',"
                f"if({delta_last},{delta_fill(True)},{delta_fill(False)}))")
        # Main segment, and the part after start/finish for a turn that wraps across the line.
        items.append(rect(f'Band {i:02d}', sx, sy, 2, sh, BAND)
                     .show_if(f'{start} >= 0')
                     .bind('Left', f'{sx} + {start} * {sw}')
                     .bind('Width', f'max(2, if({end} >= {start}, {end} - {start}, 1 - {start}) * {sw})')
                     .bind('BackgroundColor', fill))
        items.append(rect(f'Band {i:02d} wrap', sx, sy, 2, sh, BAND)
                     .show_if(f'{start} >= 0 && {end} < {start}')
                     .bind('Width', f'max(2, {end} * {sw})')
                     .bind('BackgroundColor', fill))
        centre = f'(({start} + if({end} >= {start}, {end}, {end} + 1)) / 2) % 1'
        width_px = f'if({end} >= {start}, {end} - {start}, 1 - {start}) * {sw}'
        # Full delta ("+0.11") on wide bands; on narrow ones the sign and the digits are stacked so the digits get the
        # band's full width ("+" over ".11"; user request 2026-10-03).
        shown = f"{start} >= 0 && {delta} <> 'NONE' && !{p(b + '.IsCurrent')}"
        items.append(text(f'Band {i:02d} delta', sx, sy + 19, 60, 22, '', 13, INK, CENTER)
                     .show_if(f"{shown} && {width_px} >= 34")
                     .bind('Left', f'{sx} + {centre} * {sw} - 30')
                     .bind('Text', f"isnull({p(b + '.DeltaText')},'')")
                     .bind('TextColor', f"if({delta_last},'{TEXT}','{INK}')"))
        narrow = f"{shown} && {width_px} >= 18 && {width_px} < 34"
        items.append(text(f'Band {i:02d} delta sign', sx, sy + 9, 40, 14, '', 12, INK, CENTER)
                     .show_if(narrow)
                     .bind('Left', f'{sx} + {centre} * {sw} - 20')
                     .bind('Text', f"isnull({p(b + '.DeltaSign')},'')")
                     .bind('TextColor', f"if({delta_last},'{TEXT}','{INK}')"))
        items.append(text(f'Band {i:02d} delta digits', sx, sy + 22, 40, 16, '', 12, INK, CENTER)
                     .show_if(narrow)
                     .bind('Left', f'{sx} + {centre} * {sw} - 20')
                     .bind('Text', f"isnull({p(b + '.DeltaDigits')},'')")
                     .bind('TextColor', f"if({delta_last},'{TEXT}','{INK}')"))
        # Track limits: a red bar along the band's foot (bright this lap, dim earlier this session); the number
        # turns red for an excursion this lap unless it's the turn you're in (blue wins there).
        lap_hits, session_hits = f"isnull({p(b + '.LimitsLap')},0)", f"isnull({p(b + '.LimitsSession')},0)"
        items.append(rect(f'Band {i:02d} limits', sx, sy + sh - 8, 2, 8, BAD)
                     .show_if(f'{start} >= 0 && {session_hits} > 0')
                     .bind('Left', f'{sx} + {start} * {sw}')
                     .bind('Width', f'max(6, if({end} >= {start}, {end} - {start}, 1 - {start}) * {sw})')
                     .bind('BackgroundColor', f"if({lap_hits} > 0,'{BAD}','{LIMITS_DIM}')"))
        items.append(text(f'Band {i:02d} label', sx, TOP + 62, 40, 26, '', 18, MUTED, CENTER)
                     .show_if(f'{start} >= 0')
                     .bind('Left', f'{sx} + {centre} * {sw} - 20')
                     .bind('Text', f"isnull({p(b + '.Label')},'')")
                     .bind('TextColor', f"if({p(b + '.IsCurrent')},'{CURRENT}',if({lap_hits} > 0,'{BAD}','{MUTED}'))"))

    # White, not the accent: lime would read as a pace colour against the sector fills.
    items.append(rect('Lap cursor', sx, sector_y, 3, sy + sh - sector_y, TEXT)
                 .show_if(p('Caps.LapPosition'))
                 .bind('Left', f"{sx} + isnull({p('Live.StripPos')},0) * {sw} - 1"))
    items.append(text('No lap position', sx, sy, sw, sh, 'THIS SIM REPORTS NO LAP POSITION', 20, MUTED, CENTER)
                 .show_if(f"!{p('Caps.LapPosition')}"))
    items.append(text('Unsupported notice', sx, sy, sw, sh, 'NO TURN DATA FOR THIS TRACK', 22, CURRENT, CENTER)
                 .show_if(p('Turn.NoticeVisible')))


def live_window(items):
    label_w = 140
    x, w = CX + label_w, CW - label_w - 20

    def lane(key, label, top, height, guides):
        items.append(panel(f'{key} panel', CX, top, CW, height))
        items.append(text(f'{key} label', CX + 20, top + 12, label_w - 30, 28, label, 20, LABEL))
        inner_top, inner_h = top + 16, height - 32
        for frac, caption in guides:
            y = inner_top + inner_h * frac
            items.append(rect(f'{key} guide {caption}', x, y, w, 1, GUIDE))
            items.append(text(f'{key} guide label {caption}', x - 52, y - 11, 44, 22, caption, 15, MUTED, RIGHT))
        return inner_top, inner_h

    # One box for all inputs (user request 2026-10-03): pedals 0-100 %, steering centred on the 50 line
    # (full right lock at the top, full left at the bottom).
    top, h = lane('Inputs', 'INPUTS', INPUTS_TOP, INPUTS_H, [(0, '100'), (0.5, '50'), (1, '0')])
    items.append(chart('Steering chart', x, top, w, h, f"isnull({p('Live.Steer')},0)", STEER, -1, 1,
                       POINTS, INTERVAL_MS, CHART_WIDTH).show_if(p('Caps.Steering')))
    items.append(chart('Throttle chart', x, top, w, h, f"isnull({p('Live.Throttle')},0)", THROTTLE, 0, 100,
                       POINTS, INTERVAL_MS, CHART_WIDTH))
    items.append(chart('Brake chart', x, top, w, h, f"isnull({p('Live.Brake')},0)", BRAKE, 0, 100,
                       POINTS, INTERVAL_MS, CHART_WIDTH))
    items.append(text('Throttle key', CX + 20, top + 36, 110, 24, 'THROTTLE', 16, THROTTLE))
    items.append(text('Brake key', CX + 20, top + 62, 110, 24, 'BRAKE', 16, BRAKE))
    items.append(text('Steering key', CX + 20, top + 88, 110, 24, 'STEERING', 16, STEER).show_if(p('Caps.Steering')))
    items.append(text('No steering', CX + 20, top + 88, 110, 24, 'NO STEERING', 16, MUTED).show_if(f"!{p('Caps.Steering')}"))
    items.append(text('Steering right', x + w - 30, top + 2, 26, 20, 'R', 14, MUTED, RIGHT).show_if(p('Caps.Steering')))
    items.append(text('Steering left', x + w - 30, top + h - 22, 26, 20, 'L', 14, MUTED, RIGHT).show_if(p('Caps.Steering')))

    items.append(panel('Events panel', CX, 846, CW, 128))
    # TURN is neutral so it never reads as TC (amber); the lap strip carries the amber "current turn" cue.
    rows = [('TURN', 'Live.InTurn', LANE_TURN, None), ('TC', 'Live.TC', TC, 'Caps.TC'), ('ABS', 'Live.ABS', ABS, 'Caps.ABS')]
    for row, (label, prop, color, cap) in enumerate(rows):
        y = 856 + row * 38
        items.append(text(f'{label} lane label', CX + 20, y + 4, 110, 28, label, 18, color))
        items.append(rect(f'{label} lane base', x, y + 33, w, 1, GUIDE))
        c = chart(f'{label} chart', x, y, w, 34, f'if(isnull({p(prop)},false),1,0)', color, 0, 1,
                  POINTS, INTERVAL_MS, 2)
        items.append(c.show_if(p(cap)) if cap else c)

    for frac, caption, align in ((0, f'−{SECONDS:.0f} s', LEFT), (0.5, f'−{SECONDS / 2:.0f} s', CENTER),
                                 (1, 'NOW', RIGHT)):
        items.append(text(f'Axis {caption}', x + w * frac - 60 * align, BOTTOM + 2, 120, 22, caption, 15, MUTED, align))


STANDINGS_TOP, STANDINGS_H = TOP + STRIP_H + 16, 246
INPUTS_TOP = STANDINGS_TOP + STANDINGS_H + 16
INPUTS_H = 830 - INPUTS_TOP
STANDING_ROWS = 6     # StandingsBoard.RowCount in the plugin


def standings(items):
    """Position, driver, best sectors, best lap, its delta to the session's fastest, and gap to the leader."""
    top = STANDINGS_TOP
    items.append(panel('Standings panel', CX, top, CW, STANDINGS_H))
    items.append(text('Standings label', CX + 20, top + 10, 200, 26, 'STANDINGS', 18, LABEL))
    items.append(text('Standings count', CX + 150, top + 10, 120, 26, '', 15, MUTED)
                 .bind_js('Text', f"var n=$prop('{TT}Standings.DriverCount'); return n ? n + ' CARS' : '';"))
    # (key, header, x offset, width, align); P and DRIVER sit under the panel label, so they have no header.
    cols = [('Position', None, 20, 44, CENTER), ('Name', None, 76, 300, LEFT),
            ('S1', 'S1', 386, 100, RIGHT), ('S2', 'S2', 496, 100, RIGHT), ('S3', 'S3', 606, 100, RIGHT),
            ('Best', 'BEST', 726, 130, RIGHT), ('BestDelta', 'DELTA', 866, 110, RIGHT), ('Gap', 'GAP', 986, 94, RIGHT)]
    for key, header, dx, cw, align in cols:
        if header:
            items.append(text(f'Standings head {header}', CX + dx, top + 10, cw, 26, header, 15, MUTED, align))
    player_row = '#40' + CURRENT[3:]
    for i in range(1, STANDING_ROWS + 1):
        y = top + 42 + (i - 1) * 32
        r = f'Standings.Row{i}.'
        items.append(rect(f'Row {i} player', CX + 12, y, CW - 24, 30, CLEAR, radius_only(3))
                     .bind('BackgroundColor', f"if(isnull({p(r + 'IsPlayer')},false),'{player_row}','{CLEAR}')"))
        for key, header, dx, cw, align in cols:
            t = text(f'Row {i} {key}', CX + dx, y, cw, 30, '', 20, MUTED if key in ('Position', 'BestDelta') else TEXT, align)
            t.bind('Text', f"isnull({p(r + key)},'')")
            if key in ('S1', 'S2', 'S3', 'Best'):
                t.bind('TextColor', f"if(isnull({p(r + key + 'Fastest')},false),'{FASTEST}','{TEXT}')")
            elif key == 'Name':
                t.bind('TextColor', f"if(isnull({p(r + 'IsPlayer')},false),'{CURRENT}','{TEXT}')")
            items.append(t)
    items.append(text('No standings', CX + 20, top + 42, CW - 40, 32 * STANDING_ROWS, 'NO STANDINGS FROM THIS SIM YET',
                      20, MUTED, CENTER).show_if(f"isnull({p('Standings.DriverCount')},0) = 0"))


CONDITIONS_H = 200


def conditions(items):
    """Wipers and headlights (sim adapter; N/A where not reported), then ambient air and track temperature."""
    items.append(panel('Conditions panel', RX, TOP, RW, CONDITIONS_H))
    for key, label, x, value in (('Wipers', 'WIPERS', RX + 20, 'Car.Wipers'), ('Lights', 'LIGHTS', RX + 200, 'Car.Lights')):
        on = f"isnull({p(value)},-1) > 0"
        items.append(text(f'{key} label', x, TOP + 10, 150, 26, label, 18, LABEL))
        items.append(rect(f'{key} light', x, TOP + 50, 22, 22, LIGHT_OFF, border(HAIRLINE, 1, 11))
                     .bind('BackgroundColor', f"if({on},'{ACCENT}','{LIGHT_OFF}')"))
        items.append(text(f'{key} state', x + 32, TOP + 40, 130, 42, '', 28, MUTED, LEFT, display=True)
                     .bind('Text', f"isnull({p(value + 'Text')},'N/A')")
                     .bind('TextColor', f"if({on},'{TEXT}','{MUTED}')"))
    items.append(rect('Conditions divider', RX + 20, TOP + 98, RW - 40, 1, GUIDE))
    for key, label, x, field in (('Air', 'AIR', RX + 20, 'AirTemperature'), ('Track', 'TRACK', RX + 200, 'RoadTemperature')):
        items.append(text(f'{key} temp label', x, TOP + 108, 150, 26, label, 18, LABEL))
        items.append(text(f'{key} temp', x, TOP + 134, 160, 56, '', COND_TEMP_SIZE, TEXT, LEFT, number=True)
                     .bind_js('Text', (f"var t=$prop('{GD}{field}'); if(!t) return '';"
                                       f"var u=$prop('{TT}Tyre.TempUnit')||'';"
                                       "return Math.round(t) + (u==='K' ? ' K' : '\u00b0' + u);")))


def tyres(items):
    conditions(items)
    top = TOP + CONDITIONS_H + 16
    items.append(panel('Tyres panel', RX, top, RW, BOTTOM - top))
    items.append(text('Tyres label', RX + 20, top + 10, 200, 30, 'TYRES', 20, LABEL))
    items.append(text('Temp unit', RX + RW - 120, top + 10, 100, 30, '°C', 18, MUTED, RIGHT)
                 .bind('Text', f"if(isnull({p('Tyre.TempUnit')},'C')='K','K','°' + isnull({p('Tyre.TempUnit')},'C'))"))

    # Shorter tyres (user request 2026-10-03: room for wipers / lights / temperatures above).
    tw, th = 110, 130
    zones = [36, 38, 36]
    positions = {'FL': (RX + 50, top + 72), 'FR': (RX + RW - 50 - tw, top + 72),
                 'RL': (RX + 50, top + 352), 'RR': (RX + RW - 50 - tw, top + 352)}
    for corner, (x, y) in positions.items():
        t = f'Tyre.{corner}'
        # Inner is always nearest the car's centreline: O M I on the left, I M O on the right.
        order = ['O', 'M', 'I'] if corner in ('FL', 'RL') else ['I', 'M', 'O']
        items.append(text(f'{corner} label', x, y - 30, tw, 24, corner, 16, MUTED, CENTER))
        zx = x
        for k, (zone, zw) in enumerate(zip(order, zones)):
            corners = 'tl bl' if k == 0 else 'tr br' if k == 2 else ''
            style = radius_only(16, corners) if corners else {}
            items.append(rect(f'{corner} zone {zone}', zx, y, zw, th, NODATA, style)
                         .bind('BackgroundColor', f"isnull({p(t + '.Color' + TYRE_COLOR_SUFFIX + '.' + zone)},'{NODATA}')"))
            zx += zw
        # One colour per tyre until the sim proves it reports inner/middle/outer (ACC reports only an average).
        items.append(rect(f'{corner} single', x, y, tw, th, NODATA, radius_only(16))
                     .show_if(f"!{p('Tyre.ZonesDetected')}")
                     .bind('BackgroundColor', f"isnull({p(t + '.Color' + TYRE_COLOR_SUFFIX + '.Avg')},'{NODATA}')"))
        items.append(rect(f'{corner} outline', x, y, tw, th, CLEAR, border(TYRE_OUTLINE, 1, 16)))
        items.append(text(f'{corner} temp', x, y + (th - 70) / 2, tw, 70, '', 44, INK, CENTER).bind_js('Text', (
            f"var t=$prop('{TT}{t}.Temp.Avg'); return t>0 ? Math.round(t) : '';")))

        items.append(text(f'{corner} pressure', x - 15, y + th + 10, tw + 30, 46, '', 36, TEXT, CENTER)
                     .bind('TextColor', f"isnull({p(t + '.PressureColor' + TYRE_COLOR_SUFFIX)},'{TEXT}')")
                     .bind_js('Text', (
                         f"var v=$prop('{TT}{t}.Pressure'); if(v==null) return '';"
                         f"return v.toFixed($prop('{TT}Tyre.PressureUnit')==='bar' ? 2 : 1);")))
        items.append(text(f'{corner} pressure unit', x, y + th + 54, tw, 22, 'PSI', 15, MUTED, CENTER)
                     .bind('Text', f"isnull({p('Tyre.PressureUnit')},'psi')"))

        # Brake temperature (user request 2026-10-03), styled after the Button Box (button-box-neon): a bar on the
        # outer side of the tyre, outlined in the band colour (blue < 200 °C, green to 650, yellow to 800, red), filled
        # from the bottom in a darker shade of it over 0-1000 °C, with the number in white so it reads wherever the
        # fill ends. Hidden until the sim reports a brake temperature.
        bw, inset = 34, 2
        bx = x - bw - 10 if corner in ('FL', 'RL') else x + tw + 10
        b = f'Brake.{corner}'
        fill = f"isnull({p(b + '.Fill')},0)"
        color = p(b + '.Color' + TYRE_COLOR_SUFFIX)
        reported = f"isnull({p('Brake.Reported')},false)"
        inner_h = th - 2 * inset
        items.append(rect(f'{corner} brake outline', bx, y, bw, th, NODATA, radius_only(7))
                     .show_if(reported)
                     .bind('BackgroundColor', f"isnull({color},'{NODATA}')"))
        items.append(rect(f'{corner} brake track', bx + inset, y + inset, bw - 2 * inset, inner_h, BRAKE_TRACK,
                          radius_only(5)).show_if(reported))
        items.append(rect(f'{corner} brake fill', bx + inset, y + inset, bw - 2 * inset, inner_h, NODATA, radius_only(5))
                     .show_if(f'{reported} && {fill} > 0')
                     .bind('Top', f'{y + inset} + {inner_h} * (1 - {fill})')
                     .bind('Height', f'max(2, {inner_h} * {fill})')
                     .bind_js('BackgroundColor', f"var c=$prop('{TT}{b}.Color{TYRE_COLOR_SUFFIX}')||'{NODATA}';"
                                                 "return '#66' + c.substring(3);"))
        items.append(text(f'{corner} brake temp', bx - 6, y + th / 2 - 12, bw + 12, 24, '', 14, '#FFFFFFFF', CENTER)
                     .show_if(reported)
                     .bind_js('Text', f"var t=$prop('{TT}{b}.Temp'); return t>0 ? Math.round(t) : '';"))
        items.append(text(f'{corner} brake label', bx - 6, y + th + 2, bw + 12, 16, 'BRK', 11, MUTED, CENTER)
                     .show_if(reported))

        wear_cap = p('Caps.TyreWear')
        items.append(rect(f'{corner} wear track', x, y + th + 86, tw, 6, WEAR_TRACK, radius_only(3)).show_if(wear_cap))
        items.append(rect(f'{corner} wear fill', x, y + th + 86, tw, 6, TEXT, radius_only(3))
                     .show_if(wear_cap)
                     .bind('Width', f"max(1, isnull({p(t + '.Wear')},0) / 100 * {tw})")
                     .bind('BackgroundColor', f"isnull({p(t + '.WearColor' + TYRE_COLOR_SUFFIX)},'{TEXT}')"))
        items.append(text(f'{corner} wear', x, y + th + 96, tw, 24, '', 16, MUTED, CENTER)
                     .show_if(wear_cap)
                     .bind_js('Text', f"var w=$prop('{TT}{t}.Wear'); return w==null ? '' : Math.round(w) + '%';"))

    items.append(text('Compound', RX + 20, BOTTOM - 44, RW - 40, 30, '', 16, MUTED, CENTER).bind_js('Text', (
        f"var c=$prop('{TT}Tyre.Compound')||''; var pr=$prop('{TT}Tyre.Preset')||'';"
        "return (c ? c.toUpperCase().replace('_',' ') + '  ·  ' : '') + pr.toUpperCase();")))


def status_bar(items):
    items.append(panel('Status bar', 24, 1000, 1872, 60))

    def setting(key, label, x, label_w, value_expr, js=False, fmt=None):
        items.append(text(f'{key} status label', x, 1005, label_w, 50, label, 22, MUTED))
        value = text(f'{key} value', x + label_w, 1005, 70, 50, '', 28, TEXT)
        if js:
            value.bind_js('Text', value_expr)
        else:
            value.bind('Text', value_expr, fmt=fmt)
        items.append(value)
        return value

    def light(key, x, active, color):
        items.append(rect(f'{key} light', x, 1018, 24, 24, LIGHT_OFF, border(HAIRLINE, 1, 12))
                     .bind('BackgroundColor', f"if(isnull([{GD}{active}],false),'{color}','{LIGHT_OFF}')"))

    setting('TC', 'TC', 48, 44, f"isnull([{GD}TCLevel],'')")
    light('TC', 142, 'TCActive', TC)
    # TC cut isn't normalised by SimHub; the plugin's adapter reads it (ACC) and reports N/A elsewhere.
    cut = setting('TC cut', 'TC CUT', 190, 82, f"var v=$prop('{TT}Car.TCCut'); return v==null ? 'N/A' : v;", js=True)
    cut.bind('TextColor', f"if(isnull({p('Car.TCCut')},-1) < 0,'{MUTED}','{TEXT}')")
    setting('ABS', 'ABS', 360, 52, f"isnull([{GD}ABSLevel],'')")
    light('ABS', 462, 'ABSActive', ABS)
    setting('BB', 'BB', 510, 40, f'isnull([{GD}BrakeBias],0)', fmt='0.0')
    setting('MAP', 'MAP', 640, 56, f"isnull([{GD}EngineMap],'')")
    if SLASH:
        items.append(image('Status slash', 820, 1010, 240, 40, 'slash'))
    items.append(text('Plugin info', 1080, 1005, 520, 50, '', 16, MUTED, RIGHT).bind_js('Text', (
        f"var v=$prop('{TT}Version'); if(!v) return '';"
        f"return 'TURN TELEMETRY ' + v + '  \u00b7  ' + ($prop('{TT}Adapter')||'').toUpperCase();")))

    # Flag: the highest-priority flag SimHub reports, as a colour swatch with its name. While a flag is out the swatch
    # flashes (350 ms phases from the clock) between the flag colour and a dark tint (black and chequered invert
    # instead) inside a bright frame; no blur or glow, so no GPU effects. Steady and grey when there's no flag.
    flag_table = ("var g='" + GD + "';"
                  "function f(n){ return $prop(g + 'Flag_' + n) == 1; }"
                  # name, on background, on text, label, off background, off text
                  "var all=[['Black','#FF111111','#FFFFFFFF','BLACK','#FFFFFFFF','#FF000000'],"
                  "['Checkered','#FFFFFFFF','#FF000000','CHEQUERED','#FF000000','#FFFFFFFF'],"
                  "['Yellow','#FFFFD000','#FF000000','YELLOW','#40FFD000','#FFFFD000'],"
                  "['Blue','#FF1E6BFF','#FFFFFFFF','BLUE','#401E6BFF','#FF5A95FF'],"
                  "['Orange','#FFFF8A00','#FF000000','ORANGE','#40FF8A00','#FFFF8A00'],"
                  "['White','#FFFFFFFF','#FF000000','WHITE','#40FFFFFF','#FFFFFFFF'],"
                  "['Green','#FF2EE66B','#FF000000','GREEN','#402EE66B','#FF2EE66B']];"
                  "var flag=null; for (var i=0;i<all.length;i++) { if (f(all[i][0])) { flag=all[i]; break; } }"
                  "var on = Math.floor(Date.now() / 350) % 2 == 0;")

    def flag_js(on_index, off_index, default):
        return flag_table + f"if (!flag) return {default}; return on ? flag[{on_index}] : flag[{off_index}];"

    items.append(text('Flag label', 1620, 1005, 70, 50, 'FLAG', 22, MUTED))
    items.append(rect('Flag swatch', 1690, 1012, 182, 36, LIGHT_OFF, border(HAIRLINE, 1, 6))
                 .bind_js('BackgroundColor', flag_js(1, 4, f"'{LIGHT_OFF}'")))
    items.append(rect('Flag frame', 1687, 1009, 188, 42, CLEAR, border(TEXT, 2, 8))
                 .bind_js('Visible', flag_table + "return flag != null;"))
    items.append(text('Flag name', 1690, 1012, 182, 36, '', FLAG_SIZE, MUTED, CENTER, display=True)
                 .bind_js('Text', flag_table + "return flag ? flag[3] : 'NO FLAG';")
                 .bind_js('TextColor', flag_js(2, 5, f"'{MUTED}'")))


def plugin_banner(items):
    missing = f"isnull({p('Contract')},0) != {CONTRACT}"
    items.append(rect('Plugin banner', 460, 430, 1000, 220, '#F00B0E13', border(CURRENT, 2, 8)).show_if(missing))
    items.append(text('Plugin banner title', 460, 460, 1000, 70, 'TURN TELEMETRY PLUGIN NOT FOUND', 40, CURRENT, CENTER)
                 .show_if(missing))
    items.append(text('Plugin banner detail', 460, 540, 1000, 80,
                      f'Install or update the plugin (contract v{CONTRACT}) and restart SimHub.', 24, MUTED, CENTER)
                 .show_if(missing))


def build(theme):
    globals().update({k: v for k, v in theme.items() if k.isupper()})
    dashlib.apply_theme(theme)
    globals()['FONT'] = dashlib.FONT
    items = [image('Carbon background', 0, 0, W, H, 'carbon')]
    top_bar(items)
    focal_column(items)
    lap_strip(items)
    standings(items)
    live_window(items)
    tyres(items)
    status_bar(items)
    plugin_banner(items)  # last, so it draws on top

    out = ROOT / 'dash' / NAME
    lo, hi = CARBON
    # Background art: the carbon weave, or the livery artwork (which includes the weave).
    images = {'carbon': livery.background(W, H) if BACKGROUND == 'livery' else carbon.render(W, H, lo, hi)}
    if SLASH:
        images['slash'] = carbon.slashes(240, 56, SLASH)
    dash = write_dash(out, NAME, TITLE, 'Turn-by-turn telemetry: turns and sectors this lap, standings, live inputs, tyres (needs the Turn Telemetry plugin)',
                      [screen('Main', items)], images, W, H, VERSION)
    bad = check_bounds(dash, W, H)
    if bad:
        raise SystemExit(f'items out of bounds: {bad}')
    # Every font must be known to both Dash Studio (DashFonts family) and the tablet (Web/FontFaces.css), with glyphs
    # for what it shows; otherwise the tablet silently falls back to a serif.
    font_problems = fontcheck.check_dash(out / f'{NAME}.djson')
    if font_problems:
        raise SystemExit('font check failed:\n  ' + '\n  '.join(font_problems))
    print(f'built {out} ({len(items)} items)')
    return out


# Real in-game screenshot of the released dashboard (ACC Silverstone, 2026-10-03: sector row, corner deltas, brake bars), used as its SimHub thumbnail.
SCREENSHOT = ROOT / 'docs' / 'images' / 'screenshot-dashboard.png'


def thumbnail(out, name, key):
    """SimHub's dashboard thumbnail: <name>.djson.png (main preview) and <name>.djson.00.png (screen 0). The released
    look uses the real in-game screenshot; other themes get a preview_dash.py render with sample data. install() and
    package_release.py pick them up with the other <name>.djson* files."""
    import subprocess
    import sys
    png = Path(out) / f'{name}.djson.png'
    if key == 'livery' and SCREENSHOT.exists():
        shutil.copy2(SCREENSHOT, png)
    else:
        theme_args = [] if key == 'classic' else ['--theme', key]
        subprocess.run([sys.executable, str(ROOT / 'tools' / 'preview_dash.py'), str(Path(out) / f'{name}.djson'), str(png)]
                       + theme_args, check=True, capture_output=True)
        png.with_suffix('.html').unlink(missing_ok=True)
    shutil.copy2(png, Path(out) / f'{name}.djson.00.png')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--install', action='store_true', help='copy into SimHub DashTemplates')
    # livery is the released Turn Telemetry Dashboard; classic and neon are optional looks (not shipped).
    parser.add_argument('--theme', choices=sorted(themes.THEMES) + ['all'], default='livery')
    args = parser.parse_args()
    for key in sorted(themes.THEMES) if args.theme == 'all' else [args.theme]:
        out = build(themes.THEMES[key])
        thumbnail(out, themes.THEMES[key]['NAME'], key)
        if args.install:
            print('installed to', install(out, themes.THEMES[key]['NAME']))
