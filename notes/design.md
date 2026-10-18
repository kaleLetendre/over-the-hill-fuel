# Fuel: design decisions

Decided 2026-10-17.

## Rules

- **Consumption** depends on engine load and RPM, plus a small idle burn. Distance alone doesn't fit off-road driving.
- **Tank size and thirst** are per car, based on the real car each one is modelled on. Real numbers only set the *relative* differences between cars.
- **Scale factor.** The map is much smaller than real distances, so real tanks would never run low. A global factor divides the tanks so that a full tank covers a chosen number of legs between refill points. It gets set by measuring in-game distances.
- **Empty tank:** the engine turns off and won't restart. The way out is the game's existing teleport/respawn.
- **Flooding and fuel are independent.** Drying out after a flood must not clear the start block while the tank is empty. The engine can start only when it's both dry and fuelled.
- **Refill rule:** fuel refills to full only on the events that refill the toolbox items (`CarInventory.RPC_RefillItems`), so it follows the game's own toolbox rules. Camping does **not** refuel. The mechanic, cabins and portals do, as long as they refill the toolbox.
- **Jerry can:**
  - It's a toolbox item bought and equipped at the mechanic like any other tool, taking a tool slot. Not the game's Side_Jerrycan accessory. (Decided 2026-10-18.)
  - Each can adds a fixed amount: half a Burro tank (35 L real, divided by TankRatio), the same for every car. Up to 2 per car. (2026-10-18)
  - In the car picker, your car's button shows the fuel level and jerry-can icon instead of damage.
  - It can be used on another player's car.
- **Weight change:** dropped for now. It may not suit an arcade game.

## Persistence and multiplayer

- The fuel level survives save, load and quitting mid-trip, so a reload doesn't give a free fill-up.
- The host (Photon Fusion state authority) tracks fuel and clients receive the synced value. Everyone in a session needs the mod.

## UI

- Reuse the game's engine-flooding panel (EngineFloodingUIPanel) instead of a health-bar-style bar.
  - Reverse its direction: full when the tank is full, empty when it's out.
  - Scale it to about 60% of the flooding panel's size.
  - Place it on the right, above the shifter (the gear / H / L cluster).
  - Swap the engine icon for a fuel-pump icon. Unity can't load SVG at runtime, so it ships as a PNG: white on transparent, about 128 px square.
- The bar is white normally. Below the low-fuel threshold it turns red inside, keeping its white outline.
- It blinks or sounds a warning when fuel is low.

## Settings

| Setting | Purpose |
|---|---|
| Enabled | Turns fuel on or off |
| Consumption multiplier | Easy, real or hard |
| Low-fuel threshold | Where the bar turns red |
| Empty behaviour | Stall (default) or limp mode |

## Interactions

- **Extra Gears:** if burn depends on RPM, the taller 3rd and 4th gears use less fuel at cruise.

## Open

- Each car's real-world basis, tank size and economy (research).
- The scale factor (needs in-game distance measurements).
- The jerry can's capacity once it stops being a full refill.
- Jerry can on other players' cars: sent to their driver over Fusion reliable data (Net.cs); both players need the mod. Untested with two players.
