# LightLag

Light-speed signal delay and a command queue for KSP 1.12, built on CommNet. Commands you send an uncrewed probe arrive after the signal crosses its control path, the way RemoteTech's delay works, but without replacing CommNet.

- **Stock CommNet:** works on its own. Stock's connection rules, "Require Signal for Control", plasma blackout and probe control points (crewed pods that can fly probes) all still apply.
- **RealAntennas (recommended):** RealAntennas provides dishes, pointing and real signal strength. LightLag only adds the delay and the queue on top.

**Status: milestone M1, not yet checked in game.** Steering, translation, throttle, wheel and trim input to an uncrewed vessel are delayed. Staging, action groups, SAS buttons and part menus aren't delayed yet; they arrive in M2 and M5. See [docs/DESIGN.md](docs/DESIGN.md) for the plan and [docs/COVERAGE.md](docs/COVERAGE.md) for which inputs are covered.

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
- **Signal delay 2.31 s · 4 in flight:** the delay, and how many changes of input are still on their way.
- **No signal: commands can't be sent:** new input isn't sent. Input already sent still arrives; then the stick centers, and the throttle is cut or held per the setting.
- **Local control: no delay:** crew aboard, or an EVA kerbal.

The stock throttle gauge shows the throttle you commanded; the vessel follows it after the delay.

## Known limitations

- Input still in flight is lost on a scene change or reload.
- Discrete commands (staging, action groups, SAS and RCS, SAS modes, part menus) aren't delayed yet.

## Building

The project builds with [KSPBuildTools](https://github.com/KSPModdingLibs/KSPBuildTools):

```
dotnet build LightLag.sln -c Release
```

1. **Point the build at your game.** Create a gitignored `Directory.Build.props.user` that sets `KSPBT_GameRoot` to your KSP folder, and `KSPBT_ManagedPath` if needed. That install must have Harmony2 (`GameData/000_Harmony/0Harmony.dll`).
2. **Build.** `LightLag.dll` lands in `GameData/LightLag/Plugins/`, and when `KSPBT_GameRoot` is set, it's also copied into the install.
3. **Test.** The in-game test harness is in [tests/](tests/README.md).

**CI:** `.github/workflows/release.yml` builds every push. A `v*.*.*` tag also publishes a release and its CKAN entry through the shared Minmus Machine Works workflow. CKAN metadata lives in `ckan/metadata.json`.

## License

MIT. LightLag contains no code from RemoteTech (GPL-2.0) or RealAntennas (CC-BY-SA-4.0).
