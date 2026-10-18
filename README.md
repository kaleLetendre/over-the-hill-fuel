# Over the Hill: Fuel

A MelonLoader mod that adds **fuel** to *Over the Hill*.

- Cars burn fuel while the engine runs. How much depends on revs and load, from the game's own engine model, so a hard climb uses more than cruising.
- Each car's tank is based on the real car it's modelled on (table below), scaled down to the map.
- **Empty tank:** the engine dies and won't restart. Teleport or respawn to get out.
- **Refuelling:** the tank fills whenever the game refills your toolbox: the mechanic, cabins, portals, teleport and respawn. Camping does **not** refuel.
- **Jerry can:** a new tool. Equip it at the mechanic like any other tool, up to 2 per car. Each can adds half a Burro tank, to your car or another player's. It refills with the rest of the toolbox.
- **Fuel bar:** sits on the right above the gear display. Green, or red when low.
- **Saving:** fuel levels and equipped cans are saved with your game. The game's own save file never holds anything from the mod, so removing the mod leaves a save the game can read.
- **Flooding** and fuel are separate. Drying out a flooded engine doesn't let an empty one start.

**Status:** tested on Linux (Proton), single player.

> **Untested:** Windows and multiplayer. In multiplayer, **every player needs the mod**. [Reports welcome](https://github.com/kaleLetendre/over-the-hill-fuel/issues).

---

## Tank sizes

The map is much smaller than real-world distances, so a real-sized tank would never run low. Each car gets its real car's stock tank, divided by **TankRatio** (default 20, see [Settings](#settings)). So a 70 L tank holds 3.5 L in the game. Raise the ratio for smaller tanks and more fuel stops, or lower it for more range.

How fast a car burns fuel comes from its engine in the game, so thirsty engines drain faster on their own. A jerry can adds half a Burro tank (35 L real, 1.75 L at ratio 20), the same for every car.

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

The real models come from the [Real Vehicle Names](https://www.nexusmods.com/overthehill) mod. Tank sizes are stock figures, for the base model where a car had options. A car the mod doesn't recognise gets 60 L (3 L in the game).

---

## What you need

- [ ] *Over the Hill* on Steam
- [ ] About 15 minutes, mostly waiting for the game's first start with MelonLoader

Follow the guide for your system: [Linux](#setup-on-linux-steam--proton) or [Windows](#setup-on-windows).

**Already have MelonLoader v0.7.3 in the game** (for example for another mod)? Skip straight to [Install the mod](#step-5-install-the-mod).

---

## Setup on Linux (Steam + Proton)

### Step 1: Open the game folder

In Steam, right-click **Over the Hill** → **Manage** → **Browse local files**. A file manager opens on a folder containing `over the hill.exe`. Keep it open; the next steps all happen in this folder.

### Step 2: Install MelonLoader (the mod loader)

1. Download **MelonLoader v0.7.3**: [MelonLoader.x64.zip](https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip). It has to be this version; newer or older ones may not work with this mod.
2. Open the ZIP and extract **everything** into the game folder from Step 1.

Check: the game folder now has a `version.dll` file and a `MelonLoader` folder, right next to `over the hill.exe`:

    MelonLoader/
    over the hill_Data/
    over the hill.exe
    UnityPlayer.dll
    version.dll
    ...

If you see a `MelonLoader.x64` folder instead, the files were extracted one level too deep. Move its contents up into the game folder.

### Step 3: Tell Proton to load MelonLoader

1. In Steam, right-click **Over the Hill** → **Properties** → **General**.
2. In **Launch Options**, paste exactly:

       WINEDLLOVERRIDES="version=n,b" %command%

3. Close the window. Steam saves it automatically.

### Step 4: Start the game once, without the mod

1. Start Over the Hill from Steam.
2. A MelonLoader console window with scrolling text appears before the game. **The first start is slow, often several minutes,** while MelonLoader prepares itself. It can look frozen; it isn't. Wait.
3. When the game's main menu appears, quit the game.

Check: the game folder now has a `Mods` folder. If it doesn't, create one with exactly that name.

### Step 5: Install the mod

1. Download [OTH.Fuel.dll](https://github.com/kaleLetendre/over-the-hill-fuel/releases/latest/download/OTH.Fuel.dll).
2. Put it in the `Mods` folder inside the game folder.

### Step 6: Start the game and check it loaded

Start the game again. In the MelonLoader console window, look for:

    Fuel v0.3.0
    ...
    [Fuel] fuel network ready
    Mods loaded.

Load into the world and get in a car. The fuel bar appears above the gear display on the right, and the console shows a line like:

    [Fuel] 'Car_Burro(Clone)': CarInfo 'Burro' id 1260490138, capacity 3.5 L (table)

The same text is saved in `MelonLoader/Latest.log` in the game folder.

### Step 7: Equip a jerry can

At the mechanic, open the tools screen. The **Jerry can** is listed with the other tools; equip it into a slot (up to 2). Use it from the toolbox like a repair kit: pick the car to refuel. The prompt says **Refill fuel**, and each car's button shows its fuel level.

Done. You only do all this once. Next time, just start the game.

---

## Setup on Windows

### Step 1: Install MelonLoader v0.7.3

1. Download the latest [MelonLoader Installer](https://github.com/LavaGang/MelonLoader.Installer/releases/latest) (`MelonLoader.Installer.exe`) and run it.
2. Pick **Over the Hill** from the list. If it isn't listed, add it by browsing to `over the hill.exe`. To find that file: in Steam, right-click the game → **Manage** → **Browse local files**.
3. Choose version **v0.7.3**, then click **Install**.

No launch option is needed on Windows.

### Step 2: Start the game once

Start the game, wait for the main menu (the first start is slow while MelonLoader prepares itself), then quit.

### Step 3: Install the mod

1. Download [OTH.Fuel.dll](https://github.com/kaleLetendre/over-the-hill-fuel/releases/latest/download/OTH.Fuel.dll) and put it in the `Mods` folder in the game folder.
2. If Windows marks the file as downloaded from the internet: right-click it → **Properties** → tick **Unblock** → **OK**.

Then continue with [Step 6 of the Linux guide](#step-6-start-the-game-and-check-it-loaded): the checks are the same.

---

## Multiplayer

- **Every player in the session needs the mod.** An equipped jerry can is part of your car's tools, which other players' games receive.
- Each player's fuel lives on their own machine. Players with the mod send each other their fuel level every 2 s, so the jerry can picker can show it.
- Using a jerry can on another player's car sends its fuel to that player's game. You keep the can if their tank is already full.
- In the picker, cars of players without the mod are greyed out, and the mod never sends them anything.

---

## Troubleshooting

Most answers are in `MelonLoader/Latest.log` in the game folder. Search it for `[Fuel]`.

**No MelonLoader console window, and no `MelonLoader/Latest.log`**
MelonLoader isn't loading. On Linux, check the launch option from [Step 3](#step-3-tell-proton-to-load-melonloader) is exactly `WINEDLLOVERRIDES="version=n,b" %command%`, and that `version.dll` is next to `over the hill.exe`.

**The log doesn't mention `Fuel` at all, or says `0 Mods loaded`**
The DLL isn't in the `Mods` folder, or it's inside a subfolder. It must be `Mods/OTH.Fuel.dll`. On Windows, also check it's unblocked.

**The log says `Fuel network unavailable`**
Single player still works fully. Only jerry cans on other players' cars, and their fuel levels in the picker, are off. Please [open an issue](https://github.com/kaleLetendre/over-the-hill-fuel/issues) with the log.

**The engine won't start**
Check the fuel bar. If it's empty, teleport or respawn: that refills the toolbox and the tank. A flooded engine also won't start until it dries out, exactly as without the mod.

**The game crashes on start or gets stuck loading after installing the mod**
Remove `Mods/OTH.Fuel.dll` to play without it, and [open an issue](https://github.com/kaleLetendre/over-the-hill-fuel/issues) with `MelonLoader/Latest.log`. Say which other mods you have.

---

## Settings

`UserData/MelonPreferences.cfg` in the game folder, section `[Fuel]`. The section appears after the first run with the mod. **Edit it only while the game is closed.** MelonLoader rewrites the file when the game quits.

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Turn fuel on or off |
| `TankRatio` | `20` | Real tank size divided by this is the in-game tank. Applies to the next car spawn |
| `FuelScale` | `1` | Consumption multiplier: 0.5 = half as thirsty, 2 = twice as thirsty |
| `LowFuelThreshold` | `0.2` | Tank fraction below which the fuel bar turns red |
| `DebugLog` | `false` | Log the burn rate every 30 s and jerry can details |

## Saved data

`UserData/Fuel/<save name>.json` in the game folder holds each car's fuel level and which tool slots hold a jerry can. Delete it to start a save with full tanks and no cans equipped.

## Other mods

Works alongside [Extra Gears](https://github.com/kaleLetendre/over-the-hill-extra-gears) and [Impulse Triggers](https://github.com/kaleLetendre/over-the-hill-trigger-rumble) (tested together). With Extra Gears, the taller 3rd and 4th gears use less fuel at cruise.

## Updating

Download the new `OTH.Fuel.dll` from [Releases](https://github.com/kaleLetendre/over-the-hill-fuel/releases) and replace the old one in `Mods`.

## Uninstall

- **Just this mod:** delete `Mods/OTH.Fuel.dll` from the game folder. Your saves keep working. Equipped jerry cans simply aren't there any more.
- **MelonLoader too:** delete `version.dll` and the `MelonLoader` folder from the game folder. On Linux, also clear the launch option from Step 3.

---

## How it works

- **Burn:** each tick, the engine's own fuel rate (`Engine.sensorFuelRate`, Vehicle Physics Pro) is added up in litres. At zero fuel, the mod cuts fuel to the engine, stops it, and blocks restarts, even after a flood dries out.
- **Refills:** a postfix on `CarInventory.RPC_RefillItems`, the game's toolbox refill, fills the tank. Refills that happen inside camping are skipped.
- **Jerry can:**
  - It's a runtime item added to the game's item catalog and every car's tool list.
  - It uses the repair kit's car picker, and a prefix on `StateCarRepair.HealPlayer` refuels the car instead of repairing it.
  - The game handles equipping, charges and refills.
  - Around every game save, the can is taken out of the save and kept in the mod's own file instead.
- **Multiplayer:**
  - Fuel messages use Photon Fusion reliable data with the mod's own key, routed through the host.
  - A native hook on `OverlandNetworkRunner.OnReliableDataReceived` takes the mod's messages and passes everything else to the game untouched.

Details and the investigation are in [notes/log.md](notes/log.md), the rules in [notes/design.md](notes/design.md) and the game's hook points in [notes/hooks.md](notes/hooks.md).

## Building from source

Only needed to change the mod. Players should use the DLL from Releases.

- Install MelonLoader and run the game once (Linux Steps 1–4), so `MelonLoader/Il2CppAssemblies` exists to build against.
- `scripts/install-melonloader.sh` does Linux Step 2 for you; `scripts/uninstall-melonloader.sh` reverses it.
- `scripts/build.sh [--no-copy]` builds with the .NET SDK (8+; `~/.dotnet` is picked up automatically) and copies the DLL into the game's `Mods` folder. Set `GAME_DIR` if the game isn't in `~/.steam/steam/steamapps/common/over the hill`.

## License

[MIT](LICENSE).
