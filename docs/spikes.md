# Spike results

Fill this in while running the [Live Trace Spike](../dash/LiveTraceSpike/README.md). Pass criteria are in [plan.md](plan.md#phase-0-live-window-spike-go-no-go).

Environment: SimHub 9.11.21 · tablet: _model / browser_ · date: _

## S2 / S2b: ChartItem live window on the web client

| Screen | Dash Studio (PC) | Tablet: smooth? | Actual window (s) | SimHub CPU (no tablet → tablet) |
| --- | --- | --- | --- | --- |
| 30 Hz (33 ms × 180) | | | | |
| 60 Hz (16 ms × 360) | | | | |

Notes:

## S4: Steering raw value

| Sim | Raw at full right | Raw at full left | Right draws upward? | Notes |
| --- | --- | --- | --- | --- |
| AC | | | | |
| ACC | | | | |

## S6a: TC / ABS flags

| Sim | TCActive fires? | ABSActive fires? | Notes |
| --- | --- | --- | --- |
| AC | | | |
| ACC | | | |

## Verdict

**2026-10-02: S2 pass.** The user reported that "everything runs smoothly" on the tablet. Both screens rendered, and the steering, TC/ABS lanes, and lights behaved as expected. No per-screen CPU figures or raw steering values were recorded, so fill in the tables above if exact numbers are needed later.

- [x] S2 pass: use native charts for the live window (D11 confirmed)
- [ ] S2 fail: move to the plugin-rendered image test (S1)
