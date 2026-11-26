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
| Readout | `SignalBarWidget` (done): a stopwatch icon and the delay as one more item in CommNet's signal icon row (`TelemetryUpdate`), so it's there in flight and map view; text styled from the MET display, with the stock tooltip. With no signal, just the stopwatch, grey, so the flight computer can still be opened | M1 |
| Next contact | `ContactPredictor` (done, stock CommNet only): with no signal, when the vessel is expected to get one, and through which station or vessel. Off in RealAntennas mode (Q9). See §3 | M2 |
| Player context | `PlayerInput` (done): a depth counter that Harmony sets around the player's entry points, `FlightInputHandler.Update`, `ActionGroupToggleButton.SetToggle` and `VesselAutopilotUI.OnClickButton` | M2 |
| Discrete hooks | `CommandHooks` (done): prefixes on `StageManager.ActivateNextStage`, `ActionGroupList.ToggleGroup` and `SetGroup`, and `VesselAutopilot.SetMode`. They queue a command only in player context | M2 |
| Queue | `CommandQueue` (done) and `QueuedCommand` with `StageCommand`, `ActionGroupCommand` and `SasModeCommand` (done), held and run by `LightLagVessel` | M2 |
| Flight computer | `FlightComputerPanel` (done): uGUI "mission console" (black glass, monospace, signal green, amber for what's on its way) opened from the readout; control path hop by hop (with no signal, the next contact instead), input in flight, queue with countdowns and abort, extra delay | M2 |
| Time warp | `WarpGuard` (done): drops on-rails warp to 1× before a queued command runs | M2 |
| Part menus | `PartMenuHooks` (done): `BaseEvent.Invoke` inside `UIPartActionButton.OnClick`, and `UIPartActionFieldItem.SetFieldValue`, → `PartEventCommand` and `PartFieldCommand`; `ScienceHooks` wraps the science results dialog's callbacks → `ScienceCommand`; `Windows`/`WindowHooks` delay window buttons listed in `LIGHTLAG_WINDOW` nodes → `MethodCommand`; `Exemptions` reads `LIGHTLAG_EXEMPTIONS` nodes and RemoteTech's `skip_delay`/`skip_control` categories | M3 |
| API | `LightLagAPI` | M4 |
| RealAntennas adapter | `LightLag.RealAntennas.dll` with `[KSPAssemblyDependency("RealAntennas")]`; strength rules from `RACommLink` data rates; a next-contact model (Q9) | M5 |

## 3. Delay math

- **Delay:** one way, Σ over the links of `ControlPath` of |a.position − b.position| ÷ (c × `lightSpeedMultiplier`), with c = 299,792,458 m/s. The path runs to the nearest control source, which CommNet already picks through `FindClosestControlSource`.
- **No connection:** with stock's "Require Signal for Control" on, the delay is infinite and nothing new can be sent. With it off, no delay applies.
- **Local control:** the delay is 0.

### Next contact

CommNet only knows who can reach whom right now; nothing in KSP looks ahead. `ContactPredictor` does, for the active vessel while it has no signal:
- **Model:** stock CommNet's link rules at future positions. Every node keeps the antenna powers CommNet gave it now. A link needs `CommNetScenario.RangeModel.InRange` for one of the relay/transmit power pairs, and no body within radius × `scaledRadiusHorizonMultiplier` × CommNet's occlusion multiplier (vacuum or atmosphere) of the line, like stock's horizon culling but as a sphere. A path goes through nodes with relay power and ends at a control source (`isControlSource`; one that isn't multi-hop only directly).
- **Where things will be:** vessels in flight on their current orbit (`Orbit.getTruePositionAtUT`); landed vessels and ground stations at their latitude, longitude and altitude, turned with the body (`360° × Δt / rotationPeriod` of longitude), plus the body's own `getTruePositionAtUT`.
- **Search:** 600 samples up to a horizon of two orbits (or two of the body's days when landed), between an hour and 50 game days, and no later than the end of the orbit patch (SOI change, maneuver). 40 samples a frame; then bisection to about a second. It restarts every 5 s of real time and keeps the last answer meanwhile. A window shorter than the sample step can be missed.
- **Not modelled:** burns, staging, plasma blackout, power running out. If the model sees a path now while CommNet doesn't, something outside it cuts the signal (plasma, say), so there's no estimate.
- **RealAntennas mode:** off whenever RealAntennas is loaded (`ContactPredictor.RealAntennasMode`), and on any other network that isn't stock's `CommNetwork`; the flight computer and tooltip show no line. Turning it back on for RealAntennas is part of M5 (Q9).
- **Self-check:** `ContactPredictor.ModelCheck` runs the model at the current time; the harness checks it agrees with CommNet for a connected vessel.

## 4. Public API (M4; sketch)

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

Part commands save their target as the part's `persistentId` plus the module's index and class name (`TARGET` nodes for buttons, inline for fields), the event or field name, and for fields the value as text (`value`), parsed against the field's type when it runs, plus `symmetry`. Science commands save the module (`TARGET`), the action, the module's callback method name and the data's `subjectID`; the data itself stays on board in the module and is found again by subject when the command runs. Window commands (`MethodCommand`) are saved only when they're calls on a part module with simple arguments (the module, declaring class, method, and each argument's type and value); others hold the live object and arguments in memory, aren't written to the save, and are lost on a reload or scene change.

**Not saved:** steering and throttle input in flight. What's still in `FlyByWireDelay`'s queue is lost on a scene change or reload; the vessel keeps the last input that had arrived.

**Unloaded vessels:** the queue stays in the save. Commands that came due while the vessel was unloaded run in order once it's loaded and off rails, with a message. Staging among them fails unless the vessel is the active one.

## 6. Questions

| # | Question | Status |
|---|---|---|
| 1 | Name | Decided: LightLag |
| 2 | One-way or round-trip delay | Decided: one way only. A round-trip setting existed early on and was removed: it only doubled the number and confused what the delay means |
| 3 | Do commands already sent still arrive after signal loss? | Decided: yes, for steering and throttle (M1) and for discrete commands (M2) |
| 4 | Which part-menu actions are exempt? | Decided (M3): our own analysis, in `PART_MENUS.md`. Every button and control on the active vessel's part menus is delayed unless it doesn't command the vessel: your viewpoint (Control From Here, camera), windows and readouts (Review Data, KerbNet, mod windows), names, targets and tallies, and cosmetics. Crew transfer, Level Up Crew and construction settings (autostrut, rigid attachment, symmetry) are delayed. These are listed in `Rules/*.cfg` (a file per mod; any mod can add a `LIGHTLAG_EXEMPTIONS` node); RemoteTech's `skip_delay`/`skip_control` markers are honored too. RemoteTech's own list (renaming, crew transfer, camera) was the starting point, but it delays viewpoint and window buttons |
| 5 | Control with no signal | Decided: follow stock's "Require Signal for Control". **On:** nothing new is sent; input and commands already sent still arrive, then the stick centers and the throttle is cut or held. Stock already gives an uncrewed probe no control without a signal, so this matches it. **Off:** delay doesn't apply, so what stock's partial control allows (staging, action groups, SAS, RCS) acts at once; input still in flight is dropped, and queued commands still run when due |
| 6 | Commands for unloaded vessels | Decided (M2): kept in the save; overdue ones run in order on load, with a message |
| 7 | Which calls count as the player's? | Decided (M2): only those made inside `FlightInputHandler.Update`, `ActionGroupToggleButton.SetToggle`, `VesselAutopilotUI.OnClickButton` and (M3) `UIPartActionButton.OnClick`, plus part-menu controls' `SetFieldValue` and the science results dialog's Transmit, Discard and Lab callbacks (only its buttons call them), and the entry methods of windows in `LIGHTLAG_WINDOW` nodes. KSP's and other mods' calls stay instant |
| 9 | Next contact on RealAntennas | Open (M5). For now `ContactPredictor` is off in RealAntennas mode (RealAntennas loaded) and on any network that isn't stock `CommNetwork`, and nothing is shown, since RealAntennas' links are a link budget (power, gain, pointing, noise, encoding), not stock's range formula. What RealAntennas offers (read 2026-11-25, `4628e68`): its link math runs in `Precompute.Precompute` (public: `Initialize(nodes)`, `DoThings(bodies, nodes, forceValid)`, `SimulateComplete(ref ConnectionDebugger, nodes)`), and its planner window already runs it on made-up nodes without making links. But it reads the present from live objects: node `precisePosition` (settable), occluders from `body.position` (not), and dish pointing from `RealAntenna.ToTarget` (the target's transform now). So it can't be pointed at a future time from outside. **Options:** (a) a small upstream API in RealAntennas that takes body positions and antenna directions as input, or evaluates the network at a given UT; (b) in the adapter, our own link budget built on its public `Physics` functions (`ReceivedPower`, `PointingLoss`, `NoiseTemperature`) and each antenna's encoder, with our geometry and pointing at the future time; (c) occlusion only, labelled as a best case. (a) is the most faithful; (b) is the fallback. Don't copy RealAntennas code into LightLag either way |
| 8 | How is stick and throttle input in flight shown? | Decided: as one indicator, not a list or a count. Holding a key sends a stream of small changes, so a count means nothing; the readout says "input arriving" and the flight computer says how long until the last of it lands. Only discrete commands are listed, and only they can be cancelled |

## 7. Gotchas

- **Input order:**
  - `FlightInputHandler` copies the player's input into the active vessel's `ctrlState` (`Vessel.SetControlState`). Then `Vessel.FeedInputFeed` calls `OnPreAutopilotUpdate`, `OnAutopilotUpdate` (stock SAS), `OnPostAutopilotUpdate` and `OnFlyByWire`, in that order.
  - LightLag hooks **first in `OnPreAutopilotUpdate`**, so only the player's input is delayed, and SAS and autopilots (MechJeb drives on `OnFlyByWire`) act on board.
  - RemoteTech hooks `OnFlyByWire` instead, and has to strip the stock autopilot's output back out.
  - `LightLagVessel` re-checks each physics frame that its callback is still first.
- **Non-active loaded vessels:** KSP zeroes their stick and translation every frame but leaves their throttle. `FlyByWireDelay` still runs on them, so input sent before a vessel switch keeps arriving.
- **Throttle gauge:** the stock gauge shows `FlightInputHandler.state.mainThrottle`, the commanded throttle, not what has arrived. The vessel acts on `ctrlState`.
- **Only changes are queued.** Consecutive equal states are skipped, so a long delay doesn't build a queue of identical frames. When delay starts (after a load, a vessel switch, crew leaving), the current input counts as already sent; after a signal loss, it's compared with what the vessel is doing (stick centered, throttle per the setting). Otherwise unchanged input would show as still arriving.
- **Arrival slack:** inputs due within half a physics frame are delivered this frame, so a near-zero delay (on the pad, at KSC) adds no lag.

- **Stock's no-signal locks come first.** `ModuleCommand.UpdateControlSourceState` decides an uncrewed probe's control without a signal, and `Vessel` sets the matching `vessel_noControl_<id>` input lock. `FlightInputHandler` and the UI check it before calling `ToggleGroup` or staging, so locked keys never reach `CommandHooks`:
  - **"Require Signal for Control" on:** "No Telemetry", control level `NONE`, lock `ALL_SHIP_CONTROLS`. Every key is stopped by stock, so `CommandHooks`' "not sent" message is only a safety net.
  - **Off:** partial control, lock `PARTIAL_SHIP_CONTROLS` (pitch, roll, yaw, throttle, translation, wheel). `Vessel.PartialControlHasSASRCS` is true by default, so SAS and RCS stay usable; with it false, the lock is `PARTIAL_SHIP_CONTROLS_SASRCS`, which also locks them. Staging and action groups are never locked here, and LightLag lets them act at once.
- **Part-menu buttons:** `UIPartActionButton.OnClick` sends the event to symmetry counterparts with `part.Events.Send(id)` and then calls `evt.Invoke()`; both end in `BaseEvent.Invoke`, and `BaseEventList.Send` uses the overload with `BaseEventDetails`, so both overloads are hooked. KSP names a `BaseEvent` after its method, not the `KSPEvent` attribute's `name`, so exemptions list method names. An event that runs at once runs outside player context, so the events it calls aren't queued instead.
- **Window patches wait for the main menu.** `Bootstrap` runs at `Startup.Instantly`, before the game database (and Module Manager's patches) has loaded, so `Windows` reads `LIGHTLAG_WINDOW` nodes and applies its patches from a `MainMenu` addon, when every mod assembly is loaded too.
- **KAS hold-to-run buttons repeat.** `GUILayoutButtons.Push` compares the button with the motor's actual state, so while a press is on its way it calls `SetMotor` every frame. `WindowHooks` drops a call whose arguments match the latest one still queued for the same target.
- **Science results dialog:** each page carries the module's Transmit, Discard, Keep and Lab callbacks, and `addPage` chains the dialog's own page-dismiss onto them, so wrapping them before `addPage` still closes the page. `ModuleScienceExperiment` hides its part-menu buttons while its `resultsDialog` is set, and only a callback clears it; a queued click therefore runs Keep at once, or the experiment's menu would stay empty until the command ran (or forever, if cancelled).
- **Part-menu controls:** every control sets its value through `UIPartActionFieldItem.SetFieldValue`, which applies symmetry (only when `affectSymCounterparts` includes the scene) and calls `onFieldChanged`; `PartFieldCommand` does the same when it runs. `UIPartActionFloatEdit.Setup` also calls it, to clamp a value as the menu opens, so that call is excluded. Each frame `UpdateItem` redraws a control from the field's value, so a held-back slider would snap back under the mouse; the `UIPartActionFloatRange.UpdateItem` postfix shows the value on its way instead.
- **Player context, not call sites:** KSP itself calls `SetGroup(SAS, false)` (`VesselAutopilot.Update`) and `SetMode(StabilityAssist)` (`VesselAutopilotUI.LateUpdate`), and MechJeb calls `StageManager.ActivateNextStage` directly. Delaying those would make on-board behavior lag, so `CommandHooks` intercept only inside the player entry points that `PlayerInput` brackets.
- **The stage key does two things:** `FlightInputHandler.Update` calls `StageManager.ActivateNextStage()` and then `ToggleGroup(Stage)`. When the staging is queued, the toggle in the same frame is folded into the `StageCommand`.
- **Staging acts on the active vessel only:** `ActivateNextStage` is static. A `StageCommand` that comes due after the player switched vessels fails with a message.
- **SAS mode buttons resync:** `VesselAutopilotUI.OnClickButton` returns early when `SetMode` returns false, and `LateUpdate` redraws the buttons from `Autopilot.Mode`. The hook returns false for a queued mode, so the navball shows the vessel's actual mode until the command arrives. The SAS/RCS buttons likewise follow the group's actual state.
- **Time warp:** on-rails warp can jump past a command within one frame, and staging can't run on rails. `WarpGuard` drops on-rails warp to 1× (physics warp is left alone) when the next command is within three frames at the current rate plus a second; commands wait while the vessel is packed.
- **The signal bar is uGUI.** Stock shows or hides the globe button just by toggling it, so the icon row lays itself out. The readout joins that row (the nearest `HorizontalLayoutGroup` above `signal_icon`) as its last item, so the panel's own background takes it in, it follows the panel into map view, and it scales with the UI. Without a layout group it's placed after the rightmost icon and the background is widened by hand. The panel's structure is logged once per session (`signal bar:` lines) for when stock or a mod changes it. The panel is inactive without CommNet, and the flight UI builds over the first frames, so it's attached from a coroutine; the log says where it attached. TextMeshPro needs its own assembly reference (`Unity.TextMeshPro.dll`).
- **The flight computer is uGUI on KSP's app canvas** (`UIMasterController.appCanvas`), so KSP scales it by UI scale × app scale and hides it with F2. It's built in code: layout groups, a `ContentSizeFitter`, TextMeshPro text in a monospace font if the game has one loaded (stock has none, so it falls back to Noto Sans, whose digits are already fixed width), and a `TMP_InputField`. Glyphs are kept to ones KSP's fonts have (no Δ, minus sign or box drawing). KSP's TextMeshPro reads `<mspace=0.6em>` as a tiny width and stacks the characters, so `<mspace>` isn't used. In localization files `//` starts a comment, so a string can't contain it. CommNet node names, such as KSC's stations, are `#autoLOC` tags that need `Localizer.Format`.
- **Typing in the flight computer:** UI text fields don't stop KSP's key bindings, so the window sets a `KEYBOARDINPUT` lock while its field has focus.
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
   - flight computer window with cancel and extra delay;
   - the queue is saved;
   - time warp drops before an arrival.
4. **M3:** part menus, moved ahead so a near-stock game is fully covered. **Code done; not checked in game yet.**
   - part-menu buttons and controls on the active vessel through the queue;
   - RemoteTech's exemption rules, `Rules/*.cfg`, one file per mod in the set, and the `skip_delay`/`skip_control` markers.
5. **M4:** `LightLagAPI` and a LightLag backend in MechJebRTBridge.
6. **M5:** RealAntennas checked in game, plus the adapter DLL and strength rules. Then whatever `COVERAGE.md` shows missing.
   - **Next contact in RealAntennas mode:** it's off today (`ContactPredictor.RealAntennasMode`). Pick an option from Q9, give the adapter its own model, and turn it back on.
