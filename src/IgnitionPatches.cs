using HarmonyLib;
using Il2Cpp;
using Il2CppFusion;

namespace OTH.Fuel;

// Keeps an empty engine from starting, independently of flooding.
//
// StartEngine and ToggleEngine are refused while the tank is empty (Toggle only
// when it would start the engine). The flooding system may clear the shared
// EngineStartBlocked flag when the engine dries out or the car respawns, so the
// fuel block is re-asserted right after those calls. It is also enforced every
// network tick (CarIgnition.FixedUpdateNetwork) and every frame (Tank.Tick), in
// case the game starts the engine some other way.
internal static class IgnitionPatches
{
    [HarmonyPatch(typeof(CarIgnition), nameof(CarIgnition.StartEngine))]
    private static class StartEnginePatch
    {
        private static bool Prefix(CarIgnition __instance)
        {
            Tank t = Tank.Local;
            return !(Mod.Enabled && t != null && t.Refuse(__instance, "StartEngine"));
        }
    }

    [HarmonyPatch(typeof(CarIgnition), nameof(CarIgnition.ToggleEngine))]
    private static class ToggleEnginePatch
    {
        private static bool Prefix(CarIgnition __instance)
        {
            Tank t = Tank.Local;
            if (!Mod.Enabled || t == null || !t.Empty || !t.OwnsIgnition(__instance) || __instance.EngineRunning)
            {
                return true;
            }
            return !t.Refuse(__instance, "ToggleEngine");
        }
    }

    [HarmonyPatch(typeof(CarIgnition), nameof(CarIgnition.FixedUpdateNetwork))]
    private static class FixedUpdateNetworkPatch
    {
        private static void Postfix(CarIgnition __instance)
        {
            Tank t = Tank.Local;
            if (!Mod.Enabled || t == null || !t.Empty || !t.OwnsIgnition(__instance))
            {
                return;
            }
            try
            {
                t.Enforce(null);
            }
            catch (System.Exception ex)
            {
                Fail(ex);
            }
        }
    }

    [HarmonyPatch(typeof(EngineFloodingSystem), nameof(EngineFloodingSystem.SetFlooded))]
    private static class SetFloodedPatch
    {
        private static void Postfix(EngineFloodingSystem __instance, bool flooded)
        {
            Tank t = Local(__instance);
            if (t != null)
            {
                Reassert(t, __instance, flooded ? "SetFlooded(true)" : "SetFlooded(false)");
            }
        }
    }

    [HarmonyPatch(typeof(EngineFloodingSystem), nameof(EngineFloodingSystem.ResetFloodingState))]
    private static class ResetFloodingPatch
    {
        private static void Postfix(EngineFloodingSystem __instance)
        {
            Tank t = Local(__instance);
            if (t != null)
            {
                Reassert(t, __instance, "ResetFloodingState");
            }
        }
    }

    // Runs on every car's flooding system for any player's respawn; only our
    // driver's respawn matters.
    [HarmonyPatch(typeof(EngineFloodingSystem), nameof(EngineFloodingSystem.OnStateAuthorityPlayerRespawned))]
    private static class RespawnedPatch
    {
        private static void Postfix(EngineFloodingSystem __instance, PlayerRef playerRef)
        {
            Tank t = Local(__instance);
            if (t == null || !t.IsDriver(playerRef))
            {
                return;
            }
            RefillPatches.Note("respawn");
            Reassert(t, __instance, "respawn reset");
        }
    }

    private static float nextTrace;

    private static bool failed;

    private static Tank Local(EngineFloodingSystem fs)
    {
        Tank t = Tank.Local;
        return Mod.Enabled && t != null && t.OwnsFlooding(fs) ? t : null;
    }

    private static void Reassert(Tank t, EngineFloodingSystem fs, string after)
    {
        try
        {
            // SetFlooded may run every network tick; keep the trace to one line per 2 s.
            if (Mod.DebugLog && UnityEngine.Time.unscaledTime >= nextTrace)
            {
                nextTrace = UnityEngine.Time.unscaledTime + 2f;
                Mod.Log.Msg($"'{t.Name}': {after} (flooded={(bool)fs.IsFlooded}, startBlocked={(bool)t.Ignition.EngineStartBlocked}, empty={t.Empty})");
            }
            t.Enforce(after);
        }
        catch (System.Exception ex)
        {
            Fail(ex);
        }
    }

    private static void Fail(System.Exception ex)
    {
        if (!failed)
        {
            failed = true;
            Mod.Log.Warning("Could not enforce the fuel block: " + ex.Message);
        }
        // Stop tracking so a persistent fault doesn't throw every network tick.
        Tank.Local?.Release("enforce failed");
        Tank.Local = null;
    }
}
