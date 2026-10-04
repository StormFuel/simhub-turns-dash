# Turn Telemetry Dashboard for SimHub

A SimHub plugin and 1920×1080 dashboard for a tablet or second screen. It shows your telemetry **turn by turn**:

- **Corners:** which corner you're at.
- **Sectors:** how each sector compares (purple, green, yellow), and where you exceeded track limits.
- **Standings:** best sectors, best laps, and gaps.
- **Inputs and tyres:** live throttle, brake and steering, plus colour-coded tyre temperature and pressure.

The corner tile carries on from [Assetto Corsa Turns](https://github.com/StormFuel/assetto-corsa-turns).

![Turn Telemetry Dashboard in ACC at Silverstone](docs/images/screenshot-dashboard.png)

## Download and install

New to GitHub or SimHub? Follow these steps in order. It takes about five minutes.

### What you need

- **A Windows PC** with your racing sim.
- **SimHub**, the free app this plugs into. If you don't have it, download it from [simhubdash.com](https://www.simhubdash.com/), install it, and start it once.
- **Somewhere to show the dashboard** (optional but recommended): a tablet or phone on the same Wi-Fi as your PC, or a second monitor. The dashboard is designed for a 1920×1080 landscape screen.

### 1. Download

1. Open the [**latest release**](https://github.com/StormFuel/simhub-turns-dash/releases/latest) page. You don't need a GitHub account.
2. Scroll down to **Assets**.
3. Click **`TurnTelemetry-v….zip`** (for example `TurnTelemetry-v0.2.4.zip`). Ignore the "Source code" files; those are for developers.

### 2. Unzip

1. Open your **Downloads** folder.
2. Right-click the zip and choose **Extract All…**, then **Extract**.
3. A folder opens containing `Install.cmd`, `Uninstall.cmd`, a `plugin` folder and a `READ ME FIRST.txt`.

Running Install.cmd from inside the zip without extracting it won't work.

### 3. Install

1. Double-click **`Install.cmd`**.
2. If Windows shows **"Windows protected your PC"**, click **More info**, then **Run anyway**. If it shows **"Open File – Security Warning"**, click **Run**. Windows warns about any small tool downloaded from the internet that isn't from a big publisher.
3. A black window opens. It finds SimHub and copies in the plugin and the dashboard.
   - If SimHub is running, it asks to close it. Type **Y** and press Enter.
   - If Windows asks for permission to make changes, click **Yes**. That's only needed when your SimHub folder is protected.
4. When it says **Done**, type **Y** and press Enter to start SimHub.

### 4. Turn on the plugin in SimHub

When SimHub starts, it asks whether to enable **Turn Telemetry**. Click **Yes**.

If you missed the prompt, go to SimHub's **Settings → Plugins**, tick **Turn Telemetry**, and restart SimHub. Afterwards, **Turn Telemetry** appears in SimHub's left menu.

### 5. Show the dashboard

**On a tablet or phone (recommended):**
1. Connect it to the same Wi-Fi as your PC.
2. Find your PC's local IP address: on the PC, open **Command Prompt** (search for "cmd" in the Start menu), type `ipconfig` and press Enter, then look for **IPv4 Address**, for example `192.168.1.20`.
3. In the tablet's browser, go to **`http://<that address>:8888`**, for example `http://192.168.1.20:8888`.
4. Pick **Turn Telemetry Dashboard**. Most browsers can add it to the home screen for full-screen use.
5. If Windows asks whether SimHub may use the network, click **Allow**.

**On a second monitor:** in SimHub open **Dash Studio**, find **Turn Telemetry Dashboard** in the list, and open it on that screen.

### 6. Drive

Start your sim and go out on track. The dashboard fills in as you drive:
- **Sectors** appear after your first lap on a new track.
- **Corner colours** start from your second lap.

[What everything on the screen means →](docs/user-guide.md#whats-on-the-screen)

### Updating

Download the newer zip from the [latest release](https://github.com/StormFuel/simhub-turns-dash/releases/latest) and run its **`Install.cmd`**. It replaces the old version. Your turn edits and learned sector lines are kept.

### Uninstalling

Run **`Uninstall.cmd`** from the zip folder. Your turn edits stay in `SimHub\PluginsData\TurnTelemetry`; delete that folder to remove them too.

### If something doesn't work

| What you see | What to do |
| --- | --- |
| The installer says **SimHub was not found** | Install SimHub first. If it's in an unusual folder, open Command Prompt in the unzipped folder and run `Install.cmd -SimHubDir "D:\Games\SimHub"` (with your SimHub folder) |
| **"TURN TELEMETRY PLUGIN NOT FOUND"** on the dashboard | The plugin isn't enabled. See step 4, then restart SimHub |
| The tablet can't open the page | Check both devices are on the same Wi-Fi, and use the PC's IPv4 address (not `localhost`). Allow SimHub through Windows Firewall if asked |
| Text looks odd or too small | Use a 1920×1080 landscape screen, or zoom the browser to fit |
| Anything else | In SimHub open **Turn Telemetry → Save problem report** and attach the zip to a [GitHub issue](https://github.com/StormFuel/simhub-turns-dash/issues) |

You can also [install by hand](docs/user-guide.md#install) without Install.cmd.

## Features

- **Turns this lap:** each corner as a band on a lap strip. The corner you're in is shown in neon blue, and the next one in the turn tile. Curated official numbering is included for some tracks, and the rest are auto-numbered from SimHub's track maps. Tap **SET LINE** at the start line, or use the turn editor, to fix numbering.
- **Sectors:** timed live and coloured purple (fastest of anyone), green (your best), yellow (slower) or red (track limits exceeded). Boundaries are learned the first time you cross them.
- **Corner deltas:** each corner turns green if you were as fast as or faster than your best through it, or yellow if slower, with the difference shown, the way race engineers compare corners.
- **Track limits per turn:** a red marker on the corner where you went off, and a session count.
- **RESET LAPS:** clears lap data after a manual reset (tap twice to confirm); restarts from the game menu are detected automatically.
- **Standings:** position, driver, best S1–S3, best lap, delta to the fastest, and gap to the leader. Your row is always shown.
- **Live inputs:** throttle, brake and steering over the last 6 s, plus TC/ABS intervention.
- **Tyres:** blue cold, green ideal, yellow above, red overheated, with pressures. Presets are per sim and compound.
- **Brakes:** a temperature bar beside each tyre that fills and changes colour as the brakes come up to temperature (blue < 200 °C, green to 650, yellow to 800, red).
- **Car and conditions:** RPM (red on the limiter), TC, TC cut, ABS, brake bias, engine map, flag, wipers, lights, and air and track temperature.
- **Multi-sim:** Assetto Corsa and ACC are tested. Other sims work as far as SimHub provides the data. See [what each sim supports](docs/user-guide.md#what-each-sim-supports).

### Known limits

- **ACC races:** ACC reports nothing about track limits in races, so the dashboard shows *TRACK LIMITS N/A IN RACE*. ACC practice/qualifying and AC work.
- **AC track limits follow each track's surface data:** some tracks (e.g. Laguna Seca) mark gravel and sand as valid track, so running wide there isn't detected.
- **TC cut, wipers, lights:** ACC only; other sims show N/A.
- **Sectors:** appear after you've crossed each sector line once on a new track.

## Plugin settings

**Turn Telemetry** in SimHub's left menu has these sections:

- **Settings:** pressure unit and tyre wear meaning.
- **Turn editor:** fix corner numbering and names.
- **Good to know:** what each sim supports.
- **Report a problem:** saves a zip to attach to an issue or Discord post; see [Reporting a problem](docs/user-guide.md#reporting-a-problem).
- **Live diagnostics:** what the plugin sees.

![Turn Telemetry plugin settings](docs/images/screenshot-plugin-settings.png)

## Roadmap

- **In-game overlays:** the same components (lap strip, standings, inputs, tyres) as separate overlays with transparent backgrounds, for drivers who want them on the game screen instead of a tablet.

## Documentation

- [docs/user-guide.md](docs/user-guide.md): install, screen guide, colours, sim support, troubleshooting
- [docs/architecture.md](docs/architecture.md): components, data flow, the plugin-to-dashboard property contract
- [docs/design.md](docs/design.md): layout, colour rules, typography, themes
- [docs/plan.md](docs/plan.md): decision record and roadmap
- [docs/test-plan.md](docs/test-plan.md): in-sim checks and findings

## Building from source

Requires the .NET SDK (8 or later), Python 3, and SimHub at the default path (`-p:SimHubDir=...` otherwise).

```powershell
dotnet test plugin/TurnTelemetry.slnx                                              # unit tests
dotnet build plugin/TurnTelemetry/TurnTelemetry.csproj -c Release -p:Deploy=true   # build + copy into SimHub (close SimHub first)
python tools/build_dash.py --install                                               # generate the dashboard + copy into SimHub
python tools/package_release.py                                                    # release zip + .simhubdash into dist/
```

The dashboard is generated by `tools/build_dash.py`; don't edit the `.djson` by hand. A plugin-free live-input dashboard ([dash/LiveTraceSpike](dash/LiveTraceSpike/README.md)) is kept as a possible standalone release.

Style note: the dashboard art is generated and inspired by action-sports liveries in general. It uses no third-party logos, names or marks.
