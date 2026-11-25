# Part menus: what's delayed

Every button (`KSPEvent`) and control (`KSPField` with a flight UI control) in the active vessel's part menus is delayed, except the ones below. This is LightLag's own analysis of stock KSP 1.12.5 and the mod set, done on 2026-11-25. `GameData/LightLag/Exemptions/` implements it, with `Stock.cfg` and one file per mod; keep the two in sync (AGENTS.md, Rule #1).

## The test

**Does it command the vessel?** That means changing what the craft physically does, its resources, or the state of its parts. If it does, it's delayed: the order has to reach the craft. If it only changes your view, a window, a name, or what you're looking at, it's instant.

## Instant

| Group | Stock | Mod set | Why |
|---|---|---|---|
| Your viewpoint | Control From Here (`ModuleCommand.MakeReference`, `KerbalSeat`/`ModuleDockingNode`/`ModuleGrappleNode.MakeReferenceTransform`), `ModuleCommand.ChangeControlPoint`, `Part.AimCamera`, `Part.ResetCamera` | EL `ELControlReference.MakeReference` | Picks the reference you fly and view from. The navball and SAS switch at once |
| Windows and readouts | Review Data (`ModuleScienceExperiment`, `ModuleScienceContainer`), `ModuleKerbNetAccess.OpenKerbNet`, `ModuleRoboticController.OpenControllerEditor`, `ModuleFlightDisplay.ToggleGui`, `ModuleOrbitalScanner` overlay, color, resource and cutoff | SCANsat `reviewEvent`; Firespitter info, help and trim popups, launch-position logging; KAS winch and fuel GUIs; Konstruction's Konstructor, Dekonstructor and Konfabricator; MKS Orbital Logistics; EL Show/Hide/Rename UI; RealAntennas planning, targeting and debug | Opens or adjusts a display |
| Names, targets and tallies | Rename Vessel, Rename Asteroid, Rename Comet, `ModuleGroundExpControl.SetVesselNaming`, Target Center of Mass (asteroids, comets) | MKS `ModuleColonyRewards.CheckRewards` | Bookkeeping, not the craft |
| Cosmetic | `ModuleServiceModule.openCutaway` (field) | — | Visual only |

A mod can also mark an event or field with RemoteTech's `category = "skip_control"` (instant) or `"skip_delay"` (needs a signal, no delay). None in the mod set do.

## Delayed: examples

Everything not listed above, including:
- **Engines and power:** Activate/Shutdown Engine, Toggle Mode, converters, generators, radiators, intakes, reaction wheels, thrust, gimbal and authority limiters.
- **Parts that move:** Extend/Retract on panels, antennas and ladders; Deploy and Cut on chutes; Toggle Gear; servos; Deploy on fairings.
- **Separation and docking:** Decouple, Undock, Release, crossfeed, Make Primary Docking Node, Release Clamp.
- **Science and comms:** Deploy and Reset on experiments, Transfer Data, Transmit Data, the Enviro sensor's Toggle Display (it powers the sensor), orbital surveys, Start Object Tracking.
- **Crew and construction:** Transfer Crew, Level Up Crew, Autostrut, Rigid Attachment, Remove From Symmetry. They change the craft or its crew, so the order has to get there. A vessel with a pilot or probe-capable crew has local control and no delay anyway.
- **Mods:** MKS converters and WOLF (Connect to Depot, routes, recipes), USI deploy, anchor and pulse-drive fuel, Konstruction magnets, pistons and welding, KAS winches and ports, SCANsat Start/Stop scan and Analyze Data, RealAntennas Disable antenna permanently.

## Not covered (instant whatever the table says)

- **Buttons inside other windows** opened from a part menu: the robotics controller and the mod windows above. Buttons in them that command the vessel should be delayed, and will be, one window at a time (COVERAGE.md). The science results dialog is covered: Transmit, Discard, Reset and Process in lab are delayed, and Keep is instant. KerbNet only shows things, so it stays instant.
- **Resource flow and priority toggles** in a part menu. They don't use `SetFieldValue`.

## Differences from RemoteTech

RemoteTech's list of instant buttons is only renaming, crew transfer and the camera. It delays Control From Here, Review Data, KerbNet, target selection and the scanner overlay; LightLag treats those as view or UI. LightLag delays crew transfer, which RemoteTech doesn't. RemoteTech also delays only toggles, sliders and cycles among the controls; LightLag delays every kind.

## Adding a mod

When a mod joins the set, list its flight part-menu buttons and fields and apply the test. For a quick list, search its source for `KSPEvent` with `guiActive = true`. Put the instant ones in a new `Exemptions/<Mod>.cfg`, and add them to the table above.
