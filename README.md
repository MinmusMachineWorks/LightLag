# LightLag

Light-speed signal delay and a command queue for KSP 1.12, built on CommNet. Commands you send an uncrewed probe arrive after the signal crosses its control path, the way RemoteTech's delay works, but without replacing CommNet.

- **Stock CommNet:** works on its own. Stock's connection rules, "Require Signal for Control", plasma blackout and probe control points (crewed pods that can fly probes) all still apply.
- **RealAntennas (recommended):** RealAntennas provides dishes, pointing and real signal strength. LightLag only adds the delay and the queue on top.

**Status: milestone M3, partly checked in game.** Steering, translation, throttle, wheel and trim input to an uncrewed vessel are delayed. Staging, action groups, SAS and RCS toggles, SAS modes and part-menu buttons and controls go through a command queue you can inspect and cancel from. See [docs/DESIGN.md](docs/DESIGN.md) for the plan and [docs/COVERAGE.md](docs/COVERAGE.md) for which inputs are covered.

## Install

Install through CKAN as `LightLag`, after adding the [Minmus Machine Works repository](https://github.com/MinmusMachineWorks/KSP-Mods-CKAN).

- **Requires:** Harmony2.
- **Conflicts with:** RemoteTech and SignalDelay, which delay commands themselves.

## Settings

The settings are under **Difficulty → Advanced**, next to stock CommNet's:

| Setting | Default | Meaning |
|---|---|---|
| Light-speed delay | On | Delay commands to uncrewed vessels |
| Round-trip delay | Off | Commands take effect after the signal reaches the vessel and comes back |
| Speed of light multiplier | 1.00 | Above 1 shortens every delay; below 1 lengthens it |
| Throttle on signal loss | Cut | Cut or hold an uncrewed vessel's throttle when it loses its signal. Only with stock's "Require Signal for Control" on |

## What you see in flight

The delay sits on the stock signal bar at the top left, just right of CommNet's signal icons, in flight and in map view. It shows a flight computer button and the active vessel's state; hover it for the details, and click it to open the **flight computer**:
- **2.31 s** (or **2.31 s · 2 queued**): the delay, and how many commands are queued. The tooltip also says whether stick and throttle input you gave is still on its way.
- **No signal** (in red): stock's "Require Signal for Control" is on, so stock locks every control, and LightLag sends nothing new. Input already sent still arrives; then the stick centers, and the throttle is cut or held per the setting.
- **No delay** (no signal, none required): "Require Signal for Control" is off, so stock gives the vessel partial control without a signal, and there's no path to delay along. What stock allows (staging, action groups, SAS and RCS) acts at once; stock still locks the stick, throttle and translation.
- **Local:** crew aboard, or an EVA kerbal. No delay.

The bar entry is hidden when light-speed delay is off or CommNet isn't running.

The stock throttle gauge shows the throttle you commanded; the vessel follows it after the delay. The SAS, RCS and gear buttons and the navball's SAS modes show the vessel's actual state, so they change when the command arrives.

## Command queue

Staging, action groups (custom 1–10, abort, gear, lights, brakes), SAS and RCS toggles, SAS modes and the active vessel's part menus (buttons such as Extend or Deploy, and toggles, sliders and other controls) don't act at once on a vessel with delay. They're queued and run when the signal arrives. With no signal they aren't sent at all, unless "Require Signal for Control" is off; then they act at once. Without a signal, stock gives an uncrewed probe no control at all ("No Telemetry"), so with "Require Signal for Control" on, every key simply does nothing before LightLag sees it.

The **flight computer** (click the delay on the signal bar) shows the active vessel's signal and queue:
- **Signal:** the delay, and the control path hop by hop: each relay or station, how far, and its share of the delay.
- **Stick and throttle:** how long until the last of your stick and throttle input arrives. It can't be cancelled.
- **Countdowns:** when each command arrives and when it runs.
- **Cancel:** a cancel is a signal too, so it's allowed only if it can reach the vessel before the command runs.
- **Extra delay:** new commands run at least this many seconds after you send them, even if the signal arrives sooner. Use it to time a command, for example staging at a moment you pick. It's saved per vessel.

On-rails time warp drops to 1× before a queued command runs; physics warp is left alone. The queue is saved with the vessel; commands that came due while a vessel was unloaded run when it loads.

Only your own commands are delayed. KSP's (SAS switching itself off) and other mods' (MechJeb staging) run on board without delay.

**Part menus:**
- **Instant:** buttons and controls that don't command the vessel: Control From Here and the camera, Review Data, KerbNet and other windows, renaming and targets, plus mod buttons that only open a window. [docs/PART_MENUS.md](docs/PART_MENUS.md) has the full analysis; the list is in `GameData/LightLag/Rules/`, with `Stock.cfg` and one file per mod in the set. Another mod can add its own `LIGHTLAG_EXEMPTIONS` node in any `.cfg` (see `Stock.cfg`). A mod can also mark an event or field with RemoteTech's `skip_delay` (needs a signal, no delay) or `skip_control` (no checks) category.
- **Other vessels' parts** (an EVA kerbal working on a part, setting a target) aren't delayed.
- **Symmetry:** clicking a button on a part with symmetry queues one command for all its counterparts.
- **Science results (Review Data):** Transmit, Discard, Reset and Process in lab are queued like any command, and the data stays on board until the command arrives, as if you'd pressed Keep. Keep acts at once. This works for mod experiments that use the stock dialog too.
- **Windows opened from a part menu:** their buttons that command the vessel are delayed too: the robotics controller's playback, KAS's winch remote, Konstruction's build buttons, MKS Orbital Logistics' start, abort and resume, Extraplanetary Launchpads' build, pause, cancel, finish and release, and RealAntennas' dish targeting. Each mod's file in `GameData/LightLag/Rules/` lists them (`LIGHTLAG_WINDOW`); another mod can add its own.
- **Sliders:** a drag is sent as one change, and the slider shows the value on its way. Toggles and other controls show the part's actual state until the command arrives.

## Known limitations

- Stick and throttle input still in flight is lost on a scene change or reload. Queued commands are kept.
- Resource transfers aren't delayed: part-menu resource flow and priority toggles, crew-free resource transfer, and KAS's fuel transfer window.
- Editing a robotics controller's sequence (keyframes, length) isn't delayed; its playback buttons are.
- Window commands that aren't on a part module (EL's build controls, MKS transfers, RealAntennas targeting, Konstruction's Dekonstructor and KonFabricator) are lost on a reload or scene change. The flight computer marks them "(not saved)".
- Staging works only on the active vessel, so a staging command that arrives after you switch vessels fails.
- The flight computer window doesn't block clicks to the scene behind it.

## Building

The project builds with [KSPBuildTools](https://github.com/KSPModdingLibs/KSPBuildTools):

```
dotnet build LightLag.sln -c Release
```

1. **Point the build at your game.** Create a gitignored `Directory.Build.props.user` that sets `KSPBT_GameRoot` to your KSP folder, and `KSPBT_ManagedPath` if needed. That install must have Harmony2 (`GameData/000_Harmony/0Harmony.dll`).
2. **Build.** `LightLag.dll` lands in `GameData/LightLag/Plugins/`. When `KSPBT_GameRoot` is set, the whole `GameData/LightLag/` folder (DLL, localization, `.version`) is also copied into the install.
3. **Test.** The in-game test harness is in [tests/](tests/README.md).

**Dependencies through CKAN.** A dev build isn't a CKAN install, so CKAN won't install its dependencies by itself; it only sees `LightLag.dll` as an unmanaged mod. A dev metapackage fixes that:

```
python3 tools/dev_ckan.py
```

This writes `ckan/LightLag-dev.ckan` (gitignored). It carries LightLag's relations from `ckan/metadata.json` (requires Harmony2, recommends RealAntennas, conflicts with RemoteTech and SignalDelay) and installs no files. Install it once in the CKAN GUI with **File → Install from .ckan…**. CKAN then installs and keeps the dependencies, offers the recommendations, and refuses conflicting mods, while each build copies the latest LightLag in.

- **From the command line:** with the CKAN GUI closed, `python3 tools/dev_ckan.py --gamedir "<KSP folder>"` also installs it. Recommended mods aren't pulled in this way.
- **After `ckan/metadata.json` changes:** run the script again and install the new file. Its version only goes up when the metadata changes.

**CI:** `.github/workflows/release.yml` builds every push. A `v*.*.*` tag also publishes a release and its CKAN entry through the shared Minmus Machine Works workflow. CKAN metadata lives in `ckan/metadata.json`.

## License

MIT. LightLag contains no code from RemoteTech (GPL-2.0) or RealAntennas (CC-BY-SA-4.0).
