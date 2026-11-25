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

Load a save and enter the Space Center. Results go to `KSP.log`:

```
[LightLag Test] PASS assembly loaded — 0.1.0.0
[LightLag Test] SUMMARY pass=3 fail=0
```

## Tests

| Test | Checks |
|---|---|
| assembly loaded | `LightLag.dll` is in `AssemblyLoader.loadedAssemblies` |
| settings type present | `LightLag.LightLagParams` exists |
| settings registered in the current game | `CustomParams(LightLagParams)` returns the settings node |
