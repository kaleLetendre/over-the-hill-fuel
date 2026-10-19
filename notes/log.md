# Log

## 2026-10-17: v0.1.0 (phase 1)

### Implemented

- Project `OTH.Fuel` (namespace `OTH.Fuel`), layout and build scripts as in ExtraGears. Builds with 0 warnings, 0 errors; deployed to `Mods/OTH.Fuel.dll`.
- Files: `Mod.cs` (prefs, per-frame tick), `Tank.cs` (fuel state, burn, engine block), `CarSpecs.cs` (capacity table), `SpawnPatches.cs`, `IgnitionPatches.cs`, `RefillPatches.cs`, `FuelBar.cs`.
- **Car discovery** (no scene searches): postfix `NetworkedVehicleStatus.Spawned` queues every car; once its `CarInfo` is known (retried 1/s, up to 10 s) one line is logged: GameObject name, `CarInfo.name`, `ItemId`, state/input authority, proxy, local, capacity. `CarInfo` comes from `VehicleCustomizationController.CurrentCarInfo`, falling back to `Odometer.carInfo`.
- **Local car** = the car passed to `HUDManager.SetupCarRelatedHUD`. Fallback: the first spawned car with `HasInputAuthority`. References (`CarIgnition`, `EngineFloodingSystem`, `CarInventory`, `VPVehicleController`) are found once with GetComponent / InChildren / InParent and cached.
- **Burn**: `OnUpdate`, while `CarIgnition.EngineRunning`: `Engine.sensorFuelRate / Engine.Settings.fuelDensity * FuelScale * dt` litres (VPP engine via `VPVehicleController.m_engine`; `NetworkedVehicleStatus.EngineFuelRate` if there is no VPP engine). If VPP reports 0 for the first 10 s of running, a warning is logged and a crude fallback (0.03 + 0.6 * load * rpm/peak L/min) is used.
- **Capacity**: table keyed by `CarInfo.name` (exact match, case-insensitive; contains-match removed in the review fixes) with the 22 `CarAudioManager.VehicleType` names, all at the test default 2 L. Fuel starts full on adoption.
- **Empty**: `allowedFuelRatio = 0` (previous value saved), `StopEngine()` (throttled to 2/s), `EngineStartBlocked = true` (only with state authority). Re-applied every frame while empty. Prefixes refuse `CarIgnition.StartEngine` and `ToggleEngine` (Toggle only when the engine is off) for the local car while empty.
- **Flooding independence**: postfixes on `EngineFloodingSystem.SetFlooded`, `ResetFloodingState` and `OnStateAuthorityPlayerRespawned` re-assert the fuel block for the local car. A log line says when one of them had cleared it.
- **Refill**: postfix `CarInventory.RPC_RefillItems` refuels the local car to full; every call (any car) is logged with local/state/input flags and recent context. On refill from empty: `allowedFuelRatio` restored; start block handling: see review fixes (ownership).
- **Refill context** (logging only, 15 s window): `CarInventory.OnPOIReached` (POI name + info type), `AskForItemRefill`, `RPC_SetEquippedItems(alsoRefill)`, `CarDamageSystem.RPC_VisualCarHeal` reason, `HUDManager.PortalFinished`, `HUDManager.OnBeginCamping`, respawn. No CampManager hook.
- **HUD**: on `SetupCarRelatedHUD`, clone `engineFloodingUIPanel.floodObject` if it contains `floodFill` (else just the `floodFill` GameObject) under `drivingModeSelector`. Strips `EngineFloodingUIPanel`, `WorldTrackingRectTransform` and localization components, forces CanvasGroup alpha 1, adds `LayoutElement.ignoreLayout`. Scale: see review fixes (template rect size x 0.6 in canvas units). Placed every 1 s with pivot bottom-right just above `GearDisplay`'s top-right world corner. Fill: `fillImage.fillAmount = fuel/capacity` if the image is Filled, else `ContextUIFill.SetValue(frac, 1)`. White, red (0.9, 0.15, 0.1) below `LowFuelThreshold`; background/outline untouched. Icon: embedded `assets/fuel-icon.png` via `ImageConversion.LoadImage` -> `Sprite.Create`, put on the clone's non-bar Image (preferring one named "icon"), else on a new Image left of the bar. Hidden when there is no tracked car. Setup errors: one warning; tick errors: one warning, then the bar stops updating until the next car spawn.
- Prefs `[Fuel]`: `Enabled` (true), `FuelScale` (1.0), `LowFuelThreshold` (0.2), `DebugLog` (false: adds a burn line every 30 s, flooding-call traces and context events).

