# Turn Telemetry Dashboard

The released 1920×1080 dashboard (the "livery" token set in [`tools/themes.py`](../../tools/themes.py)). It needs the Turn Telemetry plugin; without it, the screen shows a "plugin not found" banner. Layout and colour rules are in [docs/design.md](../../docs/design.md); bindings follow the property contract in [docs/architecture.md](../../docs/architecture.md#property-contract-plugin--dashboard). For users: [docs/user-guide.md](../../docs/user-guide.md).

![Approximate preview with sample data](../../docs/images/dashboard-livery.png)

*Rendered by `tools/preview_dash.py` with sample values, not by SimHub.*

**These files are generated. Don't edit them by hand.** Change [`tools/build_dash.py`](../../tools/build_dash.py) and rebuild:

```powershell
python tools/build_dash.py --install                 # this dashboard, and copy into SimHub\DashTemplates
python tools/build_dash.py --theme all               # also the unreleased classic / neon looks (dash/TurnTelemetry, dash/TurnTelemetryNeon)
python tools/preview_dash.py dash/TurnTelemetryDashboard/TurnTelemetryDashboard.djson preview.png --theme livery
python tools/package_release.py                      # release zip + .simhubdash into dist/
```

## Tablet test

Open **TurnTelemetry** in Dash Studio once, then view it on the tablet (`http://<pc-ip>:8888`).

| # | Check | Expected | Result |
| --- | --- | --- | --- |
| D1 | Opens in Dash Studio | No load errors; carbon background and panels appear | |
| D2 | Plugin banner | Hidden while the plugin is enabled. If shown, the dashboard can't see `TurnTelemetry.Contract`. | |
| D3 | Top bar | Short game name (e.g. `ACC`), track, car; lap number, current lap time, delta (green when ahead), best lap | |
| D4 | Live window | Same smoothness as LiveTraceSpike; throttle, brake, and steering track inputs | |
| D5 | Event lanes | TURN steps up inside recorded turns; TC/ABS fire on intervention | |
| D6 | Lap strip, no turn data | Cursor moves across the lap; caption "NO TURN DATA · RECORD WITH…"; focal tile says NO TURN DATA | |
| D7 | Lap strip with turns | After recording a few turns: bands appear with labels, the current one turns amber; focal tile NEXT → amber CURRENT with frame | |
| D8 | Tyres (ACC) | One colour per tyre, temperatures in °F, pressures coloured LOW (blue) / OK (white) / HIGH (red), no wear bar | |
| D9 | Tyres (AC) | Zones may split into O/M/I stripes (`zones=True` in diagnostics); wear bar shown | |
| D10 | Forza Horizon | Lap strip shows "THIS SIM REPORTS NO LAP POSITION"; TC/ABS rows hidden; steering moves | |
| D11 | Readability | Legible at arm's length; nothing clipped or overlapping | |
| D12 | Turn tile | Only the number (and corner name). It turns pink with a frame while you're in the corner | |
| D13 | Track map | The TRACK panel shows the circuit, your dot, other cars, and the start line | |
| D14 | Buttons reach the plugin | Tapping SET LINE on the **tablet** shows a message in the tile. If nothing happens, the button-to-action link (`TriggerAction`) needs adjusting; the wheel-button action `SetStartLine` works regardless | |
| D15 | SET LINE | Tap it as you cross the start line: *LINE SET · TURN 1 = …*, and the next corner shows 1 | |
| D16 | Undo / reset | The button reads UNDO for 5 s, and tapping it restores the numbering. Later, RESET (visible when you've set a line) returns to the default | |

