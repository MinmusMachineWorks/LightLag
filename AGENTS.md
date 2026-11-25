# AGENTS.md

Instructions for AI coding agents (and humans) working in this repo.

## What this project is

**LightLag** is a standalone KSP 1.12 mod. It adds RemoteTech-style light-speed **signal delay** and a **command queue** on top of CommNet.

**What it reads:** only what every CommNet implementation provides on `vessel.connection`:
- `ControlPath` (its links and their endpoints);
- `IsConnected`;
- `GetControlLevel`.

**What it doesn't do:** replace CommNet or subclass `CommNetVessel`. That's why it works on stock CommNet and on RealAntennas alike. Dishes, pointing and signal strength are RealAntennas' job.

**How it works:** input is intercepted with Harmony and with a `FlyByWire` delay queue. Nothing in KSP or other mods is modified on disk.

Read these before changing anything:
- `README.md`: user-facing overview, settings, build, install.
- `docs/DESIGN.md`: architecture, delay math, decided and open questions, gotchas, milestones.
- `docs/COVERAGE.md`: **the coverage matrix**. Every way a player can affect a vessel, its hook, and its status (✅/⬜/⚠️/🐞).

## Rule #1: keep the docs in sync with the code

**Every change that alters behavior, scope, build, or design must update the docs in the same change.** A change isn't finished until the docs describe the code as it now stands.

