# LightLag design

The background research is in `../COMMNET_RT_FEASIBILITY.md` and the repo plan in `../LIGHTLAG_REPO_PLAN.md`, both in the KSP project folder outside this repo. This file is the living design: update it with the code (AGENTS.md, Rule #1).

## 1. Principles

1. **Read CommNet, don't replace it.** LightLag uses only `vessel.connection`: `ControlPath`, `IsConnected` and `GetControlLevel`. Its per-vessel state lives in a plain `VesselModule`, never a `CommNetVessel` subclass. Stock CommNet and RealAntennas both work unchanged underneath.
2. **Delay commands, not physics.** A command (a stick input, staging, an action group, a SAS mode) takes effect after the delay. What it starts then runs locally in real time, the way an on-board computer would. MechJebRTBridge already follows this model for MechJeb.
3. **Local control has no delay.** No delay applies to:
   - a crewed command pod with enough crew to control it;
   - an EVA kerbal;
   - a vessel whose nearest control source is on board.
4. **Stock rules first.** "Require Signal for Control", plasma blackout and probe control points stay stock's. LightLag adds only what stock lacks.
5. **Each hook stands alone.** Every Harmony patch is applied in its own try/catch (`Bootstrap.Patch`), so one broken target can't disable the rest.

## 2. Architecture

| Area | Class (planned) | Milestone |
|---|---|---|
| Settings | `LightLagParams` (done) | M0 |
| Delay | `DelayService`: per-vessel delay from `ControlPath`, cached per physics frame; `LocalControl` | M1 |
| Steering and throttle | `FlyByWireDelay`: queue `FlightCtrlState` with an arrival time, replay it after the delay | M1 |
| Signal loss | `Rules/SignalLoss`: commands already sent still arrive; throttle cut or hold | M1 |
| Readout | `UI/DelayReadout` | M1 |
| Queue | `LightLagVessel` (VesselModule), `CommandQueue`, `QueuedCommand` and its subclasses | M2 |
| Discrete hooks | Harmony prefixes on `StageManager.ActivateNextStage`, `ActionGroupList.ToggleGroup` and `VesselAutopilot.SetMode` | M2 |
| Queue UI | `UI/QueueWindow`: countdowns, cancel, extra delay | M2 |
| Time warp | drop out of warp before a queued command arrives | M2 |
| API | `LightLagAPI` | M3 |
| RealAntennas adapter | `LightLag.RealAntennas.dll` with `[KSPAssemblyDependency("RealAntennas")]`; strength rules from `RACommLink` data rates | M4 |
| Part menus | `BaseEvent.Invoke`, part-menu fields | M5 |

## 3. Delay math

- **One way:** delay = Σ over the links of `ControlPath` of |a.position − b.position| ÷ (c × `lightSpeedMultiplier`), with c = 299,792,458 m/s. The path runs to the nearest control source, which CommNet already picks through `FindClosestControlSource`.
- **Round trip:** `roundTrip` doubles it.
- **No connection:** the delay is infinite, and nothing new can be sent.
- **Local control:** the delay is 0.

## 4. Public API (M3; sketch)

It mirrors the parts of `RemoteTech.API` that MechJebRTBridge uses, so the bridge's LightLag backend stays small:

```csharp
public static class LightLagAPI
{
    public static bool IsEnabled();
    public static bool HasLocalControl(Guid vesselId);
    public static double GetDelay(Guid vesselId);        // seconds; +Infinity when not connected
    public static bool IsConnected(Guid vesselId);
    public static bool Enqueue(Guid vesselId, ConfigNode command);   // Executor, ReflectionType, ReflectionPopMethod, QueueLabel, caller data
    public static event Action<Guid, ConfigNode> CommandArrived;
}
```

## 5. Persistence (M2)

The queue is saved in `LightLagVessel`'s ConfigNode. Each command records its send UT, arrival UT, type, label and payload.

**Not decided yet:** what happens with commands for unloaded vessels. They could run on load, or be dropped with a message.

## 6. Questions

| # | Question | Status |
|---|---|---|
| 1 | Name | Decided: LightLag |
| 2 | One-way or round-trip delay | Decided: one way by default, with round trip as a setting |
| 3 | Do commands already sent still arrive after signal loss? | Proposed: yes |
| 4 | Which part-menu actions are exempt? | Open (a `skip_control`-style category plus a list) |
| 5 | Control with no signal | Proposed: stock "Require Signal for Control"; no stricter rule of our own |
| 6 | Commands for unloaded vessels | Open |

## 7. Gotchas

- **Settings section:** `LightLagParams` uses `Section = "Advanced"` and stock CommNet's `DisplaySection` (`#autoLoc_6002170`), with `SectionOrder = 2`, so it shows up right after the CommNet settings.
- **RealAntennas and plasma:** RA overrides `CommNetwork.TryConnect` and never calls `GetSignalStrengthMultiplier`, so stock's plasma penalty doesn't reach RA links. A RealAntennas PR is planned. Until then, a vessel in plasma under RA keeps its link.

## 8. Milestones

1. **M0:** skeleton, CI, settings, harness. Builds in CI; not loaded in game yet.
2. **M1:** delay on stock CommNet:
   - delay readout;
   - delayed steering and throttle;
   - signal-loss rules;
   - local-control exemptions.
3. **M2:** the queue:
   - staging, action groups, SAS/RCS and SAS modes through it;
   - queue window with cancel and extra delay;
   - the queue is saved;
   - time warp drops before an arrival.
4. **M3:** `LightLagAPI` and a LightLag backend in MechJebRTBridge.
5. **M4:** RealAntennas checked in game, plus the adapter DLL and strength rules.
6. **M5:** part menus, then whatever `COVERAGE.md` shows missing.
