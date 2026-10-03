"""Build the release packages into dist/.

    python tools/package_release.py

Builds the plugin (Release) and the Turn Telemetry Dashboard, then writes:
  dist/TurnTelemetry-v<version>.zip            plugin + dashboard + one-click installer (Install.cmd)
  dist/TurnTelemetryDashboard-v<version>.simhubdash   dashboard only (double-click to import into SimHub)
The version comes from plugin/Directory.Build.props.
"""
import re
import shutil
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DIST = ROOT / 'dist'
DASH = 'TurnTelemetryDashboard'
PLUGIN_OUT = ROOT / 'plugin' / 'TurnTelemetry' / 'bin' / 'Release' / 'net48'
PLUGIN_FILES = ['TurnTelemetry.dll', 'TurnTelemetry.Core.dll']

READ_ME = """Turn Telemetry {version} for SimHub
=====================================

INSTALL
  1. Unzip this folder anywhere.
  2. Double-click Install.cmd. It finds SimHub, waits for SimHub to close, and copies in
     the plugin and the "Turn Telemetry Dashboard". (Windows may ask for administrator
     rights if your SimHub folder needs them.)
  3. Start SimHub and answer Yes when it asks to enable "Turn Telemetry".
  4. Show "Turn Telemetry Dashboard" on your tablet or second screen
     (on a tablet: open http://<your-pc-ip>:8888 and pick it).

REMOVE
  Double-click Uninstall.cmd. Your turn edits and learned sectors stay in
  SimHub\\PluginsData\\TurnTelemetry.

MANUAL INSTALL (instead of Install.cmd)
  - Close SimHub. Copy plugin\\TurnTelemetry.dll and plugin\\TurnTelemetry.Core.dll into the
    SimHub folder (right-click each > Properties > Unblock first if Windows marked them).
  - Double-click TurnTelemetryDashboard.simhubdash to import the dashboard.

Guide, screenshots and known limits: https://github.com/StormFuel/simhub-turns-dash
"""


def version():
    props = (ROOT / 'plugin' / 'Directory.Build.props').read_text(encoding='utf-8')
    return re.search(r'<Version>([^<]+)</Version>', props).group(1)


def simhubdash(path):
    """A .simhubdash is a zip holding the dashboard folder."""
    src = ROOT / 'dash' / DASH
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        for f in sorted(src.glob(f'{DASH}.djson*')):
            z.write(f, f'{DASH}/{f.name}')


def main():
    v = version()
    subprocess.run(['dotnet', 'test', str(ROOT / 'plugin' / 'TurnTelemetry.slnx')], check=True)
    subprocess.run(['dotnet', 'build', str(ROOT / 'plugin' / 'TurnTelemetry' / 'TurnTelemetry.csproj'), '-c', 'Release'],
                   check=True)
    subprocess.run(['python', str(ROOT / 'tools' / 'build_dash.py')], check=True)

    DIST.mkdir(exist_ok=True)
    stage = DIST / f'TurnTelemetry-v{v}'
    if stage.exists():
        shutil.rmtree(stage)
    (stage / 'plugin').mkdir(parents=True)
    for f in PLUGIN_FILES:
        shutil.copy2(PLUGIN_OUT / f, stage / 'plugin' / f)
    for f in ['Install.cmd', 'Uninstall.cmd', 'Install.ps1']:
        shutil.copy2(ROOT / 'installer' / f, stage / f)
    simhubdash(stage / f'{DASH}.simhubdash')
    (stage / 'READ ME FIRST.txt').write_text(READ_ME.format(version=v).replace('\n', '\r\n'), encoding='utf-8')

    package = DIST / f'TurnTelemetry-v{v}.zip'
    with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED) as z:
        for f in sorted(stage.rglob('*')):
            if f.is_file():
                z.write(f, f'TurnTelemetry-v{v}/{f.relative_to(stage).as_posix()}')
    simhubdash(DIST / f'{DASH}-v{v}.simhubdash')
    print('wrote', package)
    print('wrote', DIST / f'{DASH}-v{v}.simhubdash')


if __name__ == '__main__':
    main()
