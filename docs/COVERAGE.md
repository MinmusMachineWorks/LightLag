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
| Staging (key and button) | `StageManager.ActivateNextStage` | Harmony prefix | M2 | ⬜ |
| Action groups, including abort, gear, lights and brakes | `ActionGroupList.ToggleGroup` | Harmony prefix | M2 | ⬜ |
| SAS and RCS toggles | `ActionGroupList.ToggleGroup` (SAS and RCS groups) | Harmony prefix | M2 | ⬜ |
| SAS mode buttons on the navball | `VesselAutopilot.SetMode` | Harmony prefix | M2 | ⬜ |
| Part-menu buttons | `BaseEvent.Invoke` | Harmony prefix | M5 | ⬜ |
| Part-menu toggles, sliders and cycles | `BaseField` setters reached from part menus | Harmony | M5 | ⬜ |
| MechJeb autopilots | MechJebRTBridge seams | Bridge backend for `LightLagAPI` | M3 | ⬜ |
| kOS | kOS connectivity manager | Needs kOS-side support | later | ⬜ |
| Dish targeting (RealAntennas) | RA targeting window | Not decided | later | ⬜ |
| Stock SAS corrections | `OnAutopilotUpdate` | ➖ Runs after `FlyByWireDelay`, on board: SAS acts on the delayed input without its own delay | — | ➖ |
| Autopilots on `OnFlyByWire` (MechJeb's attitude control and others) | `OnFlyByWire` | ➖ Run after `FlyByWireDelay`, on board. Their *commands* are the bridge's job | — | ➖ |
| Science transmission | Stock / RA transmitters | ➖ Data flows *from* the vessel, so no command delay applies | — | ➖ |
| EVA kerbals, crewed pods with control | — | ➖ Local control: no delay | — | ➖ |
