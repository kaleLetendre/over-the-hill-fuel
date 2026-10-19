using System;
using System.Globalization;
using MelonLoader;
using UnityEngine;

namespace OTH.Fuel;

// Adds fuel to Over the Hill.
//
// The local player's car burns fuel while its engine runs, at the rate Vehicle
// Physics Pro already computes (Engine.sensorFuelRate: rpm and load dependent,
// idle included), times FuelScale. At empty the engine stops and won't restart
// until the game refills the toolbox (CarInventory.RPC_RefillItems: mechanic,
// cabins, portals, teleport/respawn). Camping does not refuel. Flooding and fuel
// are separate start blocks: drying out never lets an empty engine start.
//
// A fuel bar, cloned from the game's engine-flooding bar, sits above the gear
// display.
//
// Each car's level and jerry can are saved in a sidecar file next to the game
// save (Store, SavePatches). The jerry can (JerryCan) is a local-only toolbox
// tool that refuels the car. Only the local car is tracked; nothing is synced.
public class Mod : MelonMod
{
    internal static MelonLogger.Instance Log;

    private static MelonPreferences_Entry<bool> prefEnabled;

    private static MelonPreferences_Entry<float> prefFuelScale;

    private static MelonPreferences_Entry<float> prefLowFuel;

    private static MelonPreferences_Entry<float> prefTankRatio;

    private static MelonPreferences_Entry<bool> prefDebug;

    private static MelonPreferences_Entry<bool> prefMultiplayer;

    internal static bool Enabled => prefEnabled.Value;

    internal static float FuelScale => Tank.Sane(prefFuelScale.Value);

    // Real tank size / this = game tank size. At least 1.
    internal static float TankRatio => Math.Max(1f, Tank.Sane(prefTankRatio.Value));

    internal static float LowFuelThreshold => Mathf.Clamp01(prefLowFuel.Value);

    internal static bool DebugLog => prefDebug.Value;

    internal static bool Multiplayer => prefMultiplayer.Value;

    private const float BarInterval = 0.1f;

    private float nextBar;

    private bool wasEnabled = true;

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        MelonPreferences_Category cat = MelonPreferences.CreateCategory("Fuel");
        prefEnabled = cat.CreateEntry("Enabled", true, description: "Turn fuel on or off");
        prefFuelScale = cat.CreateEntry("FuelScale", 1f, description: "Consumption multiplier: 0.5 = half as thirsty, 2 = twice as thirsty");
        prefTankRatio = cat.CreateEntry("TankRatio", 20f, description: "Real tank size divided by this is the in-game tank (20 = a 70 L tank holds 3.5 L). Applies to the next car spawn");
        prefLowFuel = cat.CreateEntry("LowFuelThreshold", 0.2f, description: "Tank fraction below which the fuel bar turns red (0..1)");
        prefDebug = cat.CreateEntry("DebugLog", false, description: "Log burn rate (L/min) every 30 s, for calibrating tank sizes");
        prefMultiplayer = cat.CreateEntry("Multiplayer", true, description: "Share fuel with other modded players online (jerry cans on their cars). Set false if another mod's networking conflicts");
    }

    public override void OnUpdate()
    {
        if (!Enabled)
        {
            if (wasEnabled)
            {
                wasEnabled = false;
                Disable();
            }
            return;
        }
        wasEnabled = true;
        float now = Time.unscaledTime;
        Tank tank = Tank.Local;
        try
        {
            if (tank != null && !tank.Alive)
            {
                Log.Msg($"'{tank.Name}' gone; fuel tracking stopped");
                Tank.Lost(tank);
                tank = null;
            }
            if (tank != null)
            {
                tank.Tick(Time.deltaTime, now);
            }
            Tank.DescribePending(now);
        }
        catch (System.Exception ex)
        {
            // Most likely a car despawned between checks; never throw every frame.
            Log.Warning($"Fuel tick failed, dropping the tracked car: {ex.Message}");
            // Undo our fuel cut / start block first, or the car stays dead.
            tank?.Release("tick failed");
            Tank.Local = tank = null;
            Tank.ClearPending();
        }

        if (now >= nextBar)
        {
            nextBar = now + BarInterval;
            FuelBar.Tick(tank);
            Net.Tick(tank);
        }
    }

    // The game may not save on quit; keep the fuel level as it is now, so a
    // reload never gives back fuel burnt since the last save.
    public override void OnApplicationQuit()
    {
        if (Enabled)
        {
            SavePatches.Save("quit");
        }
    }

    // Enabled switched off at runtime: undo our engine changes and hide the bar.
    private static void Disable()
    {
        if (Tank.Local != null)
        {
            Tank.Local.Release("fuel disabled");
            Tank.Local = null;
        }
        Tank.ClearPending();
        FuelBar.Tick(null);
        Log.Msg("Fuel disabled");
    }

    internal static string F(float v)
    {
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
