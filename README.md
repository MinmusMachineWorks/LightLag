# LightLag

Light-speed signal delay and a command queue for KSP 1.12, built on CommNet. Commands you send an uncrewed probe arrive after the signal crosses its control path, the way RemoteTech's delay works, but without replacing CommNet.

- **Stock CommNet:** works on its own. Stock's connection rules, "Require Signal for Control", plasma blackout and probe control points (crewed pods that can fly probes) all still apply.
- **RealAntennas (recommended):** RealAntennas provides dishes, pointing and real signal strength. LightLag only adds the delay and the queue on top.

**Status: milestone M2, not yet checked in game.** Steering, translation, throttle, wheel and trim input to an uncrewed vessel are delayed. Staging, action groups, SAS and RCS toggles and SAS modes go through a command queue you can inspect and cancel from. Part menus aren't delayed yet; they arrive in M5. See [docs/DESIGN.md](docs/DESIGN.md) for the plan and [docs/COVERAGE.md](docs/COVERAGE.md) for which inputs are covered.

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
| Throttle on signal loss | Cut | Cut or hold an uncrewed vessel's throttle when it loses its signal |

## What you see in flight

A line under the stock time display shows the active vessel's state:
- **Signal delay 2.31 s · 4 in flight · 2 queued:** the delay, how many changes of stick and throttle input are still on their way, and how many commands are queued.
- **No signal: commands can't be sent:** new input isn't sent. Input already sent still arrives; then the stick centers, and the throttle is cut or held per the setting.
- **Local control: no delay:** crew aboard, or an EVA kerbal.

The stock throttle gauge shows the throttle you commanded; the vessel follows it after the delay. The SAS, RCS and gear buttons and the navball's SAS modes show the vessel's actual state, so they change when the command arrives.

## Command queue

Staging, action groups (custom 1–10, abort, gear, lights, brakes), SAS and RCS toggles and SAS modes don't act at once on a vessel with delay. They're queued and run when the signal arrives. With no signal they aren't sent at all.

Click the readout to open the **command queue** window for the active vessel:
- **Countdowns:** when each command arrives and when it runs.
- **Cancel:** a cancel is a signal too, so it's allowed only if it can reach the vessel before the command runs.
- **Extra delay:** new commands run at least this many seconds after you send them, even if the signal arrives sooner. Use it to time a command, for example staging at a moment you pick. It's saved per vessel.

On-rails time warp drops to 1× before a queued command runs; physics warp is left alone. The queue is saved with the vessel; commands that came due while a vessel was unloaded run when it loads.

Only your own commands are delayed. KSP's (SAS switching itself off) and other mods' (MechJeb staging) run on board without delay.

## Known limitations

- Stick and throttle input still in flight is lost on a scene change or reload. Queued commands are kept.
- Part-menu buttons and toggles aren't delayed yet.
- Staging works only on the active vessel, so a staging command that arrives after you switch vessels fails.
- The queue window doesn't block clicks to the scene behind it.

## Building

The project builds with [KSPBuildTools](https://github.com/KSPModdingLibs/KSPBuildTools):

```
dotnet build LightLag.sln -c Release
```

1. **Point the build at your game.** Create a gitignored `Directory.Build.props.user` that sets `KSPBT_GameRoot` to your KSP folder, and `KSPBT_ManagedPath` if needed. That install must have Harmony2 (`GameData/000_Harmony/0Harmony.dll`).
2. **Build.** `LightLag.dll` lands in `GameData/LightLag/Plugins/`. When `KSPBT_GameRoot` is set, the whole `GameData/LightLag/` folder (DLL, localization, `.version`) is also copied into the install.
3. **Test.** The in-game test harness is in [tests/](tests/README.md).

**CI:** `.github/workflows/release.yml` builds every push. A `v*.*.*` tag also publishes a release and its CKAN entry through the shared Minmus Machine Works workflow. CKAN metadata lives in `ckan/metadata.json`.

## License

MIT. LightLag contains no code from RemoteTech (GPL-2.0) or RealAntennas (CC-BY-SA-4.0).