### Assumptions to verify in game

- **Authority model**: in single player the local car has state and input authority. Check the "tracking ..." and "car ..." lines. As a client (non-host), our burn and block run locally only. `EngineStartBlocked` is not written without state authority, and the host can override `StopEngine`/`allowedFuelRatio`. That's acceptable for phase 1 but not a working multiplayer feature.
- **Fuel units**: `sensorFuelRate` in g/s and `fuelDensity` in g/L, so rate/density = L/s. Check the "engine:" line per car (`maxFuelPerRev`, `fuelDensity`, max rate at peak). If density is ~0.7 (kg/L), the ratio is still right as long as the rate is kg/s. If burn lines show an absurd L/min, the units are wrong.
- **Test capacity**: 2 L assumes about 0.1 L/min in normal driving (realistic VPP settings), so 15-20 min per tank. Read "full tank = N min at this rate" from the DebugLog lines and adjust `CarSpecs.DefaultCapacity` or `FuelScale`.
- **VPP fuel model active**: if the "no fuel rate" warning appears, the game doesn't run VPP's fuel model and the fallback is in use.
- **RPC_RefillItems peers**: which peers' postfixes fire, and whether it fires on spawn, respawn, teleport, cabin, mechanic, portal. Camping must not produce one. Also check whether the mechanic goes through `RPC_SetEquippedItems(alsoRefill=true)` *without* `RPC_RefillItems`; if so, fuel misses that refill.
- **Flooding block**: whether `EngineFloodingSystem` uses `EngineStartBlocked` at all, and whether drying/respawn clears it (look for "cleared the start block; re-asserted"). Whether `allowedFuelRatio = 0` alone keeps the engine off, and whether the game writes `allowedFuelRatio` itself.
- **StartEngine/ToggleEngine prefixes**: IL2CPP may inline these into callers, which would bypass the prefix. The per-frame enforce is the backstop. "StartEngine refused" lines show the prefix works.
- **Respawn**: whether it creates a new car object (= fresh full tank, logged as a new "tracking" line) or keeps the same one.
- **HUD placement**: whether `floodObject` or only the fill was cloned (see "Fuel bar added (cloned ...)"), the scale and position above the gear / H / L display, and whether the fill direction is right (log shows `fill Filled/Horizontal origin N`). Also check the icon shows, the bar hides with the HUD, and whether turning off the drivetrain display also hides the bar (it shares the cluster).

### Review fixes (same day, still v0.1.0)

- **Start-block ownership**: `Tank` records `ownBlock` when it writes `EngineStartBlocked = true` and forgets it if anything else clears the flag. Only an owned block is ever cleared. Refuelling while flooded sets `pendingUnblock`, and the tick clears our block once `IsFlooded` is false. A block that isn't ours (e.g. flooding's) is left alone. No flooding system means the flag is only cleared if we own it.
- **Fuel cut ownership**: `allowedFuelRatio` is restored to 1 only if we set it to 0 and it is still 0. On refill, the log says whether it stayed at 0 while empty or how often something reset it.
- **Despawn safety**: prefix on `NetworkedVehicleStatus.Despawned` drops the tracked car and its pending log entry, before Fusion invalidates its state. It does not call `Release`: the car is gone, and touching its networked state would throw. `Alive` also checks `Status.Object != null && Object.IsValid`. The OnUpdate tick is wrapped in try/catch: on an exception it logs once per failure and drops the car.
- **Authority**: `Adopt` requires `HasInputAuthority`. Without state authority an empty tank only refuses local `StartEngine`/`ToggleEngine` (logged once); no fuel cut, `StopEngine` or start block on that peer.
- `Tank.Release()` undoes our own fuel cut and block. It is called when the tracked car is replaced and when `Enabled` turns off at runtime.
- NaN/Infinity guard (`Tank.Sane`) on the burn rate, density and `FuelScale`.
- "fresh tank (no refill event)" is logged on adoption.
- Camping window: refills inside `CampManager.RepairPartyCars` are skipped and logged ("toolbox refilled by camping; not refuelling"). It uses a prefix/postfix flag, time-limited to 5 s. A refill that reaches us as a separate RPC *after* `RepairPartyCars` returns is not caught; the context ("CampManager.RepairPartyCars N s ago") will show it.
- `RPC_RefillItems` log adds `Runner.IsServer` / `IsClient` for this peer.
- `OwnsInventory` / `OwnsFlooding` / `OwnsIgnition` instead of a shared `Owns`. Inventory falls back to `vehicle.CachedFusionPlayer.CarInventory`; a loud warning if neither has one.
- Respawn postfix only counts when `playerRef` equals the car's `InputAuthority`.
- Empty enforcement also runs in a postfix on `CarIgnition.FixedUpdateNetwork` (local ignition only).
- Debug traces throttled to one line per 2 s, and ownership is checked before log strings are built.
- `Mod.Debug` renamed to `Mod.DebugLog`. `CarSpecs` uses exact name match only. `RPC_SetEquippedItems` logs one line.
- HUD:
  - The clone is made under an inactive staging object, so its components never Awake, then stripped and moved under the cluster.
  - Size = the template's own rect size with fixed centre anchors, times 0.6 in HUD root-canvas units (no longer derived from the flood panel's lossy scale, which may be mid-tween).
  - Pivot is set once.
  - The outline/background image is set to white.
