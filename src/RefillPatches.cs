using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;
using Il2CppFusion;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace OTH.Fuel;

// Refuels the local car to full whenever the game refills its toolbox items
// (CarInventory.RPC_RefillItems), so fuel follows the game's own toolbox rules:
// mechanic, cabins, portals and teleport/respawn refuel, camping doesn't. The
// jerry can refills at the same moments (also not when camping, or a camp
// would refuel through the can).
//
// Refills inside CampManager.RepairPartyCars are skipped. The other hooks only
// record context (POI reached, respawn, portal, camping, heals) so each refill
// line says what led to it.
internal static class RefillPatches
{
    [HarmonyPatch(typeof(CarInventory), nameof(CarInventory.RPC_RefillItems))]
    private static class RefillItemsPatch
    {
        private static void Postfix(CarInventory __instance)
        {
            if (!Mod.Enabled || __instance == null)
            {
                return;
            }
            Tank t = Tank.Local;
            bool local = t != null && t.OwnsInventory(__instance);
            string context = Context();
            NetworkRunner runner = __instance.Runner;
            string peer = runner != null ? $"server={runner.IsServer} client={runner.IsClient}" : "no runner";
            // Logged for every car while we learn which peers run this RPC.
            Mod.Log.Msg($"RPC_RefillItems on '{__instance.name}': local={local}, this peer: {peer}, "
                + $"state={__instance.HasStateAuthority} input={__instance.HasInputAuthority}"
                + (Camping ? ", inside CampManager.RepairPartyCars" : "") + $"; context: {context}");
            if (!local)
            {
                if (t == null && __instance.HasInputAuthority && !Camping)
                {
                    // Our next car's refill, before that car is tracked.
                    Tank.EarlyRefill(context);
                }
                return;
            }
            if (Camping)
            {
                Mod.Log.Msg($"'{t.Name}': toolbox refilled by camping; not refuelling");
                return;
            }
            t.Refill("toolbox refill; " + context);
        }
    }

    // Camping must not refuel, even if it refills the toolbox: refills that
    // happen inside RepairPartyCars are skipped.
    [HarmonyPatch(typeof(CampManager), nameof(CampManager.RepairPartyCars))]
    private static class RepairPartyCarsPatch
    {
        private static void Prefix()
        {
            // Time-limited, so a throw inside the call can't leave it set.
            campingUntil = Time.unscaledTime + 5f;
            Note("CampManager.RepairPartyCars");
        }

        private static void Postfix()
        {
            campingUntil = 0f;
        }
    }

    [HarmonyPatch(typeof(CarInventory), nameof(CarInventory.OnPOIReached))]
    private static class PoiReachedPatch
    {
        private static void Postfix(CarInventory __instance, PointOfInterest poi)
        {
            if (IsLocal(__instance) && poi != null)
            {
                PointOfInterestInfo info = poi.Info;
                Note($"POI '{poi.name}' ({(info != null ? info.GetIl2CppType().Name : "no info")})");
            }
        }
    }

    [HarmonyPatch(typeof(CarInventory), nameof(CarInventory.AskForItemRefill))]
    private static class AskRefillPatch
    {
        private static void Postfix(CarInventory __instance)
        {
            if (IsLocal(__instance))
            {
                Note("AskForItemRefill");
            }
        }
    }

    // May refill as part of a toolbox change (mechanic); logged to find out
    // whether it goes through RPC_RefillItems. It does not refuel by itself.
    [HarmonyPatch(typeof(CarInventory), nameof(CarInventory.RPC_SetEquippedItems))]
    private static class SetEquippedPatch
    {
        private static void Postfix(CarInventory __instance, Il2CppStructArray<uint> toolsetIDs, bool alsoRefill)
        {
            if (IsLocal(__instance))
            {
                Note($"RPC_SetEquippedItems({toolsetIDs?.Length ?? 0} items, alsoRefill={alsoRefill})", always: true);
            }
        }
    }

    [HarmonyPatch(typeof(CarDamageSystem), nameof(CarDamageSystem.RPC_VisualCarHeal))]
    private static class HealPatch
    {
        private static void Postfix(CarDamageSystem __instance, CarDamageSystem.HealthChangingReason reason)
        {
            Tank t = Tank.Local;
            if (Mod.Enabled && t != null && __instance != null && __instance.vehicleStatus != null
                && __instance.vehicleStatus.Pointer == t.Status.Pointer)
            {
                Note($"heal {reason}");
            }
        }
    }

    [HarmonyPatch(typeof(HUDManager), nameof(HUDManager.PortalFinished))]
    private static class PortalPatch
    {
        private static void Postfix()
        {
            Note("portal finished");
        }
    }

    [HarmonyPatch(typeof(HUDManager), nameof(HUDManager.OnBeginCamping))]
    private static class CampingPatch
    {
        private static void Postfix()
        {
            Note("camping started (does not refuel)");
        }
    }

    private const float ContextWindow = 15f;

    private static readonly List<(float time, string what)> notes = new();

    // Set while CampManager.RepairPartyCars runs.
    private static float campingUntil;

    private static bool Camping => Time.unscaledTime < campingUntil;

    private static bool IsLocal(CarInventory inv)
    {
        return Mod.Enabled && Tank.Local != null && Tank.Local.OwnsInventory(inv);
    }

    // Records an event for the refill context. Logged with DebugLog on, or
    // always (once) when `always` is set.
    internal static void Note(string what, bool always = false)
    {
        if (!Mod.Enabled)
        {
            return;
        }
        float now = Time.unscaledTime;
        notes.RemoveAll(n => now - n.time > ContextWindow);
        notes.Add((now, what));
        if (always || Mod.DebugLog)
        {
            Mod.Log.Msg("event: " + what);
        }
    }

    private static string Context()
    {
        float now = Time.unscaledTime;
        notes.RemoveAll(n => now - n.time > ContextWindow);
        if (notes.Count == 0)
        {
            return "no event in the last 15 s";
        }
        return string.Join(", ", notes.Select(n => $"{n.what} {Mod.F(now - n.time)} s ago"));
    }
}
