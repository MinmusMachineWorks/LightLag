# Input coverage

Every way a player (or another mod) can affect an uncrewed vessel, and how LightLag delays it.

Status: ✅ delayed, checked in game · 🧪 delayed in code, not yet checked in game · ⬜ not yet · ⚠️ partial · 🐞 broken · ➖ deliberately not delayed (reason given)

| Input | Entry point | Hook | Milestone | Status |
|---|---|---|---|---|
| Pitch, yaw, roll (keys, joystick, mouse) | `vessel.ctrlState`, first in `OnPreAutopilotUpdate` | `FlyByWireDelay` | M1 | 🧪 |
| Translation (RCS) | `vessel.ctrlState` | `FlyByWireDelay` | M1 | 🧪 |
| Throttle (keys, joystick, throttle cut/full) | `vessel.ctrlState.mainThrottle` | `FlyByWireDelay` | M1 | 🧪 (harness test) |
| Wheel steer and throttle | `vessel.ctrlState` | `FlyByWireDelay` | M1 | 🧪 |
| Trim | `vessel.ctrlState` | `FlyByWireDelay` | M1 | 🧪 |
| Staging (stage key) | `StageManager.ActivateNextStage`, from `FlightInputHandler.Update` | `CommandHooks` prefix → `StageCommand`. The Stage group toggle that follows in the same frame rides along | M2 | 🧪 |
| Action groups by key: custom 1–10, abort, gear, lights, brakes | `ActionGroupList.ToggleGroup` / `SetGroup`, from `FlightInputHandler.Update` | `CommandHooks` prefix → `ActionGroupCommand` | M2 | 🧪 |
| SAS and RCS toggles (T, R, F held) | `ActionGroupList.ToggleGroup` / `SetGroup` (SAS and RCS groups), from `FlightInputHandler.Update` | `CommandHooks` prefix → `ActionGroupCommand` | M2 | 🧪 |
| SAS, RCS, gear, lights and brakes buttons (top-left UI) | `ActionGroupList.SetGroup`, from `ActionGroupToggleButton.SetToggle` | `CommandHooks` prefix → `ActionGroupCommand` | M2 | 🧪 |
| SAS mode buttons on the navball | `VesselAutopilot.SetMode`, from `VesselAutopilotUI.OnClickButton` | `CommandHooks` prefix → `SasModeCommand` | M2 | 🧪 (harness test) |
| Part-menu buttons | `BaseEvent.Invoke` | Harmony prefix | M5 | ⬜ |
| Part-menu toggles, sliders and cycles | `BaseField` setters reached from part menus | Harmony | M5 | ⬜ |
| MechJeb autopilots | MechJebRTBridge seams | Bridge backend for `LightLagAPI` | M3 | ⬜ |
| kOS | kOS connectivity manager | Needs kOS-side support | later | ⬜ |
| Dish targeting (RealAntennas) | RA targeting window | Not decided | later | ⬜ |
| Staging, action groups and SAS modes called by KSP or other mods (SAS falling back to stability assist, SAS switching off, missions, MechJeb staging) | Same methods, outside the player entry points | ➖ `CommandHooks` act only inside `PlayerInput`'s entry points: these calls run on board. MechJeb's commands are the bridge's job (M3) | — | ➖ (harness test) |
| Stock SAS corrections | `OnAutopilotUpdate` | ➖ Runs after `FlyByWireDelay`, on board: SAS acts on the delayed input without its own delay | — | ➖ |
| Autopilots on `OnFlyByWire` (MechJeb's attitude control and others) | `OnFlyByWire` | ➖ Run after `FlyByWireDelay`, on board. Their *commands* are the bridge's job | — | ➖ |
| Science transmission | Stock / RA transmitters | ➖ Data flows *from* the vessel, so no command delay applies | — | ➖ |
| EVA kerbals, crewed pods with control | — | ➖ Local control: no delay | — | ➖ |
| Any input to a vessel with no signal, when stock's "Require Signal for Control" is off | `DelayService.Applies` returns false | ➖ Stock allows control without a signal, and there's no path to delay along | — | ➖ |
