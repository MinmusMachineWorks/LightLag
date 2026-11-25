# In-game test harness

`LightLag.Tests` is a separate, dev-only KSP plugin that checks LightLag inside a running game. It's never part of the release zip.

## Build and install

```
dotnet build tests/LightLag.Tests/LightLag.Tests.csproj -c Release
```

- **Settings:** it uses the same `Directory.Build.props.user` as the mod.
- **Output:** the DLL goes to `tests/LightLag.Tests/GameData/LightLag.Tests/Plugins/` (gitignored).
- **Install:** when `KSPBT_GameRoot` is set, the build also copies it to `<KSP>/GameData/LightLag.Tests/Plugins/`. Delete that folder from the install to remove the harness.

The harness doesn't reference LightLag. It finds the mod through KSP's loaded assemblies and reads its `[LightLag]` log lines and game state. Rebuild the mod too when you change it, because the harness tests whichever LightLag DLL is installed.

## Running

Load a save and enter the Space Center. Results go to `KSP.log`, but KSP writes that file in batches, so new lines can take a while to appear. Unity's `Player.log` has them at once (macOS: `~/Library/Logs/Squad/KSP/Player.log`):

```
grep -E "\[LightLag( Test)?\] |Exception" ~/Library/Logs/Squad/KSP/Player.log | tail -40
```

The `( Test)?\] ` part skips stock lines about vessels whose names start with "LightLag".

```
[LightLag Test] PASS assembly loaded — 0.1.0.0
[LightLag Test] SUMMARY pass=3 fail=0
```

## Flight tests

In flight, click the harness's white toolbar button. It runs the tests below against the active vessel.

**Test vessel:**
- an uncrewed probe with a CommNet connection, a few thousand km or more from its control source (orbit is fine);
- no active engines (the throttle test moves the throttle);
- no autopilot driving the throttle or SAS;
- a probe core with prograde hold, for the SAS mode test. The harness turns SAS on for it and restores SAS and its mode afterwards.

The harness temporarily lowers the speed-of-light multiplier so that the delay is about 2 s, and restores it afterwards.

| Test | Checks |
|---|---|
| delay matches the control path | `DelayService.GetDelay` equals the sum of the `ControlPath` link lengths ÷ (c × multiplier), doubled with round trip |
| no phantom input when delay starts | after the harness turns delay off and on, nothing is counted in flight 3 physics frames later (don't touch the controls while it runs) |
| throttle arrives after the delay | after the harness sets the throttle input, `vessel.ctrlState.mainThrottle` changes only after the delay (± 3 physics frames) |
| non-player commands are instant | a direct `ActionGroups.SetGroup(SAS, true)`, outside any player entry point (as KSP or MechJeb would call it), takes effect in the same frame |
| SAS mode click arrives after the delay | invoking the navball's `VesselAutopilotUI.OnClickButton` changes `Autopilot.Mode` only after the delay (± 3 physics frames); Prograde, or StabilityAssist if another mode is set |
| part-menu button arrives after the delay | a retracted solar panel's or antenna's Extend, sent inside player context the way a part-menu click is, changes its deploy state only after the delay (± 3 physics frames); the harness retracts it again. Needs a retractable deployable part |
| science dialog Discard arrives after the delay | a rerunnable experiment with no data (thermometer, barometer…) is run at once, outside player context; pressing Discard on its results page removes the data only after the delay (± 3 physics frames). Needs one that can run where the vessel is; its own discard resets it |
| window button arrives after the delay | a robotics controller's Play/pause (the `LIGHTLAG_WINDOW` command `ModuleRoboticController.TogglePlay`), called inside player context the way the window's button is, changes whether it plays only after the delay (± 3 physics frames); the harness toggles it back. Needs Breaking Ground and a KAL controller on the vessel |
| queued command is saved | while that click is queued, `LightLagVessel`'s save holds a `COMMAND` with `type = SasMode` and the mode |

## Space Center tests

| Test | Checks |
|---|---|
| assembly loaded | `LightLag.dll` is in `AssemblyLoader.loadedAssemblies` |
| settings type present | `LightLag.LightLagParams` exists |
| settings registered in the current game | `CustomParams(LightLagParams)` returns the settings node |
