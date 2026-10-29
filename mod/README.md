# MiG-29SMT for Nuclear Option (v0.2.1)

The modernised MiG-29SMT (9.19): an all-glass cockpit, the R-77 active radar missile, laser-guided Kh-29L and KAB-500L, the
Kh-31P anti-radiation missile and more internal fuel, on top of everything the MiG-29 Fulcrum mod's 9.12 has (flight model,
R-27R / R-27T / R-73 / R-60M, GSh-30-1, drop tanks, flares and chaff, airbrakes, brake chute, intake FOD doors, canopy and gear
doors, stock-style damage, rear-view mirrors). It is a separate jet from the MiG-29 Fulcrum mod and installs beside it.

## Install
1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx) into the Nuclear Option folder.
2. Install [Blueprinter](https://github.com/nikkorap/NOBlueprinter-Releases) (2.0.1 or newer) into `BepInEx/plugins`.
3. Unzip `MiG-29SMT-Mod_0.2.1.zip` into `BepInEx/plugins/MiG-29SMT/` (it holds `MiG-29SMT_0.2.1.nobp` and
   `MiG29SMTInstruments.dll`), or install the mod with a NOMNOM mod manager such as NOMM (listed as MiG-29SMT).
   Remove older `MiG-29SMT_*.nobp` files. The DLL draws the cockpit screens, gives the drop tanks their fuel and works the brake
   chute, intake doors, nozzles and gear path; without it the SMT still flies, with dark screens, empty drop tanks, no brake
   chute and the intake doors always open.
4. Optional: [NO Mirrors](https://github.com/IornMan1213/NuclearOption-Mirrors) makes the three rear-view mirrors reflect.

The SMT appears in medium hangars, shelters and revetments on land bases, in the Hyperion carrier's hangars, and in the
Encyclopedia. It is rank 3 and costs $90 million.
Multiplayer: everyone needs the same mod set (Blueprinter checks this).

## Features
- **Flight model** from the MiG-29 Fulcrum mod, tuned offline against published MiG-29 data: ~1,530 km/h at sea level, Mach 2.24
  at 13 km, ~28 deg/s instantaneous and ~19-20 deg/s sustained turn, 9 g limit, RD-33 engines (52 kN dry / ~90 kN wet in game).
  The SMT's dorsal spine holds an extra tank: **4,460 kg of internal fuel** (the 9.12 carries 3,690 kg). Radar size 0.15 clean
  (a little below the 9.12's 0.2, for the SMT's radar-absorbent treatment); stores add to it.
- **Cobra button**: **Ctrl+C** turns Stability Assist (the pitch, g and angle-of-attack limiters) off for a Cobra or another
  post-stall move. They come back on by themselves once the nose is down again or the jet is slower than 140 km/h, after 4
  seconds at most; press the key again to end it early. A short tone marks both. Key, time and tone are in Configuration
  Manager (MiG-29SMT Instruments > Flight), including a joystick button (`JoystickButton5` for any controller,
  `Joystick1Button5` for the first one). The plugin's keys do nothing while you type in chat.
- **Weapons**
  - R-77 (RVV-AE, AA-12 Adder): active radar, fire-and-forget once its own radar has the target, ~60 km. Lattice tail fins, on
    AKU-170 launchers.
  - R-27R (AA-10 Alamo-A): semi-active radar, ~50 km. Keep your radar locked on the target until impact.
  - R-27T (AA-10 Alamo-B): infrared, fire-and-forget, ~30 km. The seeker must lock before launch.
  - R-73 (AA-11 Archer): all-aspect infrared, thrust vectoring, 45 deg off-boresight.
  - R-60M (AA-8 Aphid): light, agile infrared missile, ~8 km.
  - Kh-29L (AS-14 Kedge): heavy laser-guided missile with a 320 kg warhead, ~12 km, for hardened and large targets. Lock the
    target (the SMT lases it, or a friendly designator does) and fire: it rides the laser spot.
  - KAB-500L: 500 kg laser-guided bomb, ~15 km, homing on the laser spot like the Kh-29L.
  - Kh-31P (AS-17 Krypton): ramjet anti-radiation missile, over Mach 3, ~100 km, for SAM and ship radars.
  - GSh-30-1: 30 mm cannon in the left wing root, 150 rounds at ~1,650 rpm. Fire short bursts.
  - Stations: inner pylons (R-77, R-27R, R-27T, R-73, R-60M, Kh-29L, Kh-31P, KAB-500L, PTB-1150 tank and the heavier base-game
    stores), middle pylons (R-77, R-73, R-60M, Kh-31P and the lighter base-game stores, single stores only), outer pylons (R-73,
    R-60M and small base-game stores), and a centreline station for the PTB-1500 tank, bombs, nuclear bombs, the gun pod and
    jamming pods.
  - Default loadout: gun, 4x R-77, 2x R-73. Preset loadouts (also used by AI SMTs): air superiority (R-77 / R-73), long-range CAP
    (PTB-1500 / R-77 / R-73), mixed (R-27R / R-77 / R-73), dogfight (R-27T / R-73 / R-60M), precision strike (Kh-29L / KAB-500L /
    R-73), SEAD (Kh-31P / R-77 / R-73), strike (FAB-500 / rockets / R-73) and ferry (three tanks / R-73).
  - Weapon modders: see `WEAPON_MODS.md` in the source repository to make your weapons selectable on the SMT.
- **Drop tanks**: PTB-1500 centreline tank (~1,180 kg of fuel) and PTB-1150 wing tanks (~905 kg each). Their fuel is used first,
  and the aircraft's weight follows the fuel. Drop them by selecting them and firing, or all at once with **Ctrl+J** (blocked with
  the gear down). AI SMTs drop theirs when empty.
- **Countermeasures** from the BVP-30-26M dispensers on the tail booms: 90 IR flares and 90 radar chaff cartridges, plus the
  Radar ECM jammer. Cycle countermeasures to choose. Chaff works best when the missile sees you side-on and is close. The **Wingtips** station takes a pod on each wing tip
  with 60 more: flares, chaff, or 30 of each, added to the jet's own while the pods are on. Optional separate **Flares** and **Chaff**
  keys (keyboard or HOTAS buttons, unbound by default) are in the Configuration Manager under Countermeasures.
- **Airbrakes** open with the throttle at idle; the **brake chute** streams after touchdown with the throttle at idle above
  ~110 km/h and is dropped below ~30 km/h or when you open the throttle; the **intake FOD doors** close on the ground below
  200 km/h with the engines running.
- **Damage like the stock jets**: soot, dents and scorched patches that spread, bullet holes where you are hit, and parts that
  break off cleanly with structure inside.
- **Liveries**: VKS Grey 26 and Dark Grey 52 (the SMT's own), plus the 9.12's Grey (plain, no markings), Desert Tan, Digital Grey, Display
  Blue, 29+20 Special, Tricolour Digital 741, OVT Splinter 917, White 44 and LII Gromov 84 (both factions can pick any).

## Cockpit
An all-glass panel with three large colour displays, each in a bezel. They are drawn by `MiG29SMTInstruments.dll`, only while
you look from the cockpit.

| Display | What it shows |
|---|---|
| Left: flight display | attitude with pitch ladder and bank scale, speed and altitude tapes, heading tape, Mach, g, angle of attack, vertical speed, radar altitude, gear and missile warnings |
| Centre: tactical display and stores | the game's tactical display across the top; below it every weapon station with rounds left (the selected one highlighted), countermeasures, and the current target with its range |
| Right: radar warning and systems | threats by bearing and type, inbound missiles and lock; your own signature (engine heat LOW / MED / HIGH, radar cross-section against clean, radar on or off); RPM and EGT for both engines, throttle, internal and drop-tank fuel, hydraulics, oxygen and the caution messages |

Between the screens are the gear lights and lever, a small standby flight display and the master caution. The up-front control
panel under the HUD shows the clock and the selected weapon, and its 16 keys light up with flight events: canopy, brakes, flaps,
gear, weapon armed, gun, radar lock, missile inbound, hydraulics, engine trouble, afterburner, intake doors, low fuel, empty drop
tanks, low flares or chaff, and low altitude.

**Cockpit setups:** layouts made in the cockpit editor (`.cpit` files) go in the `Cockpits` folder next to the DLL. Pick one in
Configuration Manager (MiG-29SMT Instruments > Cockpit > Cockpit layout) or cycle them with the "Next cockpit layout key". A file
that cannot be used falls back to the default cockpit, and the log says why.

At night the panel and consoles are lit (brightness: `BepInEx/config/iornman.mig29smt.instruments.cfg`, `Night panel lighting`,
0 = off; the same file has the drop-tank `Jettison key`, `Drop empty tanks automatically`, the `Hide flight HUD` option and the
mirror settings). **Ctrl+M** folds the mirrors.

## Troubleshooting
- **The SMT isn't in the aircraft list.** It spawns from medium hangars, shelters and revetments on land bases, and from the
  Hyperion carrier's hangars. Check that `BepInEx/LogOutput.log` has the line `Loaded mig29smt x.y.z`, and that only one
  `MiG-29SMT_*.nobp` is installed.
- **Can't join a multiplayer server.** Every player needs the same mod set and the same SMT version; Blueprinter compares them
  when you join.
- **The screens are dark, the drop tanks give no fuel, no brake chute.** These come from `MiG29SMTInstruments.dll`: it must be in
  `BepInEx/plugins` (next to the .nobp is fine). `BepInEx/LogOutput.log` then shows a line starting `MiG-29 instruments on`.
- **Kh-29L or KAB-500L miss.** They home on a laser spot: lock the target so the SMT lases it, and keep it locked until impact.
- **R-27R misses.** It's semi-active: keep the target locked on your radar until impact. The R-77 is fire-and-forget.
- **Something else.** [Open an issue](https://github.com/IornMan1213/MiG29SMT-NuclearOption/issues/new/choose) with your mod
  version and the `Exception` lines from `%USERPROFILE%\AppData\LocalLow\Shockfront\NuclearOption\Player.log`.

## Known limitations
- The flight model was tuned and verified offline; report handling issues with your controls and speed/altitude.
- Nuclear Option has no supersonic drag rise, so drag is tuned to hit the real top speeds; as a result subsonic climb and
  acceleration are somewhat below the real jet's.

## Links
- Source, releases and issues: https://github.com/IornMan1213/MiG29SMT-NuclearOption
- The MiG-29 Fulcrum (9.12) mod: https://github.com/IornMan1213/MiG29-NuclearOption

## Credits
- MiG-29 3D model: "MiG-29 - Fighter Jet - Free" by **bohmerang** (https://sketchfab.com/3d-models/mig-29-fighter-jet-free-0a21787096244220b246ec8747e7b09c),
  licensed CC BY-NC-SA 4.0 (https://creativecommons.org/licenses/by-nc-sa/4.0/). Converted and modified for this mod (including the
  SMT's dorsal spine); this mod file is distributed under the same licence: free, non-commercial, credit required.
- Cockpit, missile, bomb, launcher and drop-tank models, flight model, tooling: made for this mod.
- Built on the KR-67 Ifrit's systems from Nuclear Option (Shockfront), loaded with Blueprinter by nikkorap.
- Built with AI assistance (Claude).
