# Fuel: game hook map

Surveyed 2026-10-17 from the Il2CppInterop stubs in
`~/over-the-hill-extra-gears/build/ref/asm` (also linked as `build/asm`).

The stubs have signatures and fields but no method bodies. So any statement here about *what a method does* is inferred from names, types and caller counts (`[cc=N]`), and needs to be confirmed at runtime.

## Consumption

- VPP already models fuel use.
  - `Engine.sensorFuelRate` comes from `maxFuelPerRev`, rpm and load.
  - The game replicates it as `NetworkedVehicleStatus.EngineFuelRate`.
  - Consumption only needs to integrate this, times our scale factor.
- Other values to read:
  - `Engine.sensorRpm`, `sensorLoad` and `sensorWorking`.
  - `NetworkedVehicleStatus.EngineRunning` and `ThrottleValue`.
- Tick:
  - Use a postfix on `CarIgnition.FixedUpdateNetwork`, or our own component.
  - Run it only when `HasStateAuthority` is set and `Runner.IsForward` is true.
  - The game's per-car accumulator `Odometer.FixedUpdate` (writing `VehicleItem.MetersTravelled`) is a precedent.

## Engine kill

- `CarIgnition` exposes:
  - `StartEngine()`, `StopEngine()` and `ToggleEngine()`.
  - Networked `EngineRunning`, plus `EngineStartBlocked` (public setter).
- `Engine.allowedFuelRatio` is VPP's own fuel cut: 0 stops the engine, values from 0 to 1 make it sputter.
- Template: `EngineFloodingSystem` probably does `StopEngine` plus `EngineStartBlocked = true`, and clears the block on drying or respawn.
  - So flooding may clear our block too.
  - Prefix `CarIgnition.StartEngine` to return false while the tank is empty.
  - Re-assert the block after `EngineFloodingSystem.SetFlooded` and `ResetFloodingState`.

## Refill moments

- **Single best signal:** the static `CarDamageSystem.VehicleHealChanged(PlayerRef, PlayerRef, CarDamageSystem, HealthChangingReason)`.
  - Its reasons are `HealedByPOI`, `HealedByCamping`, `HealedByTeleporting`, `HealedBySelf` and `HealedByOther`.
- **Cabins and other points of interest:**
  - The static event `PointOfInterest.POIReached`, filtered on `PointOfInterestType.Cabin`.
  - The game's own refills run here: `CarDamageSystem.OnPOIReached` heals the car and `CarInventory.OnPOIReached` refills tools (`AskForItemRefill` → `RPC_RefillItems`).
- **Camping:** postfix `CampManager.RepairPartyCars()`, or use the static `OnBeginCamping` / `CabinCampingStarted` events.
- **Mechanic:** the mechanic is the merchant.
  - Hooks: `MerchantTrigger.InteractPerformed`, `StateMerchantEnter.Open()`, or `POIReached` with `Info is MerchantInfo`.
  - `MechanicSleepManager.NotifyInteracted()` fires when you talk to the mechanic.
- **Portal:** the static `Portal.PortalFinished` event, or `PortalNetworkManager.OnMapLoadedAndServerReady()`.
- **Way out when empty:**
  - `SpawnManager.StateAuthorityPlayerRespawned` fires on respawn; the flooding system already resets on this event.
  - `OverlandSessionPlayers.PlayerTeleported` fires on teleport.
  - Or use `VehicleHealChanged` with `HealedByTeleporting`.

## Jerry can (toolbox item)

The game models toolbox items like this:

- **Definition:** `InventoryItemScriptableObject` with `ItemId`, `Name`, `Icon` and `Type = UsableItem`, registered in `PlayerInventoryManager.InventoryItemsCatalog`. The catalog's `IdToSO`, `IdToName` and `NameToId` dictionaries are all writable.
- **Per-car availability:** `CarInfo.ToolsLists.VehicleTools`. Ownership and count per car are in `VehicleSpecificItem.VehicleStates`.
- **Charges for the current trip:** `CarInventory` (networked).
  - `EquippedItems` holds the slots and `AvailableItems` holds the charges left.
  - `AskForItemConsumption(id)` → `RPC_ConsumeItem` uses a charge.
  - `RPC_RefillItems` restores charges.
- **Tools UI:** `ContextUIAvailableToolsPanel`.
  - `usableItemOperations` maps each item to a `UsableItemsOperation` enum value.
  - `PerformOperation(op, itemId)` runs the chosen item.
  - Plan: add a pair with a cast value such as `(UsableItemsOperation)100`, and prefix `PerformOperation` to handle it.
- **Targeting another player's car:** copy `StateCarRepair` (its `SetupCarRepairButtons`, `maxRepairRange` and `HealPlayer`).
  - Healing goes through `CarDamageSystem.RPC_HealPlayer` to the target car's owner.
  - Fuel needs its own message routed the same way.

## Save and load

- The save format has no per-car state except `VehicleItem.MetersTravelled` and the tool lists. Damage and flooding are runtime-only, and custom JSON fields would likely be dropped by the game's converters.
- Plan: a sidecar JSON file keyed by profile name plus `CarInfo.ItemId`.
  - Write it in a postfix on `OverlandSaver.SaveGame()` [cc=22], which also covers autosaves.
  - Read it in a postfix on `OverlandSaver.SetCurrentGameSave` / `LoadGame`.
  - Apply it on `NetworkedVehicleStatus.SpawnedCallback` / `FusionPlayer.PlayerSpawned`.

## HUD

- `HUDManager.Instance` holds `engineRPMDisplay`, `drivingModeSelector`, `engineFloodingUIPanel` and `carHealthUI`.
- `SetupCarRelatedHUD(NetworkedVehicleStatus)` runs when a car spawns. Postfix it to build the fuel bar.
- The bottom-right cluster is `ContextUIDrivingModeSelector.GearDisplay`, with the gear text and the 4H / 4L selectors, plus `ContextUIEngineRPMDisplay` (`Pips`).
- `EngineFloodingUIPanel` follows the car in world space (`WorldTrackingRectTransform`). Its bar is a `ContextUIFill` (`floodFill`).
  - Clone the `ContextUIFill`, reparent it next to `GearDisplay`, and drop the world tracking.
  - Fill it with `SetValue(fuel, capacity)` and `SetFillColor` for the red state.

## Cars

- `CarAudioManager.VehicleType` lists:
  - Protector, Bull, TrailBurner, DingDing, Burro, Timberbox, Trailrat, Brick
  - RustOx, GrandWoodman, BlastWagon, Mammoth, Panda, Boar, Marshal, Polyana
  - Lynx, Rancho, Wombat, Tinny, Cumbre, Roamer
- Some may be DLC-only or unused.
- Key the fuel spec table by `CarInfo.name` or `ItemId`.
  - Get the car's `CarInfo` from `VehicleCustomizationController.CurrentCarInfo` or `Odometer.carInfo`.
  - Fall back to `CarInfo.carCategorySetup` (`VehicleControllerSetup`, which has `mass` and `engineTiers`).
  - The engine tier (`EngineUpgradeEffect.tier`) could scale thirst.

## Multiplayer

- Each car's fuel lives with that car's Fusion state authority.
- Using a jerry can on someone else's car sends a request to that car's owner. This mirrors `CarDamageSystem.RPC_HealPlayer`.
