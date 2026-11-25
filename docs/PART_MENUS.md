# Part menus: what's delayed

Every button (`KSPEvent`) and control (`KSPField` with a flight UI control) in the active vessel's part menus is delayed, except the ones below. This is LightLag's own analysis of stock KSP 1.12.5 and the mod set, done on 2026-11-25. `GameData/LightLag/Rules/` implements it, with `Stock.cfg` and one file per mod; keep the two in sync (AGENTS.md, Rule #1).

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

- **Resource transfers:** part-menu resource flow and priority toggles (they don't use `SetFieldValue`), and KAS's fuel transfer window.
- **Robotics sequence editing:** keyframes, length and presets in the robotics controller.

## Windows

Opening a window is instant; the buttons inside it that command the vessel are delayed, by the same test. Each mod's `Rules/<Mod>.cfg` lists them in a `LIGHTLAG_WINDOW` node (`Stock.cfg` explains the format).

| Window | Delayed | Instant |
|---|---|---|
| Science results (Review Data) | Transmit, Discard, Reset, Process in lab (`ScienceHooks`, not a `LIGHTLAG_WINDOW`) | Keep, paging |
| Robotics controller (stock) | Play/pause, reverse, loop mode, skip buttons, position and speed sliders | Sequence editing, renaming |
| KerbNet (stock) | — | Everything: it only shows things |
| KAS winch remote | Motor (start, hold, stop), release, stretch, detach | Highlighting |
| Konstruction | Build vessel (Konstructor), Dekonstruct parts, KonFabricate | Choosing what to build |
| MKS Orbital Logistics | Start, abort, resume transfer (to the origin vessel) | Planning a transfer, clearing finished ones |
| Extraplanetary Launchpads | Build, pause, resume, cancel, restart, finish, release | Choosing a craft, renaming |
| RealAntennas targeting | Picking a target | Target mode and sorting |
| KAS fuel transfer | — (not covered yet) | |

## Differences from RemoteTech

RemoteTech's list of instant buttons is only renaming, crew transfer and the camera. It delays Control From Here, Review Data, KerbNet, target selection and the scanner overlay; LightLag treats those as view or UI. LightLag delays crew transfer, which RemoteTech doesn't. RemoteTech also delays only toggles, sliders and cycles among the controls; LightLag delays every kind.

## Adding a mod

When a mod joins the set, list its flight part-menu buttons and fields and apply the test. For a quick list, search its source for `KSPEvent` with `guiActive = true`. Put the instant ones in a new `Rules/<Mod>.cfg`, and add them to the table above. Then look at the windows its buttons open: find the methods their commanding buttons call, and list them with the window's draw or click methods in a `LIGHTLAG_WINDOW` node in the same file.

**Checking in game.** The window rules for KAS, Konstruction, MKS, Extraplanetary Launchpads and RealAntennas were written from their source and haven't been tried in game; neither has the stock robotics controller's. When a mod's compat is worked on, test its windows too: with delay on, press each delayed button and check it shows in the queue and acts only when it arrives, and that the instant ones still act at once. The log line `window '<name>': N entry methods, M delayed methods` at the main menu shows the rules were applied (a mod's version may have renamed a method; a warning names it). Then mark the window checked in `COVERAGE.md`.
