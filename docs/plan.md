# Turn Telemetry: Plan and decision record

## Decision record

| # | Decision | Choice | Rationale |
| --- | --- | --- | --- |
| D1 | Target display | 1920×1080 landscape tablet or 2nd monitor | Room for a full-lap strip, a live window, and tyres at once |
| D2 | Build approach | Dash Studio dashboard + C# SimHub plugin | Distance-aligned lap buffers, turn catalogue, per-sim steering, and tyre presets cannot be done reliably in dash JavaScript. Lovely Dashboard uses the same split. |
| D3 | Turn data source | Curated JSON + AC `sections.ini` + **turn editor** (amended 2026-10-03). The editor offers candidates (every AC section with its name, or corners detected from a lap), and the user picks the first turn, auto-numbers, adjusts, and saves. The manual recorder remains. | Originally "never auto-detect". After the first tablet test, the user found per-turn marking too much work, and AC files mostly have names without numbers while ACC has no files at all. Candidates are only proposals: nothing is used until the user reviews and saves, so displayed numbers are still the user's. |
| D4 | Trace layout | **Start with only the 6 s live window** (no history). The full-lap strip and ghost lap come after the renderer is proven. | Drawing a distance-aligned lap trace is the biggest technical risk. The live window can be built with SimHub's native `ChartItem`, without a plugin. |
| D5 | Reference lap | Session best valid lap, falling back to last lap | Most useful comparison; nothing to persist |
| D6 | Tyre colours | Per-sim/compound presets, computed in the plugin | Optimal windows vary a lot between cars and sims; keeps one colour scale |
| D7 | Turns-app parity | NEXT/CURRENT focal tile only | Per-turn cards and the theme catalogue are deferred |
| D8 | Telemetry colours | Brake red, throttle green, steering blue | Same as the Turns app |
| D9 | Dashboard source | Kept unpacked in git, packed by a tool | Reviewable diffs |
| D10 | First sims | AC and ACC | Installed locally, so they can be validated |
| D14 | Themes (2026-10-03) | A token set per theme (`tools/themes.py`) with one dashboard per theme: **Turn Telemetry** (classic) and **Turn Telemetry Neon**. The plugin publishes tyre colours in both palettes. | The user wanted a far more dramatic, neon, action-sports look. A separate dashboard keeps the classic one intact and lets the user switch in SimHub. The look is "inspired by" only, with no third-party logos or names. |
| D16 | Livery theme (2026-10-03) | Third dashboard, **Turn Telemetry Livery**: race-livery style with generated art (lime wedges, white drips, orange splatter), Eurostar race-number type, orange for the corner you're in | Requested from a livery reference image. Style only: no third-party logos, names, or race numbers. |
| D15 | SET LINE replaces Set Turn 1 (2026-10-03) | One tap, no mode: tap **SET LINE** where numbering should begin, normally as you cross the start/finish line, and the next corner ahead becomes Turn 1. The button reads **UNDO** for 5 s; **RESET** returns to default numbering. The turn tile shows only the number and corner name (no NEXT/CURRENT label; it turns pink with a frame inside the corner). The old "then" box now holds SimHub's track map. | The start line is the clearest reference for where numbering begins, and choosing a corner was less intuitive. The NEXT label read as part of the number. Contract bumped to 2: the `SetT1.*` properties and Set Turn 1 actions were removed. |
| D17 | First release, v0.2.0 (2026-10-03) | One dashboard ships: **Turn Telemetry Dashboard** (the livery look). Classic and Neon stay as optional build themes. The release zip has a one-click `Install.cmd` (finds SimHub, waits for it to close, unblocks and copies the DLLs, imports the dashboard) and a standalone `.simhubdash`. | The user picked the livery style to keep. "Turn Telemetry Dashboard" is shorter than "Turn based Telemetry Dashboard" and matches the plugin name. Users shouldn't need to copy DLLs by hand. |
| D18 | Colour rules (2026-10-03) | Purple/green/yellow mean pace only; red means track limits; neon blue means "you / now". Microsectors dropped. | Pink/lime highlights were confusing next to sector colours. Microsectors can't be compared with other drivers. |
| D19 | Turns strip reads Turn 1 → max (2026-10-03, issue #11) | The strip starts halfway between the corner before Turn 1 and Turn 1 (`StripLayout`), so corners always read 1, 2, 3 … left to right. The car marker starts wherever the sim's lap starts and wraps. No rotation when the sim's timing line already sits just before Turn 1 (most tracks) or when no corner is numbered 1. Lap and sector timing are unchanged; only the drawing moves. | Drivers and engineers talk in corner numbers, and the official numbering counts from the main series' start line (F1 at Silverstone: Abbey is Turn 1). A sim's timing line can be elsewhere: ACC's Silverstone line is on the National straight before Copse (T9), so the raw order was 9 … 18, 1 … 8, which read as broken during hotlaps and standing starts. Rejected: renumbering corners from the sim's line (it would contradict track maps, commentary and the curated data). |
| D20 | Game sectors kept; their own row when they can't line up (2026-10-03, issue #11) | Sectors stay the game's own sectors, which always begin at the sim's timing line. When the strip is rotated (D19) they'd read S2 · S3 · S1 · S2 over the turns, so they are drawn as a separate row of boxes in game order S1 · S2 · S3, deliberately not aligned to the turns. The turns strip keeps neutral dividers with S1/S2/S3 tags where each sector starts on track. When the line is just before Turn 1 the original aligned row is used (`Strip.Rotated`). | This is what real timing does: a series' sector 1 always starts at its timing line, and nobody renumbers sectors to match corner numbers, because sector times must match official timing to compare cars. Keeping the game's sectors means our times match the sim's HUD and splits, and **purple** (fastest of anyone) still works: SimHub's best sectors for other drivers are the game's sectors. Rejected: our own sectors starting at Turn 1 (would read in order, but times wouldn't match the game and purple would be impossible); ignoring data until Turn 1 on standing starts (only trims the first lap; every later lap still starts at the sim's line); reverting to timing-line order (loses D19). Showing them as separate boxes keeps sectors visibly distinct from turns (user requirement) instead of implying they line up. |
| D21 | Per-turn time deltas (2026-10-03, issue #12) | Each corner's band is coloured by the time gained (green) or lost (yellow) in that corner against **your best clean time through that corner** on any lap (a theoretical best; amended the same day), with the delta inside wider bands; last lap's results show dimmed until reached. A corner with a track-limits excursion (or the pit lane) gets no colour and never sets a best. The lap is partitioned into one segment per corner, from midway after the previous corner to midway before the next (first and last reach the line). Times come from when the car enters each distance bin of the lap (`LapTrace.EntryTime`). | Engineers analyse driving corner by corner, not by the three timing sectors: in MoTeC or Pi they split the lap into per-corner segments and compare against a reference lap. That fits a turn-based dashboard and reads in corner order regardless of where the game's sectors fall. Partitioning the whole lap (straights included) means nothing is hidden between corners (against a per-corner best the deltas no longer sum to the lap delta, which compares whole laps), and exit speed is credited to the corner it came from: the first half of the following straight belongs to that corner's segment, the second half (the approach and braking for the next corner) to the next one. Reference: first built against the session-best valid lap, but a lap invalidated anywhere then gave no colours at all (user test, ACC Silverstone hotlap: both laps cut, bands stayed grey). Pros never use a lap that gained from track limits as a reference; engineers compare against a clean representative lap or a **theoretical best** made of the best time through each corner. The per-corner clean best is that theoretical best, and since excursions are pinned to corners, a cut only discards that corner's time instead of the whole lap. Rejected: best valid lap only (nothing until a fully clean lap); best lap even if invalid (a cut lap could become the reference). Green/yellow follow the pace colours (D18); red stays reserved for track limits, already shown on the cut corner. No delta: a corner's first clean pass (it sets the best), a stop or spin (segment > 120 s), or a turn crossing the line. |
| ~~D13~~ | ~~Set Turn 1 from the dashboard~~ | Superseded by D15. | |
| D12 | Turn data base (2026-10-03) | Every track starts with turns: curated official numbering where verified; circuit **templates** that carry official numbering to the same circuit in other sims; then automatic numbering from AC section names or SimHub map geometry, clearly flagged as automatic | The user asked for a base instead of starting from scratch. SimHub's own track maps (exact positions in each sim's lap scale) make this possible without driving. Templates are accepted only on a strong geometric match (≥ 0.80; wrong circuits ≤ 0.60), so a mismatch falls back to automatic numbering rather than wrong official numbers. |
| D11 | Live-window renderer (**confirmed 2026-10-02**, [spikes.md](spikes.md)) | Native `ChartItem`: one chart per channel, overlaid with transparent backgrounds | `ChartItem` draws one line per chart (single value, colour, and thickness), plots against time (`PointsCount` × `MinimumRefreshIntervalMS`), and has a fixed min/max. The existing *Button Box* dashboard already overlays Throttle and Brake charts this way. |

## Phase 0: Live-window spike (go/no-go)

A plugin-free test dashboard ([`dash/LiveTraceSpike`](../dash/LiveTraceSpike/README.md)), generated by [`tools/build_spike_dash.py`](../tools/build_spike_dash.py). It contains:

- pedals lane: throttle (green) and brake (red) charts overlaid, 0–100
- steering lane: steering chart (blue), −1…+1, zero line, right turn drawn upward
- events lane: TC (amber) and ABS (violet) 0/1 charts, plus live lights in the status bar
- a diagnostics line showing the current game and the raw steering value
- **Screen 1 at 30 Hz** (33 ms × 180 points ≈ 6 s) and **Screen 2 at 60 Hz** (16 ms × 360 points ≈ 6 s), to compare on the tablet

| Check | Question | Pass criterion |
| --- | --- | --- |
| S2 | Do overlaid `ChartItem`s render smoothly on the **web/tablet client** at 1920×1080? | 30 Hz screen looks continuous, with no stutter or blanking |
| S2b | What does it cost? | SimHub CPU increase < 5% with the tablet connected (Task Manager) |
| S4 | Raw steering for AC and ACC (`DataCorePlugin.GameRawData.Physics.SteerAngle`) | Value stays within −1…+1 at full lock, and the sign is known |
| S6a | Do `TCActive` / `ABSActive` fire in AC and ACC? | The lights and event lane react when TC/ABS intervene |

Results go in `docs/spikes.md`.

**If S2 passes:** the live window is done. Move on to the plugin core (Phase 1) and add turn bands and the full-lap trace later.
**If S2 fails:** test the plugin-rendered image approach (architecture §8, option A) next.

### Deferred spikes (needed for the full-lap trace)

| Spike | Question | Pass criterion |
| --- | --- | --- |
| S1 | Can an `ImageItem` show a plugin-published image that updates frequently on the web client? | ≥10 Hz, no flicker, CPU increase < 5% |
| S3 | If S1 fails: performance of generated column layers (≈1,200 bound rectangles) | ≥15 fps on the tablet |
| S5 | `TrackPositionPercent` scale and S/F behaviour; `TrackIdWithConfig` format | Documented |
| S6b | `TyreWear` meaning (used vs remaining) per sim | Recorded in the capability matrix |

## Phase 1: Plugin core (built 2026-10-02, awaiting in-sim test)

- [x] Project scaffold: Core (netstandard2.0) + net48 host + xunit tests; `-p:Deploy=true` copies into SimHub
- [x] Generic, AC, and ACC adapters
- [x] Frame normaliser with discontinuity detection and percent-scale detection
- [x] Lap trace (400 bins), lap store, session-best logic. ~~Rolling buffer~~ dropped: `ChartItem` keeps its own history (D11).
- [x] Turn catalogue (user file → curated → AC `sections.ini`), port of `TurnState`, turn recorder actions
- [x] Tyre model and provisional presets
- [x] Property publisher implementing contract v1
- [x] Unit tests: 60 passing
- [ ] In-sim checks in [test-plan.md](test-plan.md) (AC and ACC)

## Phase 2: Dashboard v1 (built 2026-10-03, awaiting tablet test)

Generated by [`tools/build_dash.py`](../tools/build_dash.py) into [`dash/TurnTelemetryDashboard`](../dash/TurnTelemetryDashboard/README.md). Shared helpers are in `tools/dashlib.py`, and `tools/preview_dash.py` renders an approximate PNG for layout review.

- [x] Carbon background, panel frame, top bar (session, lap, current/delta/best) and status bar (TC/ABS/BB, plugin info)
- [x] Focal tile (NEXT/CURRENT, amber frame on entry), last turn, gear and speed
- [x] Live window from the Phase 0 spike, now bound to `TurnTelemetry.Live.*`, plus a TURN lane (`Live.InTurn`). Steering, TC, and ABS rows hide when the sim can't report them.
- [x] **Lap strip** (new): whole-lap bar with turn bands from `Turn.Band.nn`, the current turn in amber, and a cursor at `Live.LapPos`. It brings the Turns app's turn highlighting to the whole lap without needing the full-lap trace renderer.
- [x] Tyre block: one colour per tyre until `Tyre.ZonesDetected`, then mirrored O/M/I stripes. Pressure coloured by state, wear bar when `Caps.TyreWear`, compound and preset.
- [x] Plugin-missing banner (`Contract` ≠ 1), unsupported-track notice, and "no lap position" state (Forza Horizon)
- [ ] Tablet test (see the dashboard README)
- [ ] Full-lap trace with ghost lap, after S1/S3 picks its renderer
- [ ] Pack tool → `TurnTelemetry.simhubdash`

## Phase 3: Data and tuning

**Turn data (2026-10-03).** Curated, with official numbers verified online and positions from SimHub maps:

| Sim | Curated tracks |
| --- | --- |
| ACC | Silverstone, Monza |
| AC | Silverstone GP, Monza, Spa, Imola, Nürburgring GP, Barcelona (2007–2020), Laguna Seca ×2, Mugello, COTA (from its numbered sections) |

The 9 circuit templates carry this numbering to the same circuits in any sim once SimHub has a map: ACC Spa, Imola, Nürburgring, Barcelona, Laguna Seca, COTA, and other sims. Not curated, because their numbering couldn't be confirmed against the geometry: **Red Bull Ring** (turn 8 not identifiable), **Zandvoort** (AC has the pre-2020 layout; guides number the current one), **Canada**, **Abu Dhabi**, **Le Mans**, **Vallelunga**, and **Magione**. These get the automatic base. ACC tracks without a SimHub map on this PC can't be checked until they've been driven once.


- Curated turn files for the AC and ACC tracks driven most often, created with the recorder
- Tyre presets checked for AC (road, semi-slick, slick) and ACC (GT3/GT4, dry and wet)
- In-sim test plan (`docs/test-plan.md`): S/F wrap, pit exit, reset to pits, invalid laps, unsupported track, plugin missing

## Phase 4: More sims

iRacing, LMU/rF2, AMS2, and RaceRoom adapters, plus a capability matrix in the README. Each sim needs live validation before it is marked supported.

**Forza:**
- **Horizon** (`FH6`, tested 2026-10-02) is open world. It reports no lap position, so it gets the live trace and tyres only; laps, turns, and the recorder are disabled. `ForzaHorizonAdapter` reads steering from `GameRawData.Steer`.
- **Motorsport** runs on circuits and should get the full feature set. Test it when available.
- **Backlog (2026-10-03):** run the Forza checklist F1–F11 in [test-plan.md](test-plan.md#forza-backlog-added-2026-10-03) for Horizon and Motorsport, then turn the findings into adapter changes and unit tests (as was done for ACC's line-crossing order), and record Forza in the README's sim-support table.

## Phase 5: Release

Installer zip (plugin DLL + data + `.simhubdash`), README install guide with screenshots, and versioned release notes in `docs/releases/`.

### Optional: standalone Live Trace dashboard

The Phase 0 spike ([`dash/LiveTraceSpike`](../dash/LiveTraceSpike/README.md)) is **kept as a reference and as a possible separate release**. It is a plugin-free dashboard page with only the live telemetry: pedals, steering, and TC/ABS. Keep its generator ([`tools/build_spike_dash.py`](../tools/build_spike_dash.py)) working, and don't fold it into the main dashboard's source. To publish it:

- rename it (e.g. "Live Trace") and drop the "SPIKE" caption and the diagnostics line
- keep only the 30 Hz screen, or offer 30/60 Hz as a setting
- add a steering expression for each sim beyond AC/ACC (or use `TurnTelemetry.Live.Steer` when the plugin is installed, falling back to raw values)
- package it as its own `.simhubdash` with a screenshot

## Open questions

1. Final product and plugin name. "Turn Telemetry" (property prefix `TurnTelemetry.`) is a placeholder.
2. Whether to accept community-contributed turn files through pull requests, and how to credit them.
3. Minimum supported SimHub version. Currently assumed to be the locally installed build.

## Roadmap

- **In-game overlays (planned):** offer the components (turns-this-lap strip, standings, inputs, tyres, car/conditions) as separate SimHub overlays with transparent backgrounds, for drivers who prefer them in-game rather than on a tablet. The generator already builds each panel from shared helpers, so an overlay build would emit one `.djson` per component with `PANEL` backgrounds made transparent and no background art.