- Pref description: "Consumption multiplier".
- Known limitation: if the game auto-starts the engine (e.g. on respawn) *before* `RPC_RefillItems` arrives, the start is refused while still empty. The engine then stays off after the refill until the player starts it.

### Test checklist

1. Set `DebugLog = true` in `[Fuel]` with the game closed, start a session.
2. Log on spawn: one "car '...': CarInfo ..." line per car, "tracking '...' (HUD car) ... fresh tank", "'...' engine: ..." line, "Fuel bar added (... size WxH, scale k ...)". There must be no "NO CarInventory" warning. Note CarInfo names and ids for the table; the keys need exact names.
3. HUD: bar above the gear display, right-aligned, about 60% of the flood bar's size, fuel-pump icon, full and white. Hide the HUD (photo mode or the hide option): the bar hides too.
4. Drive normally for a few minutes; read the 30 s burn lines (L/min, minutes per tank). Idle for one interval: the burn should be low but non-zero. Revving and climbing should burn more.
5. Optional: set `FuelScale` to 20 (game closed) to empty the tank quickly.
6. Below 20%: fill turns red, outline stays white.
7. At empty: "out of fuel" + "engine start blocked". The engine stops. Pressing start gives "StartEngine refused"/"ToggleEngine refused", and the engine stays off.
8. Camp while empty or low: "camping started" and "CampManager.RepairPartyCars" events. Expect no "refuelled" line. If RPC_RefillItems fires inside camping, expect "toolbox refilled by camping; not refuelling". A "refuelled" line whose context names RepairPartyCars means the refill arrived after the call returned (a gap in the camping window).
9. Flood the engine (drive into deep water) with fuel, let it dry: it must start normally. Then flood it while empty, let it dry: the engine must stay off (look for the re-assert line or "start block still set").
10. Teleport/respawn while empty: "RPC_RefillItems ... this peer: server=... ; context: ..." then "refuelled 0 -> 2 L". Then "allowedFuelRatio stayed at 0 / was reset N times" and "engine start unblocked (refuelled)" (or "not ours, left as is"). The engine starts. Note whether the game tried to auto-start the engine before the refill (a "StartEngine refused" line just before it).
11. Reach a cabin, the mechanic and a portal with a part-empty tank: each should log a refill with its context. Note any that don't.
12. Refuel while flooded and empty (respawn right after flooding, if possible): expect "engine still flooded, start block stays until it is dry", then later "engine start unblocked (engine dry after refuel)" or "start block cleared by the game while waiting". The engine must start once dry, never stay blocked.
13. Respawn and despawn: no repeating warnings. Expect a "despawned; fuel tracking stopped" line, then a new "tracking ... fresh tank (no refill event)" line if a new car object spawns.
14. Multiplayer, if available: host and client logs, and which peers log RPC_RefillItems (server/client flags). On a client, expect "no state authority on this peer; empty tank not enforced here" at empty. Check whether the bar works on the client.

## 2026-10-17: first in-game test (single player, Burro)

