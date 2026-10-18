using System;
using HarmonyLib;
using Il2Cpp;

namespace OTH.Fuel;

// Car discovery without scene-wide searches.
//
// NetworkedVehicleStatus.Spawned runs once per car on every peer: each car is
// queued to be described in the log (name, CarInfo, authority flags).
// Despawned drops it again.
// HUDManager.SetupCarRelatedHUD runs for the car the HUD follows, i.e. the
// local player's car: that car gets a tank and the HUD gets the fuel bar.
internal static class SpawnPatches
{
    [HarmonyPatch(typeof(NetworkedVehicleStatus), nameof(NetworkedVehicleStatus.Spawned))]
    private static class StatusSpawnedPatch
    {
        private static void Postfix(NetworkedVehicleStatus __instance)
        {
            if (!Mod.Enabled || __instance == null)
            {
                return;
            }
            try
            {
                Tank.Seen(__instance);
            }
            catch (Exception ex)
            {
                Mod.Log.Warning("Spawn hook failed: " + ex.Message);
            }
        }
    }

    // Drop the car before Fusion invalidates its networked state.
    [HarmonyPatch(typeof(NetworkedVehicleStatus), nameof(NetworkedVehicleStatus.Despawned))]
    private static class StatusDespawnedPatch
    {
        private static void Prefix(NetworkedVehicleStatus __instance)
        {
            if (__instance == null)
            {
                return;
            }
            try
            {
                Tank.Despawned(__instance);
            }
            catch (Exception ex)
            {
                Mod.Log.Warning("Despawn hook failed: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(HUDManager), nameof(HUDManager.SetupCarRelatedHUD))]
    private static class SetupHudPatch
    {
        private static void Postfix(HUDManager __instance, NetworkedVehicleStatus nvs)
        {
            if (!Mod.Enabled || nvs == null)
            {
                return;
            }
            try
            {
                Tank.Adopt(nvs, "HUD car");
            }
            catch (Exception ex)
            {
                Mod.Log.Warning("Could not track the HUD car: " + ex);
            }
            FuelBar.Build(__instance);
        }
    }
}
