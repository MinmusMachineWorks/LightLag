# Input coverage

Every way a player (or another mod) can affect an uncrewed vessel, and how LightLag delays it.

Status: ✅ delayed · ⬜ not yet · ⚠️ partial · 🐞 broken · ➖ deliberately not delayed (reason given)

| Input | Entry point | Hook | Milestone | Status |
|---|---|---|---|---|
| Pitch, yaw, roll (keys, joystick, mouse) | `FlightCtrlState` via `Vessel.OnFlyByWire` | `FlyByWireDelay` | M1 | ⬜ |
| Translation (RCS) | `FlightCtrlState` | `FlyByWireDelay` | M1 | ⬜ |
| Throttle (keys, joystick) | `FlightCtrlState.mainThrottle` | `FlyByWireDelay` | M1 | ⬜ |
| Wheel steer and throttle | `FlightCtrlState` | `FlyByWireDelay` | M1 | ⬜ |
| Trim | `FlightCtrlState` | `FlyByWireDelay` | M1 | ⬜ |
| Staging (key and button) | `StageManager.ActivateNextStage` | Harmony prefix | M2 | ⬜ |
| Action groups, including abort, gear, lights and brakes | `ActionGroupList.ToggleGroup` | Harmony prefix | M2 | ⬜ |
| SAS and RCS toggles | `ActionGroupList.ToggleGroup` (SAS and RCS groups) | Harmony prefix | M2 | ⬜ |
| SAS mode buttons on the navball | `VesselAutopilot.SetMode` | Harmony prefix | M2 | ⬜ |
| Part-menu buttons | `BaseEvent.Invoke` | Harmony prefix | M5 | ⬜ |
| Part-menu toggles, sliders and cycles | `BaseField` setters reached from part menus | Harmony | M5 | ⬜ |
| MechJeb autopilots | MechJebRTBridge seams | Bridge backend for `LightLagAPI` | M3 | ⬜ |
| kOS | kOS connectivity manager | Needs kOS-side support | later | ⬜ |
| Dish targeting (RealAntennas) | RA targeting window | Not decided | later | ⬜ |
| Science transmission | Stock / RA transmitters | ➖ Data flows *from* the vessel, so no command delay applies | — | ➖ |
| EVA kerbals, crewed pods with control | — | ➖ Local control: no delay | — | ➖ |
