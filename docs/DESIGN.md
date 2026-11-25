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
| Delay | `DelayService` (done): per-vessel delay from `ControlPath`, computed on each call (a few links, cheap); `LocalControl` (done) | M1 |
| Steering and throttle | `FlyByWireDelay` (done): queue the player's `ctrlState` with an arrival time, replay it after the delay. Owned by `LightLagVessel` (done), a plain VesselModule on loaded vessels | M1 |
| Signal loss | `SignalLoss` (done): input already sent still arrives; then the stick centers and the throttle is cut or held. Only while stock's "Require Signal for Control" is on (`DelayService.SignalRequired`) | M1 |
| Readout | `DelayReadout` (done): IMGUI line under the stock time display | M1 |
| Player context | `PlayerInput` (done): a depth counter that Harmony sets around the player's entry points, `FlightInputHandler.Update`, `ActionGroupToggleButton.SetToggle` and `VesselAutopilotUI.OnClickButton` | M2 |
| Discrete hooks | `CommandHooks` (done): prefixes on `StageManager.ActivateNextStage`, `ActionGroupList.ToggleGroup` and `SetGroup`, and `VesselAutopilot.SetMode`. They queue a command only in player context | M2 |
| Queue | `CommandQueue` (done) and `QueuedCommand` with `StageCommand`, `ActionGroupCommand` and `SasModeCommand` (done), held and run by `LightLagVessel` | M2 |
| Queue UI | `QueueWindow` (done): opened by clicking the readout; countdowns, cancel, extra delay | M2 |
| Time warp | `WarpGuard` (done): drops on-rails warp to 1× before a queued command runs | M2 |
| API | `LightLagAPI` | M3 |
| RealAntennas adapter | `LightLag.RealAntennas.dll` with `[KSPAssemblyDependency("RealAntennas")]`; strength rules from `RACommLink` data rates | M4 |
| Part menus | `BaseEvent.Invoke`, part-menu fields | M5 |

## 3. Delay math

- **One way:** delay = Σ over the links of `ControlPath` of |a.position − b.position| ÷ (c × `lightSpeedMultiplier`), with c = 299,792,458 m/s. The path runs to the nearest control source, which CommNet already picks through `FindClosestControlSource`.
- **Round trip:** `roundTrip` doubles it.
- **No connection:** with stock's "Require Signal for Control" on, the delay is infinite and nothing new can be sent. With it off, no delay applies.
- **Local control:** the delay is 0.

## 4. Public API (M3; sketch)

It mirrors the parts of `RemoteTech.API` that MechJebRTBridge uses, so the bridge's LightLag backend stays small:

```csharp
public static class LightLagAPI
{
    public static bool IsEnabled();
    public static bool HasLocalControl(Guid vesselId);
    public static double GetDelay(Guid vesselId);        // seconds; +Infinity when not connected and a signal is required, 0 when no delay applies
    public static bool IsConnected(Guid vesselId);
    public static bool Enqueue(Guid vesselId, ConfigNode command);   // Executor, ReflectionType, ReflectionPopMethod, QueueLabel, caller data
    public static event Action<Guid, ConfigNode> CommandArrived;
}
```

## 5. Discrete commands and persistence

**How a command travels (M2):**
- **Sent:** a player command on a vessel with delay becomes a `QueuedCommand` with `SentUT`, `ArrivalUT` = sent + delay, and `ExecuteUT` = the later of arrival and sent + the vessel's extra delay. The original call is skipped.
- **Pass-through:** with a delay under half a physics frame and no extra delay, the call just runs.
- **No signal:** with stock's "Require Signal for Control" on, the command isn't sent, and a message says so. With it off, delay doesn't apply and the command runs at once.
- **Runs:** `LightLagVessel` runs due commands each physics frame while the vessel is off rails, outside player context, so the hooks let them through. A failure (staging on a vessel that's no longer active, a SAS mode the vessel can't hold) shows a message.
- **Cancel:** a cancel is a signal too. The window allows it only while now + delay is before `ExecuteUT`, and the command is dropped if the cancel arrives first.
- **Extra delay:** per vessel, saved, and applied to discrete commands sent afterwards. It doesn't apply to steering and throttle, or under local control.

**Saved:** `LightLagVessel` writes its queue into the vessel's `VESSELMODULES` node in the save:

```
LightLagVessel
{
    extraDelay = 0
    COMMAND
    {
        type = SasMode          // Stage | ActionGroup | SasMode
        sent = 123456.78
        arrival = 123458.9
        execute = 123458.9
        cancelArrival = …       // only when a cancel was sent
        mode = Prograde         // payload: Stage: toggleStageGroup; ActionGroup: group, toggle, state
    }
}
```

An unreadable entry is dropped with a warning in the log.

**Not saved:** steering and throttle input in flight. What's still in `FlyByWireDelay`'s queue is lost on a scene change or reload; the vessel keeps the last input that had arrived.

**Unloaded vessels:** the queue stays in the save. Commands that came due while the vessel was unloaded run in order once it's loaded and off rails, with a message. Staging among them fails unless the vessel is the active one.

## 6. Questions