Confirmed in game:
- Burn works once the units are right. `fuelDensity` is kg/L (0.745), so `sensorFuelRate / density / 1000` = L/s. `CarIgnition.EngineRunning` was never seen true, so running now also accepts `Engine.sensorWorking`.
- Normal driving burns about 0.2 L/min, and about 0.4 L/min at high rpm. The 2 L test tank lasts about 9 minutes.
- The bar is a clone of `FloodPanelCentered`: a 100x100 square that fills vertically, not a horizontal bar. It needs a real sprite on the fill, because Unity ignores Filled mode on an Image with no sprite. Now it's a green fill (red when low) over a faint white square, with the jerry can on top.
- Refills happen on respawn/teleport, at Cabin1 and Cabin4, and at the Merchant (the mechanic).
- At empty, the engine stops and restarts are refused. `allowedFuelRatio` stayed at 0 (VPP doesn't overwrite it).
- Flooding while empty, then drying out: `SetFlooded(false)` cleared the start block, and the mod re-asserted it. Refuelling while flooded left the block until the engine was dry.
- No exceptions in the log.

Not yet tested: camping while low, portals, multiplayer.

## 2026-10-17: v0.2.0 (phases 2 and 3: saving, jerry can)

### Method bodies checked

`build/ref/asm` has signatures only, so the key methods were read from `GameAssembly.dll`. Cpp2IL 2022.1.0-pre-release.21 (Linux build) produced an ISIL dump and DLLs with `[Address]`/`[FieldOffset]` attributes; `objdump -M intel` on the method ranges, with call targets and field offsets mapped back to names, did the rest. These tools were run from a scratch directory and are not in the repo. Findings:
- `OverlandSaver.SaveGame`: returns early if `SuppressedForBenchmark` or there is no `CurrentProfileSave`. Otherwise sets `Header.LastPlayed = Now` and writes `Path.Combine(FileSystemUtils.SaveLocation(), Profile.Name + ...)` (on disk: `Documents/My Games/over the hill/Saves/Player1.json`). So `Profile.Name` is the save slot.
- `SetCurrentGameSave` only sets the property. It is called from `StateProfileManagementSystem.Open` (after `LoadGame`) and from `StartNewSave` (`ProfileSave.StartSave` -> `SetCurrentGameSave` -> `SaveGame`). Nothing else sets `CurrentProfileSave`.
- `InventoryItemJsonConverter`, `VehicleItemJsonConverter` and `PlayerInventoryJsonConverter.ReadJson` build items straight from the JSON with no catalog lookup. So an unknown id would probably load without error, and then reach every consumer of the inventory (merchant, migrations, UI). Only some of those are known to handle a failed lookup.
- `ContextUIAvailableToolsPanel.Initialize(playerRef)`: gets the player's `CarInventory.GetAvailableItems()` and the slot count (`VehicleCustomizationController.HasUpgradeOfTypeMounted(6, ...)`). Then, per icon:
  - locked (index past the slot count): lock sprite on `StatusImage`;
  - else, if the index is below the number of available items and `ItemScriptableObjectLookup` succeeds: `MainImage.sprite = SO.Icon`, and the click handler is added if `playerRef` is local;
  - else empty: no sprite, alpha 0, status off.
  Then horizontal navigation is set up. A failed lookup shows as an empty slot.
- `ButtonOnOnPointerClickOrSubmit`: `buttonSelectorToScriptableObject[button]` -> the matching `usableItemOperations` pair -> `PerformOperation(op, id)`. No charge check.
- `PerformOperation`: `PopState`, then a jump table over 0..6. Case 0 (`OpenRepairingUI`) is `PushState(new StateCarRepair(carDamageSystem, itemID))`, with the range taken from `carDamageSystem.maxRepairRange`. Values above 6 only pop the state.
- `StateCarRepair.SetupCarRepairButtons`: one button per active player within `maxRepairRange` (the local player included), all interactable. Clicking one calls `HealPlayer(player, theirDamageSystem)`. `HealPlayer` does `RPC_HealPlayer(...)`, then `PlayerCarInventory(local).RPC_ConsumeItem(repairKitID)`, an achievement, then `PopState`. `repairKitID` is used for nothing else.
- `OverlandNetworkRunner.OnReliableDataReceived`: key 1 -> `OnRuntimePlayerReceived`, key 2 -> a UTF-8 string handler, any other key -> `throw ArgumentOutOfRangeException`.

### Implemented

- **Saving** (`Store.cs`, `SavePatches.cs`):
  - Sidecar file `UserData/Fuel/<Profile.Name>.json` (invalid filename characters become `_`), written with `System.Text.Json` (`Utf8JsonWriter` / `JsonDocument`), which ships with the .NET 6 runtime MelonLoader uses. No NuGet packages.
  - Per car, keyed by `CarInfo.ItemId`: `fraction` (the value that is used), plus `litres`, `capacity` and `car` name for reading the file, and `cans`.
  - Loaded in a postfix on `SetCurrentGameSave`, which always rereads from disk: what the game didn't save, fuel doesn't keep either. During `StartNewSave` the selection counts as new: no cars, and the file is overwritten, so a new save never inherits an old file of the same name.
  - Written in a postfix on `SaveGame` (manual saves and autosaves). The tracked car's level goes into the store first, and the file is written only if the text changed. It is also written when the tracked car despawns (quit to menu) and in `OnApplicationQuit`, so quitting mid-trip never hands back burnt fuel.
  - Writes go to a temp file that is then moved over the old one. An unreadable file is copied to `.bad`, and tanks start full.
  - Fuel is never written under a different save's name: `SaveGame` checks that the store's profile is still current, and each tank remembers the profile it was restored from.
  - Adoption: once `CarInfo` is known, the tank takes the saved fraction and can charges. With no entry, the tank and the can start full. A saved 0 runs the normal empty path (fuel cut, `StopEngine`, start block).
  - Car swaps: a car's level goes back into the store whenever it stops being tracked (replaced, despawned, disabled). Once a second, the tracked car's `VehicleCustomizationController.CurrentCarInfo` is also checked; if the model changes on the same network object, the old level is stored and the new car's is restored.
  - Respawn safety: if a toolbox refill reaches an input-authority inventory before our car is tracked, and our previous car despawned less than 15 s earlier, it is applied when the new car is adopted (within 10 s). Without this, a respawn that replaces the car object would bring an empty car back empty. Without a recent despawn it is only logged, so a refill at session start (if there is one) can't top up a loaded tank.
- **Jerry can** (`JerryCan.cs`):
  - Granted free on every car, 1 charge (`JerryCan.Charges`). Refilled with the toolbox (`RPC_RefillItems` on the local car), except inside `CampManager.RepairPartyCars`; otherwise camping would refuel through the can. Charges are saved in the sidecar.
  - UI: postfix on `ContextUIAvailableToolsPanel.Initialize`, for the local player's panel only. The can goes into the first free, unlocked icon, shown with the embedded `fuel-icon.png` and the colour of a filled icon. It **takes a slot**: it shows only while the car has fewer equipped tools (`GetEquippedItems`, non-zero) than unlocked slots. Navigation is rebuilt with `UIUtilities.SetHorizontalNavigation` so a controller can reach it.
  - Our click handler is added to that icon's `UIButtonSelector.OnPointerClickOrSubmit`. A prefix on `Initialize` removes it, and clears the icon, before the game rebuilds the icons, so a game tool never inherits it.
  - Use:
    1. A click works only while `StateSelectTool` is the top state, because `PerformOperation` pops the top state. It calls the game's own `PerformOperation(OpenRepairingUI, 0xF0E1CA11)`, which opens the repair car picker (every car in range, other players included) with our id as `repairKitID`.
    2. A prefix on `StateCarRepair.HealPlayer` catches our id. It never lets the original run (no heal, no `RPC_ConsumeItem`), refuels the chosen car, uses a charge, and pops the picker.
    3. If the chosen car's tank is already full, the can is kept.
  - Safety checks before the can is first shown: the `HealPlayer` prefix is applied (`Harmony.GetPatchInfo`), and `ItemScriptableObjectLookup(0xF0E1CA11)` finds no game item.
  - No `InventoryItemScriptableObject`, catalog entry, `ToolsLists` entry, `PlayerInventory.AddItem`, `ProfileSaveMigration` call or `CarInventory` slot is created. The id exists only as `StateCarRepair.repairKitID` on this machine.
- Log line format change: refills and restores now log as `'<car>' <what>: <before> -> <after> L`, e.g. `'X' refuelled (toolbox refill; context ...): 0 -> 2 L`.
- Version 0.2.0. Builds with 0 warnings, 0 errors.

### Design choices

- **Save safety**: the can is never put into the profile inventory or tool lists, so it can't be serialised. The converters don't validate ids (see above), so an unknown id would load silently and then meet code whose handling of a failed lookup isn't known. Rather than strip it around `SaveGame`, it is kept out of every game structure. Removing the mod leaves the game save exactly as the game wrote it; the `UserData/Fuel` files are simply ignored.
- **Multiplayer state**: nothing networked is touched. `CarInventory.EquippedItems` / `AvailableItems` never contain the id, so unmodded peers can't receive it.
- **Delivery to another player's car: not implemented**, by choice. Fuel lives with each car's driver (`Tank.Local` on their machine), not with the host. Options checked:
  - Custom Fusion RPC: needs IL weaving.
  - `NetworkRunner.SendReliableDataToPlayer` + prefix on `OverlandNetworkRunner.OnReliableDataReceived`: works between modded peers, but an unmodded receiver's handler throws `ArgumentOutOfRangeException` on any key other than 1 or 2, in Fusion's receive path. Whether Fusion catches it is not known.
  - Piggybacking on `RPC_HealPlayer` with a marker amount or reason: unmodded peers would run `HealDamage` with it.
  - `RPC_RefillItems` on the target: works, but it gives the target a free toolbox refill.
  None of these is safe with an unmodded peer, and there is no safe way to detect the mod on another peer. So choosing another player's car logs `can't refuel '<car>' ... can kept` and keeps the charge. If "everyone needs the mod" (design.md) is enforced later, reliable data with our own key is the simplest route.
- The car picker shows the cars' health bars, not fuel (only the local car's fuel is known).
- No new preferences; README settings unchanged.

### Assumptions to verify in game

- `SetCurrentGameSave` fires once per load or new save, before the car spawns. Look for "save '<name>': fuel loaded / no fuel file yet / new save".
- Whether the game fires `RPC_RefillItems` at session start. If it does, either "RPC_RefillItems before our car is tracked (no recent despawn, ignored)" appears, or, if our car is already tracked, a "refuelled" line right after loading. The latter would be a free fill on load and needs a guard.
- Whether respawn/teleport replaces the car object: "despawned" followed by a new "tracking" line. If it does, check that the refill still reaches the new car ("refill that arrived before tracking" or a normal "refuelled").
- Whether a car swap changes `CurrentCarInfo` on the same object ("car changed from ...") or spawns a new car.
- `StateSelectTool` is the top state when the tools panel is clicked; otherwise "clicked outside the tools menu; ignored" appears and the can does nothing.
- The free-slot test (equipped < unlocked) matches what the panel shows. "jerry can not shown: no free tool slot (N equipped, M unlocked)" is logged once when it doesn't fit.
- The icon colour and size match the other tools, and controller navigation reaches the can (no "could not set controller navigation" warning).
- After the picker closes, the tools menu isn't left in a broken state: `PopState` is called once by `PerformOperation` and once by our prefix, the same as the game's own repair flow.
- Single player: the picker lists only the local car.

### Review fixes (same day, still v0.2.0)

- **Refill before the model is known** (B1): `Refill` / `RefillCans` on a tracked tank whose `CarInfo` isn't resolved yet set flags. `Restore` loads the saved entry, then applies those refills again, so a respawn refill can't be overwritten by a saved 0. The flags are cleared on every `Restore`.
- **Respawn window across loads** (B2): `Store.Select` resets the despawn and early-refill times, so a quit-to-menu despawn never turns a session-start refill into a free fill.
- `startingNew` is now time-limited (5 s from the `StartNewSave` prefix, cleared in the postfix) instead of a plain bool. A throw inside `StartNewSave` can't leave it set and wipe a later load. I didn't use a Harmony finalizer because I couldn't confirm Il2CppInterop runs them.
- `Store.Select` with a car already tracked (`Tank.Reselect`):
  - if the car was restored from a *different* save, it is restored again from the new one;
  - if it is the *same* save, the live level is kept, because it is newer than the file and rereading would hand back burnt fuel.
- The "gone" path in `Mod.OnUpdate` (car lost without a despawn call) now does what a despawn does: store the level, record the despawn time, write the file (`Tank.Lost`).
- **Car picker**: postfix on `StateCarRepair.SetupCarRepairButtons` for our id. A button counts as the local car when its `WorldTrackingRectTransform.transformToTrack` is the local player's `NetworkObject` transform; the game sets it from `TryGetPlayerObject(player).transform`. Every other in-range car's button is made non-interactable, and one line per picker says how many. If the local car's button can't be identified, nothing is disabled: the `HealPlayer` prefix still refuses other cars. The game sets every in-range button interactable each time it builds the picker, so real repairs are unaffected.
- **Stale handlers**: covered both ways.
  - From the disassembly: `Initialize` removes the panel's own handler from every icon and resets `buttonSelectorToScriptableObject[button]` for every icon (to the item, or null for empty and locked slots) before deciding what the icon shows.
  - Our handler is removed in our `Initialize` prefix.
  - The can now also goes only into icons at an index at or beyond `GetAvailableItems().Count`, after every icon the game fills.
- Sidecar parsing checks each field's `ValueKind`. A car without a valid `fraction` is skipped with a warning (that car starts full). Other bad fields fall back to their defaults. Only a root that isn't a JSON object, or invalid JSON, discards the file.
- When the can is used up while a panel is open, that panel's navigation is rebuilt without it.
- Jerry can UI warnings: once per distinct message instead of once per session.
- Known limits of the save key:
  - Two cars of the same model in one save share one entry (key = `CarInfo.ItemId`); it's not known whether the game allows that.
  - A car without a `CarInfo` (`ItemId` 0) is never saved and starts full.
  - Profile names that differ only in characters invalid in filenames map to the same file. The `profile` field inside is compared on load, and a mismatch is logged but the file is still used.

### Test checklist

Phase 2 (saving), with `DebugLog = true`:
1. Load a save: "save '<name>': ..." line, then "tracking ...", "CarInfo '...' id N", then "full tank (nothing saved for this car)" on first use. Check that `UserData/Fuel/<name>.json` appears after the first autosave or manual save.
2. Drive until about half full, save from the menu, quit to the main menu, load again: "restored from save '<name>' (≈50%, 1 jerry can)", and the bar is at the same level.
3. Drive a bit more without saving, quit the game outright (Alt-F4 or menu quit), start again and load: the level is the one at quit, not higher.
4. Run the tank dry, save, reload: "restored ... (0%)", "out of fuel", "engine start blocked". The engine won't start until a respawn/teleport refill.
5. Respawn/teleport after that: refuelled to full. If a new car object spawns, expect "despawned", a new "tracking" line, and one of these:
   - "refill that arrived before tracking";
   - "toolbox refill from before the car model was known" (after the "restored ... (0%)" line);
   - a normal "refuelled" line.
   The car must never stay empty after a respawn.
5a. Quit to the menu and reload within 15 s: no "refuelled" line until a real refill event.
6. Swap to a different car (garage/merchant), drive, swap back: each car shows its own level ("car changed from ..." or a new "tracking" line; `cars` in the file has both ids).
7. Start a new save with the same name as an old one (or delete and recreate): "new save, all tanks start full", and the file's `cars` is empty.
8. Switch profiles: each profile has its own file and its own levels.
9. Look in `Player.log` / `MelonLoader/Latest.log` for no repeated warnings, and no "fuel file unreadable".

Phase 3 (jerry can):
10. Leave at least one tool slot empty at the merchant (fewer tools equipped than unlocked slots). Open the tools menu: "jerry can ready (id ... is not a game item)", and the fuel-pump icon in a free slot. With every slot used: "jerry can not shown: no free tool slot".
11. Burn some fuel, open the tools menu and pick the can: "jerry can: choose a car", then the repair picker. Pick your own car: "refuelled (jerry can)", "jerry can used on '...', 0 left". The bar is full, the can is gone from the panel, and the car's health is unchanged (no repair, no repair kit used).
12. With a full tank: picking your car gives "tank already full; can kept".
13. Use the can, then reach a cabin, the mechanic or respawn: "jerry can refilled", and the can is back in the panel. Camping must not bring it back.
14. Save and reload with the can used: it stays used (`cans: 0` in the file).
15. Controller: the can can be selected with left/right in the tools panel.
16. Picker: "jerry can picker: local car found, N other car(s) disabled". With other cars in range, only your car's button is clickable. Afterwards, a real repair kit can still repair the other cars.
17. Multiplayer, if available: if the picker reported "NOT found", the other cars stay clickable; picking one gives "can't refuel '<car>' ... can kept". Nothing changes for them, and their game must not misbehave (the can is local-only).

## 2026-10-18: jerry can as a real tool

- The user wants the can equipped at the mechanic like any other tool, not the Side_Jerrycan accessory (the other Nexus fuel mod's approach) and not a free extra slot.
- Shop code, from disassembly:
  - `StateMerchantMenu.InitializeToolsScreen` lists `CarInfo.ToolsLists.VehicleTools` (Type == UsableItem, demo and DLC filters). A tool is locked unless the player owns it: SetupAvailableToolIcon finds it in `PlayerInventory.GetItems(UsableItem)`, and Count minus the number equipped is what's left. Equipped slots come from `VehicleItem.ToolsetIDs`, and `AddTool` writes the id into the first free slot.
  - `CarInventory.Initialize` reads the current VehicleItem's ToolsetIDs and calls `RPC_SetEquippedItems` and then `RPC_RefillItems`.
  - `ContextUIAvailableToolsPanel.Initialize` builds icons from `GetAvailableItems` plus the catalog lookup. A click maps the item to an operation through `usableItemOperations`.
  - `OverlandSaver.SaveGame` is the only save writer. It serialises with Newtonsoft and then calls SaveJson.
- Implementation:
  - The item is a runtime InventoryItemScriptableObject, id 0xF0E1CA11, registered in the catalog and added to every ToolsLists.
  - It uses the repair kit's operation (OpenRepairingUI). The HealPlayer prefix refuels and then calls `AskForItemConsumption`.
  - Its name and description are empty LocalizedStrings; a GetLocalizedString() prefix answers for them.
  - Charges are the game's own. Tank.Cans and the sidecar "cans" field are gone.
  - The SaveGame prefix strips the id from ToolsetIDs and the postfix puts it back. The slots are kept in the sidecar under "jerryCanSlots" and restored at CarInventory.Initialize and when the tools screen opens.
- Untested in game.
- First in-game test: the can was listed but locked. Fixed by adding an owned InventoryItem (Count 1) to PlayerInventory.Items, which is also stripped from the save.
- Reload bug: saved can slots went back only when the toolbox was first opened (about 10 s after spawn). By then `FusionPlayer.Spawned` had already sent the car's ToolsetIDs (`RPC_SetEquippedItems`, then `RPC_RefillItems`), so the car had no cans until the player re-equipped them. Fix:
  - Prepare in a `FusionPlayer.Spawned` prefix.
  - A `RPC_SetEquippedItems` prefix (input-authority car only) that adds the can to the sent array wherever the current VehicleItem holds it.

## 2026-10-18: multiplayer jerry can (Net.cs)

- The user wants the mod to work in multiplayer. Since an equipped can sits in networked tool slots, everyone needs the mod anyway, so the reliable-data route from v0.2.0 is now used.
- Key: `ReliableKey.FromInts(0x4F544846, 0x75656C21)`. A message is 21 bytes: version, type, target raw, origin raw, float value, flag byte.
- Status (type 1): each modded player sends their tank fraction every 2 s. A player is "known" for 8 s after their last status.
- Refuel (type 2): litres sent to the target's driver, who calls AddFuel.
- Routing: a client sends to the server. The host applies what is addressed to it and forwards the rest: status to every other known player, refuel to its target. The host drops any message whose claimed origin isn't the sender.
- Nothing is ever sent to a player with no status, because an unmodded `OnReliableDataReceived` throws on unknown keys.
- The receive prefix (on a method taking `ReadOnlySpan<byte>`) is applied by hand in `Net.Patch`, so a failure there only disables networking. The startup log says "fuel network ready" or "Fuel network unavailable".
- Picker: buttons of cars with known fuel (our own plus known players') show their fuel; the others are disabled. `HealPlayer(playerToHeal, …)` routes the can: the local car gets AddFuel, another car gets `Net.SendRefuel`. The can is consumed only when the send went out.
- Untested: needs two players with the mod.
- First online test (2026-10-18, the user hosting, a Windows friend as client):
  - The client tracked its own car with input but not state authority (`state=False input=True`), burned fuel and refuelled normally.
  - The host never got a status from the client, so the client's car was greyed out in the picker. Neither log has errors or network warnings.
  - v0.3.1 adds always-on diagnostics:
    - "fuel status: sending …" when a game starts sending, or "fuel status not sent: <reason>".
    - The first 10 network messages received, with their keys.
    - A host self-test, where the host sends a message to itself.
- The v0.3.1 logs showed the cause:
  - The client's status messages reached the host every 2 s (21 bytes), but with key `00000046 00000000 <garbage>`. Only the first byte of Key0 survived. The host self-test arrived the same way.
  - The interop `ReliableKey` struct is one byte, because its 16-byte fixed buffer collapses to `FixedElementField`, so the interop wrapper sends only one byte of the key.
  - v0.3.2 calls `SendReliableDataToPlayer` / `ToServer` through `il2cpp_runtime_invoke`, with our own 16-byte key buffer and span struct.
- First working online session (v0.3.2): the self-test passed, the client's status arrived, and cans worked in both directions (`jerry can from player 2 (+1.75 L)`).
- The user reported a short freeze and garbled sound around a jerry can use on their own empty car (15:35:55). The log has no errors or warnings there; the last lines before it are the refuel from 0, "allowedFuelRatio stayed at 0", and the start block lifted.
  - One suspect: the refuel set `allowedFuelRatio = 1` instead of the value it had before the cut. The value from before the cut is now saved and put back, and logged.
  - Not reproduced yet.
