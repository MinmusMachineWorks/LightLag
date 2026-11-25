# LightLag

Light-speed signal delay and a command queue for KSP 1.12, built on CommNet. Commands you send an uncrewed probe arrive after the signal crosses its control path, the way RemoteTech's delay works, but without replacing CommNet.

- **Stock CommNet:** works on its own. Stock's connection rules, "Require Signal for Control", plasma blackout and probe control points (crewed pods that can fly probes) all still apply.
- **RealAntennas (recommended):** RealAntennas provides dishes, pointing and real signal strength. LightLag only adds the delay and the queue on top.

**Status: milestone M0.** The plugin loads and its settings appear in the difficulty screen, but nothing is delayed yet. See [docs/DESIGN.md](docs/DESIGN.md) for the plan and [docs/COVERAGE.md](docs/COVERAGE.md) for which inputs are covered.

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

None of them has an effect until M1.

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