| # | Question | Status |
|---|---|---|
| 1 | Name | Decided: LightLag |
| 2 | One-way or round-trip delay | Decided: one way by default, with round trip as a setting |
| 3 | Do commands already sent still arrive after signal loss? | Decided: yes, for steering and throttle (M1) and for discrete commands (M2) |
| 4 | Which part-menu actions are exempt? | Open (a `skip_control`-style category plus a list) |
| 5 | Control with no signal | Decided: follow stock's "Require Signal for Control". **On:** nothing new is sent; input and commands already sent still arrive, then the stick centers and the throttle is cut or held. This is stricter than stock's partial control, which still allows staging and action groups without a signal, because a command can't reach the vessel without one. **Off:** delay doesn't apply, so the vessel is controlled at once, as stock allows; input still in flight is dropped, and queued commands still run when due |
| 6 | Commands for unloaded vessels | Decided (M2): kept in the save; overdue ones run in order on load, with a message |
| 7 | Which calls count as the player's? | Decided (M2): only those made inside `FlightInputHandler.Update`, `ActionGroupToggleButton.SetToggle` and `VesselAutopilotUI.OnClickButton`. KSP's and other mods' calls stay instant |

## 7. Gotchas

- **Input order:**
  - `FlightInputHandler` copies the player's input into the active vessel's `ctrlState` (`Vessel.SetControlState`). Then `Vessel.FeedInputFeed` calls `OnPreAutopilotUpdate`, `OnAutopilotUpdate` (stock SAS), `OnPostAutopilotUpdate` and `OnFlyByWire`, in that order.
  - LightLag hooks **first in `OnPreAutopilotUpdate`**, so only the player's input is delayed, and SAS and autopilots (MechJeb drives on `OnFlyByWire`) act on board.
  - RemoteTech hooks `OnFlyByWire` instead, and has to strip the stock autopilot's output back out.
  - `LightLagVessel` re-checks each physics frame that its callback is still first.
- **Non-active loaded vessels:** KSP zeroes their stick and translation every frame but leaves their throttle. `FlyByWireDelay` still runs on them, so input sent before a vessel switch keeps arriving.
- **Throttle gauge:** the stock gauge shows `FlightInputHandler.state.mainThrottle`, the commanded throttle, not what has arrived. The vessel acts on `ctrlState`.
- **Only changes are queued.** Consecutive equal states are skipped, so a long delay doesn't build a queue of identical frames. When delay starts (after a load, a vessel switch, crew leaving), the current input counts as already sent; after a signal loss, it's compared with what the vessel is doing (stick centered, throttle per the setting). Otherwise unchanged input would show as in flight.
- **Arrival slack:** inputs due within half a physics frame are delivered this frame, so a near-zero delay (on the pad, at KSC) adds no lag.

- **Player context, not call sites:** KSP itself calls `SetGroup(SAS, false)` (`VesselAutopilot.Update`) and `SetMode(StabilityAssist)` (`VesselAutopilotUI.LateUpdate`), and MechJeb calls `StageManager.ActivateNextStage` directly. Delaying those would make on-board behavior lag, so `CommandHooks` intercept only inside the player entry points that `PlayerInput` brackets.
- **The stage key does two things:** `FlightInputHandler.Update` calls `StageManager.ActivateNextStage()` and then `ToggleGroup(Stage)`. When the staging is queued, the toggle in the same frame is folded into the `StageCommand`.
- **Staging acts on the active vessel only:** `ActivateNextStage` is static. A `StageCommand` that comes due after the player switched vessels fails with a message.
- **SAS mode buttons resync:** `VesselAutopilotUI.OnClickButton` returns early when `SetMode` returns false, and `LateUpdate` redraws the buttons from `Autopilot.Mode`. The hook returns false for a queued mode, so the navball shows the vessel's actual mode until the command arrives. The SAS/RCS buttons likewise follow the group's actual state.
- **Time warp:** on-rails warp can jump past a command within one frame, and staging can't run on rails. `WarpGuard` drops on-rails warp to 1× (physics warp is left alone) when the next command is within three frames at the current rate plus a second; commands wait while the vessel is packed.
- **Typing in the queue window:** IMGUI text fields don't stop KSP's key bindings, so the window sets a `KEYBOARDINPUT` lock while its field has focus.
- **Settings section:** `LightLagParams` uses `Section = "Advanced"` and stock CommNet's `DisplaySection` (`#autoLoc_6002170`), with `SectionOrder = 2`, so it shows up right after the CommNet settings.
- **Occlusion modifiers:** stock blocks a link with a sphere of the body's radius × `occlusionMultiplierVac` (airless, default 0.9) or `occlusionMultiplierAtm` (default 0.75), so by default links pass through a planet's outer 10–25%. 1.0 matches RemoteTech, which uses the full radius less 5 m. RealAntennas ignores both settings and always occludes with the full radius.
- **RealAntennas and plasma:** RA overrides `CommNetwork.TryConnect` and never calls `GetSignalStrengthMultiplier`, so stock's plasma penalty doesn't reach RA links. A RealAntennas PR is planned. Until then, a vessel in plasma under RA keeps its link.

## 8. Milestones

1. **M0:** skeleton, CI, settings, harness. Builds in CI; not loaded in game yet.
2. **M1:** delay on stock CommNet. **Code done; builds in CI; not checked in game yet.**
   - delay readout;
   - delayed steering and throttle;
   - signal-loss rules;
   - local-control exemptions.
3. **M2:** the queue. **Code done; not checked in game yet.**
   - staging, action groups, SAS/RCS and SAS modes through it;
   - queue window with cancel and extra delay;
   - the queue is saved;
   - time warp drops before an arrival.
4. **M3:** `LightLagAPI` and a LightLag backend in MechJebRTBridge.
5. **M4:** RealAntennas checked in game, plus the adapter DLL and strength rules.
6. **M5:** part menus, then whatever `COVERAGE.md` shows missing.
