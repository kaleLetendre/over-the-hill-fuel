using System;
using HarmonyLib;
using Il2Cpp;

namespace OTH.Fuel;

// Keeps the sidecar fuel file (Store) in step with the game's own save.
//
// OverlandSaver.SetCurrentGameSave is the one place a save becomes current
// (StateProfileManagementSystem after LoadGame, and StartNewSave), so its
// postfix loads that save's fuel. StartNewSave marks the selection as fresh,
// so a new save never inherits an old sidecar of the same name.
// OverlandSaver.SaveGame (manual saves and autosaves) records the tracked car
// and writes the file; quitting writes it too (Mod.OnApplicationQuit).
internal static class SavePatches
{
    // Set while StartNewSave runs. Time-limited, so a throw inside the call
    // can't leave it set and wipe a later load.
    private static float startingNewUntil;

    private static bool StartingNew => UnityEngine.Time.unscaledTime < startingNewUntil;

    private static bool warnedSave;

    private static bool warnedSelect;

    [HarmonyPatch(typeof(OverlandSaver), nameof(OverlandSaver.StartNewSave))]
    private static class StartNewSavePatch
    {
        private static void Prefix()
        {
            startingNewUntil = UnityEngine.Time.unscaledTime + 5f;
        }

        private static void Postfix()
        {
            startingNewUntil = 0f;
        }
    }

    [HarmonyPatch(typeof(OverlandSaver), nameof(OverlandSaver.SetCurrentGameSave))]
    private static class SetCurrentGameSavePatch
    {
        private static void Postfix(ProfileSave save)
        {
            if (!Mod.Enabled)
            {
                return;
            }
            try
            {
                Profile p = save != null ? save.Profile : null;
                Store.Select(p != null && !string.IsNullOrEmpty(p.Name) ? p.Name : null, StartingNew);
            }
            catch (Exception ex)
            {
                if (!warnedSelect)
                {
                    warnedSelect = true;
                    Mod.Log.Warning("Could not load the saved fuel: " + ex.Message);
                }
            }
        }
    }

    [HarmonyPatch(typeof(OverlandSaver), nameof(OverlandSaver.SaveGame))]
    private static class SaveGamePatch
    {
        private static void Postfix()
        {
            if (Mod.Enabled)
            {
                Save("game save");
            }
        }
    }

    // Records the tracked car and writes the sidecar for the current save.
    internal static void Save(string why)
    {
        try
        {
            if (Store.Profile == null || Store.CurrentProfileName() != Store.Profile)
            {
                // No save selected, or the game switched saves without
                // SetCurrentGameSave (not expected): don't write fuel under the wrong name.
                return;
            }
            Tank t = Tank.Local;
            if (t != null && t.Alive)
            {
                Store.Put(t);
            }
            Store.Write();
        }
        catch (Exception ex)
        {
            if (!warnedSave)
            {
                warnedSave = true;
                Mod.Log.Warning($"Could not save the fuel ({why}): {ex.Message}");
            }
        }
    }
}
