# Live Trace Spike (Phase 0)

A plugin-free test dashboard. It checks whether SimHub's native `ChartItem` can draw a smooth 6-second live trace on a 1920×1080 tablet. Background and pass criteria are in [docs/plan.md](../../docs/plan.md#phase-0-live-window-spike-go-no-go).

**These files are generated. Don't edit them by hand.** Change [`tools/build_spike_dash.py`](../../tools/build_spike_dash.py) and rebuild:

```powershell
python tools/build_spike_dash.py            # build into dash/LiveTraceSpike
python tools/build_spike_dash.py --install  # build and copy into SimHub\DashTemplates
```

## Screens

| Screen | Sample interval | Points | Window |
| --- | --- | --- | --- |
| Spike 30 Hz | 33 ms | 180 | ~6 s |
| Spike 60 Hz | 16 ms | 360 | ~6 s |

Switch between them with the on-screen arrows. The top-right caption shows which screen is active.

Each screen has:

- **Pedals:** throttle (green) and brake (red), overlaid, 0–100%
- **Steering:** blue, −1…+1, right turn should draw upward
- **TC / ABS:** 0/1 event lanes, plus the lights in the status bar
- **Diagnostics** (bottom right): raw `Physics.SteerAngle` value and the raw TC/ABS flags

## Test procedure

1. In SimHub, open **Dash Studio**, find **LiveTraceSpike**, and open it once in the editor. If it fails to load, note the error. Otherwise check that the carbon background and panels appear.
2. Start AC (then ACC) and drive a few laps somewhere with heavy braking, with TC and ABS switched on.
3. View the dashboard **on the tablet** through SimHub's web dashboard (`http://<pc-ip>:8888`), full screen at 1920×1080.
4. For each screen (30 Hz and 60 Hz), record:
   - Does the trace scroll smoothly, or does it stutter, jump, or blank out?
   - Does the window actually cover ~6 s? Count seconds from a brake application at NOW until it leaves the left edge.
   - SimHub CPU in Task Manager, with and without the tablet connected
5. Steering check: at standstill, turn fully right and then fully left. Note the raw value at each lock and whether right draws upward.
6. TC/ABS check: trigger wheelspin out of a slow corner and lock the brakes into a heavy stop. Do the lights and event lanes react?

Record the results in [docs/spikes.md](../../docs/spikes.md).

## Knobs if something is off

All in `tools/build_spike_dash.py`:

| Symptom | Change |
| --- | --- |
| Right steering draws downward | `STEER_SIGN = -1` |
| Steering flat-lines at the top or bottom (raw value goes beyond ±1, e.g. radians) | Change the steering chart's min/max, or divide the expression by the car's lock |
| Window shorter than 6 s or choppy | Try a larger `interval_ms` with fewer points (e.g. 50 ms × 120) |
| CPU too high | Raise the screen or chart `RenderingSkip` |
