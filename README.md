# MiG-29SMT for Nuclear Option

A flyable MiG-29SMT (9.19), the modernised Fulcrum, for [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/).
It is a separate jet from the [MiG-29 Fulcrum mod](https://github.com/IornMan1213/MiG29-NuclearOption) (the 9.12) and installs
beside it. It has an all-glass cockpit, the R-77 active radar missile, laser-guided Kh-29L and KAB-500L strike weapons, the
Kh-31P anti-radiation missile, more fuel in the SMT's dorsal spine, and everything the 9.12 has: its own flight model, R-27R /
R-27T / R-73 / R-60M, the GSh-30-1 cannon, drop tanks, flares and chaff, airbrakes, a brake chute, intake FOD doors, stock-style
damage and rear-view mirrors that reflect with the [NO Mirrors](https://github.com/IornMan1213/NuclearOption-Mirrors) plugin.

[![Latest release](https://img.shields.io/github/v/release/IornMan1213/MiG29SMT-NuclearOption?include_prereleases&label=release)](https://github.com/IornMan1213/MiG29SMT-NuclearOption/releases)
[![Licence: code MIT, model CC BY-NC-SA 4.0](https://img.shields.io/badge/licence-MIT%20%2B%20CC%20BY--NC--SA%204.0-blue)](#licence)
[![Nuclear Option + Blueprinter](https://img.shields.io/badge/Nuclear%20Option-Blueprinter%202.1.1%2B-red)](https://github.com/nikkorap/NOBlueprinter-Releases)

**[Releases](https://github.com/IornMan1213/MiG29SMT-NuclearOption/releases) · [Report an issue](https://github.com/IornMan1213/MiG29SMT-NuclearOption/issues/new/choose) · [MiG-29 Fulcrum mod](https://github.com/IornMan1213/MiG29-NuclearOption)**

## Install (players)
1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx) into the Nuclear Option folder.
2. Install [Blueprinter](https://github.com/nikkorap/NOBlueprinter-Releases) 2.1.1 or newer into `BepInEx/plugins`.
3. Install it with a mod manager that uses [NOMNOM](https://github.com/KopterBuzz/NOMNOM) (e.g. NOMM; it is listed as
   **MiG-29SMT**), or by hand: download `MiG-29SMT-Mod_x.y.z.zip` from the newest
   [release](https://github.com/IornMan1213/MiG29SMT-NuclearOption/releases) and put the files from it in
   `BepInEx/plugins/MiG-29SMT/`. Delete any older `MiG-29SMT_*.nobp` first. `MiG29SMTInstruments.dll` (in the zip) draws the
   cockpit screens, feeds the drop tanks and works the brake chute, intake doors, nozzles and gear path.
4. Optional: [NO Mirrors](https://github.com/IornMan1213/NuclearOption-Mirrors) makes the three rear-view mirrors reflect.

The SMT and the MiG-29 Fulcrum mod can be installed together: each has its own aircraft, weapons, plugin and folder.
Multiplayer: everyone needs the same mod set (Blueprinter checks this).

See [mod/README.md](mod/README.md) for the full feature list, the cockpit screens and troubleshooting.

**Weapon modders:** [WEAPON_MODS.md](WEAPON_MODS.md) has the SMT's aircraft key, hardpoint sets and pylon clearances.

## What makes it an SMT
- **All-glass cockpit**: three large colour displays (flight display; tactical display with stores; radar warning, signature,
  engines and systems), an up-front control panel whose keys light up with flight events, and standby instruments.
- **Strike role**: Kh-29L and KAB-500L home on the laser spot of a target the SMT (or a friendly designator) lases; the Kh-31P
  homes on radars at over Mach 3. Preset loadouts for precision strike and SEAD.
- **R-77 (RVV-AE)** active radar missile on AKU-170 launchers; the default loadout is four R-77 and two R-73.
- **The dorsal spine** behind the canopy holds the extra tank: 4,460 kg of internal fuel (the 9.12 carries 3,690 kg).
- Radar size 0.15 clean (the 9.12's 0.2, less for the SMT's radar-absorbent treatment), rank 3, $90 M.
- Eleven liveries, including the SMT's VKS Grey 26 and Dark Grey 52.

## Repository layout
| Path | What it is |
|---|---|
| `unity/MiG29Tools/` | Unity editor scripts that build the mod inside the [Blueprinter Editor](https://github.com/nikkorap) project: they clone the KR-67 Ifrit, swap in the MiG model (with the SMT's dorsal spine, `tools/smt_hump.py`), split it into the game's damage parts, and set up the flight model, weapons, canopy, gear doors, displays, liveries and loading screens |
| `unity/MiG29Instruments/` | The BepInEx plugin that drives the cockpit instruments from the live aircraft (`dotnet build`, against the installed game; `MiG29Tools/MiG29InstrumentMath.cs` is shared with the editor preview); it also draws the glass-cockpit screens (`Pfd.cs`, `Widgets.cs`) |
| `tools/` | The headless build (`build_mig29.ps1`), the offline flight-model simulator (`fm_sim.py` + `fm_config.json`), the procedural missile, launcher and drop-tank generator (`missile_gen.py`), and the cockpit texture generator (`cockpit_atlas.py`: gauge faces, switch panels, caution lights) |
| `blender/` | The model export (`export_mig29.py`), the procedural cockpit (`cockpit_build.py`, fitted to the airframe), the loading-screen and showcase renders, and the measurement scripts used while fitting parts |
| `assets/loading/` | Loading screens (model renders over game terrain) |
| `cockpit/` | The cockpit editor's layout, decals, screens and MFD pages, applied by the build |
| `docs/` | The GitHub Pages project site |
| `CHANGELOG.md` | What changed in each version |
| `DEVLOG.md` | The development journal, including the game internals the build relies on |

## Building from source
The Sketchfab model and the game's assemblies are **not** in this repo. You need:

- Nuclear Option, Unity 2022.3.62f2, the Blueprinter Editor project (set up with the game's assemblies, as its own instructions describe), Blender 4.2 or newer, and Python 3 with `numpy` and `Pillow`.
- The [MiG-29 model by bohmerang](https://sketchfab.com/3d-models/mig-29-fighter-jet-free-0a21787096244220b246ec8747e7b09c) (free download).

Steps:
1. Export the model into the project:
   `blender -b MiG-29.blend --python blender/export_mig29.py -- <project>/MiG29SMTSource`
2. Prepare the textures (2K set, scorch texture, loading screens):
   `python tools/prepare_textures.py <sketchfab>/textures <project>/MiG29SMTSource`
3. With Unity Hub running and signed in, run the build from PowerShell:
   `./tools/build_mig29.ps1 -Project <project>`. It copies `unity/MiG29Tools` into the project, generates the missiles, the
   flight-model export, the livery textures and the cockpit (atlas, then Blender), writes the `.nobp` to
   `<project>/MiG29SMTBuild/`, and builds `MiG29SMTInstruments.dll` next to it (needs the .NET SDK and the game installed; pass
   `-p:GameDir=...` via the csproj if the game is elsewhere).
4. `-Release -Note "..."` also backs the build up to a `builds` branch (`tools/backup_build.py`); `python tools/gh_release.py <version>`
   publishes the GitHub release with the zip.

**On Linux:** run `tools/setup_linux.sh [<project>]` once (apt, dnf or pacman). It installs Python with numpy and Pillow, the
.NET 8 SDK, PowerShell, Blender and Unity Hub, adds `~/.local/bin`, `GameDir` and `BLUEPRINTER_PROJECT` to `~/.profile`, and
lists the remaining manual steps (Unity 2022.3.62f2 through the Hub with its Windows Build Support (Mono) module, the Steam launch option
`WINEDLLOVERRIDES="winhttp=n,b" %command%` so BepInEx loads under Proton). Then build with
`pwsh tools/build_mig29.ps1 -Project <project>`. Without the Windows fonts, the texture scripts fall back to Liberation Sans
and DejaVu Sans Condensed. To get byte-identical textures, copy `arial.ttf`, `arialbd.ttf` and `bahnschrift.ttf` into
`~/.local/share/fonts`.

`python tools/cockpit_atlas.py <project>/MiG29SMTSource` and
`blender -b --python blender/cockpit_build.py -- <project>/MiG29SMTSource <preview dir>` rebuild just the cockpit and render
previews. `MIG29_PRETTY=1 MIG29_POSE=sample` with `MiG29Tools.MiG29Preview.RenderCockpit` (via `tools/unity_run.ps1 -Graphics`)
renders the built cockpit from the pilot's eye with the instruments posed for a sample flight state.

To check flight-model changes offline, run `python tools/fm_sim.py`. It reads `tools/fm_config.json` and the part data
dumped from the built prefab (`tools/aero_reference.json`, refreshed with `MiG29Tools.MiG29AeroDump.Dump`).

## Licence
- **Source code** in this repository: [MIT](LICENSE).
- **The released `.nobp`** contains a converted and modified version of
  ["MiG-29 - Fighter Jet - Free"](https://sketchfab.com/3d-models/mig-29-fighter-jet-free-0a21787096244220b246ec8747e7b09c) by
  **bohmerang**, licensed [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/). The release file is distributed under
  the same licence: it is free, non-commercial, and requires credit. Renders of the model in `docs/img/` and `assets/` are also covered by CC BY-NC-SA 4.0. See [LICENSE-MODEL.md](LICENSE-MODEL.md).
- Nuclear Option is by Shockfront. This is an unofficial fan mod with no affiliation to Shockfront. It uses the game's own
  KR-67 systems at runtime through Blueprinter and does not redistribute any game files.
- This mod was made with the assistance of AI (Anthropic's Claude), which helped write the code, tools and documentation.
