# Tank sizes (for the Nexus description)

Source of truth: `src/CarSpecs.cs`. Real models come from the OTH.RealVehicleNames mod. Stock tank sizes are from memory and still need checking; Rust Ox and Boar are rough guesses.

## Ratio explanation (draft)

The map is much smaller than real-world distances, so a real-sized tank would never run low. Each car gets its real-world stock tank, divided by **TankRatio** (default 20, in `UserData/MelonPreferences.cfg` under `[Fuel]`). A 70 L tank holds 3.5 L in game. Raise the ratio for smaller tanks and more fuel stops, lower it for longer range. How fast a car burns fuel comes from its engine in the game, so thirsty engines drain faster on their own.

## Table

| In-game car | Real car | Stock tank | In game (ratio 20) |
|---|---|---|---|
| Mammoth | Mercedes-Benz Unimog 406 | 90 L | 4.5 L |
| Marshal | Land Rover 101 FC | 109 L | 5.45 L |
| Lynx | Volkswagen Iltis | 83 L | 4.15 L |
| Wombat | Mercedes-Benz W460 SWB | 83 L | 4.15 L |
| Brick | Toyota Land Cruiser 70 SWB | 90 L | 4.5 L |
| Bull | Ford Bronco Mk1 | 48 L | 2.4 L |
| Ding | Toyota Land Cruiser FJ40 | 63 L | 3.15 L |
| Polyana | Lada Niva | 42 L | 2.1 L |
| Protector | Land Rover Defender 90 | 55 L | 2.75 L |
| Tinny | Land Rover Series I SWB | 45 L | 2.25 L |
| Trailrat | International Scout 80 | 42 L | 2.1 L |
| Cumbre | Chevrolet K10 | 76 L | 3.8 L |
| Grand Woodman | Jeep Grand Wagoneer | 77 L | 3.85 L |
| Roamer | Ford Explorer Mk1 | 73 L | 3.65 L |
| Trail Burner | Chevrolet K5 Blazer | 95 L | 4.75 L |
| Woodman | Jeep Wagoneer | 76 L | 3.8 L |
| Rust Ox | Mercedes-Benz T2 camper | 70 L | 3.5 L |
| Blast Wagon | Dodge Power Wagon | 68 L | 3.4 L |
| Boar | Volvo TP21 Sugga | 60 L | 3.0 L |
| Burro | Toyota Land Cruiser FJ45 | 70 L | 3.5 L |
| Timberbox | Dodge Power Wagon woodie | 68 L | 3.4 L |
| Grizzly | Fiat Panda 4x4 | 35 L | 1.75 L |
| Rancho | Matra-Simca Rancho | 56 L | 2.8 L |
