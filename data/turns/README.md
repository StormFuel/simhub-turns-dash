# Curated turn data

Don't edit these files by hand. Each one is generated from a spec in [`data/curation`](../curation) by `python tools/curate_turns.py build`, which also records the sources. The method is described in [docs/architecture.md §6](../../docs/architecture.md#6-turn-catalogue-and-tracker-turns). Circuit templates in [`data/circuits`](../circuits) are generated from these files by `python tools/circuits.py build`.


One JSON file per sim and track: `turns/<GameName>/<TrackIdWithConfig>.json`. The names are the values SimHub reports, sanitised for file names. The plugin settings tab shows both for the current session.

These files are compiled into the plugin. User files with the same path under `SimHub/PluginsData/TurnTelemetry/` take precedence.

To add a track:

1. `python tools/curate_turns.py report <GameName> <track>` lists the corners detected on SimHub's map, with AC section names where they exist.
2. Look up the official numbering. Write `data/curation/<GameName>/<track>.json`, tying each official turn to a detected corner or an explicit range. Use `probe` to find flat-out kinks and `dips` to find where to split merged corners.
3. `python tools/curate_turns.py build` validates the spec (lap order, overlaps, directions) and writes the file here.
4. If the track is a reference circuit, add it to `REFERENCES` in `tools/circuits.py` and run `python tools/circuits.py build validate`.