| If you… | Update… |
|---|---|
| Add, remove, or change a Harmony patch or input hook | `docs/COVERAGE.md` (row status and entry point) and the header comment of the hook's class |
| Deliberately leave an input path undelayed | Say why in a comment at the hook **and** in `docs/COVERAGE.md` |
| Exempt a part-menu event or field (a new mod in the set, a UI-only button) | The mod's file in `GameData/LightLag/Rules/` (one per mod, `Stock.cfg` for stock) and the table in `docs/PART_MENUS.md`, which says how to apply the test |
| Delay a window's buttons (a new mod in the set) | A `LIGHTLAG_WINDOW` node in the mod's `Rules/` file, the windows table in `docs/PART_MENUS.md`, and `docs/COVERAGE.md` |
| Add or change a setting | `Settings.cs`, `Localization/en-us.cfg`, and the README settings table |
| Change what's saved in the queue | `docs/DESIGN.md` persistence notes |
| Resolve an open question | `docs/DESIGN.md` (mark it decided and say what was chosen) |
| Learn a non-obvious gotcha (KSP or CommNet internals, load order, another mod's behavior) | `docs/DESIGN.md` gotchas |
| Change the build, dependencies, target framework, CI, or install layout | README "Building"/"Install", `ckan/metadata.json` (the dev metapackage follows it), and "Build & test" below |
| Change the public API | `docs/DESIGN.md` API section, and tell the user: MechJebRTBridge's LightLag backend depends on it |
| Add or change a `[LightLag]` log line the tests match, or a test | `tests/LightLag.Tests` and the table in `tests/README.md` |

**Also:**
- Comments describe the code as it is, not its history ("was", "used to", "now").
- If you find docs that are already wrong, fix them or flag them.
- **Licensing:** LightLag is MIT. Don't copy code or configs from RemoteTech (GPL-2.0) or RealAntennas (CC-BY-SA-4.0). Reading them to learn how KSP behaves is fine.

## Repo layout

```
src/LightLag/                 LightLag.dll
  Bootstrap.cs                KSPAddon(Instantly): applies Harmony patches, each in its own try/catch
  Settings.cs                 LightLagParams: difficulty settings, in stock CommNet's section
  Log.cs                      [LightLag]-prefixed logging
  Delay/DelayService.cs       delay from vessel.connection.ControlPath; Applies(), GetDelay(), Format()
  Delay/LocalControl.cs       EVA or CommNet's Kerbal control flag: no delay
  Vessel/LightLagVessel.cs    VesselModule on loaded vessels: FlyByWireDelay first in OnPreAutopilotUpdate; queue, extra delay, save
  Input/FlyByWireDelay.cs     queues the player's ctrlState and replays it after the delay
  Input/PlayerInput.cs        player context: set around the player's entry points by Harmony
  Input/Hooks/CommandHooks.cs prefixes on staging, action groups and SAS modes; queue in player context
  Input/Hooks/PartMenuHooks.cs part-menu buttons and controls on the active vessel → part commands
  Input/Hooks/ScienceHooks.cs science results dialog: Transmit, Discard, Lab → ScienceCommand
  Input/Hooks/WindowHooks.cs  prefixes on window commands from LIGHTLAG_WINDOW → MethodCommand
  Input/Windows.cs            reads LIGHTLAG_WINDOW nodes at the main menu and applies their patches
  Input/Exemptions.cs         part-menu exemptions: LIGHTLAG_EXEMPTIONS nodes, skip_delay/skip_control
  Queue/QueuedCommand.cs      a discrete command: sent, arrival and execute UTs, cancel, save/load
  Queue/Commands/*.cs         StageCommand, ActionGroupCommand, SasModeCommand, PartEventCommand, PartFieldCommand, ScienceCommand, MethodCommand
  Queue/PartRef.cs            a saved part and module reference (persistentId, module index and class)
  Queue/CommandQueue.cs       a vessel's commands in run order; runs due ones, handles cancels
  Rules/SignalLoss.cs         stick centered, throttle cut or held, once in-flight input has arrived
  Rules/WarpGuard.cs          drops time warp before a queued command runs
  UI/SignalBarWidget.cs       delay readout and flight computer button in the stock signal icon row (flight and map)
  UI/DelayStatus.cs           the active vessel's delay state as short and long text
  UI/FlightComputerWindow.cs  flight computer: control path, input in flight, extra delay, queue with cancel
  UI/Messages.cs              on-screen messages
docs/DESIGN.md                design, decisions, gotchas, milestones
docs/COVERAGE.md              input coverage matrix
docs/PART_MENUS.md            which part-menu buttons and controls are delayed, and why
GameData/LightLag/            packaging root (Plugins/ and LightLag.version are build outputs, gitignored)
  Rules/*.cfg                 per mod (Stock.cfg first): exempt part-menu events and fields, and delayed window buttons
tests/LightLag.Tests/         dev-only in-game test harness (separate plugin, never shipped)
ckan/metadata.json            CKAN metadata template for the shared release workflow
tools/dev_ckan.py             writes ckan/LightLag-dev.ckan, a metapackage of those relations for dev installs
```

**Reference checkouts beside this repo** (read-only, never modified from here):
- `../RemoteTech`: RemoteTech source, for how it delays input.
- `../src/RealAntennas`: RealAntennas source. It's the CommNet implementation LightLag must keep working on.
- `../MechJebRTBridge`: the MechJeb bridge that will get a LightLag backend.

## Build & test

- **Build:** `dotnet build LightLag.sln -c Release`.
- **Machine paths:** `KSPBT_GameRoot` and `KSPBT_ManagedPath` go in the gitignored `Directory.Build.props.user`. Without it, KSPBuildTools uses the default Steam path. The KSP install needs Harmony2.
- **Dev install:** a build copies `GameData/LightLag/` into the install. `tools/dev_ckan.py` writes the `LightLag-dev` CKAN metapackage from `ckan/metadata.json`, so CKAN manages the dependencies of a dev install. It's generated, never edited by hand: change `ckan/metadata.json` instead.
- **CI:** `.github/workflows/release.yml` installs Harmony2 through CKAN, builds the solution, and hands `GameData/` to `MinmusMachineWorks/KSP-Mods-CKAN`'s `release-mod.yml`. A `v*.*.*` tag publishes a release (`release_status: testing` until 1.0). MinVer derives the version from the tag.
- **Tests:** `tests/README.md`.
