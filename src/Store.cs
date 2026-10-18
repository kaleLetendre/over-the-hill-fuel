using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Il2Cpp;
using MelonLoader.Utils;

namespace OTH.Fuel;

// Saved fuel, one sidecar file per game save: UserData/Fuel/<profile>.json.
//
// The game names its save file after Profile.Name (OverlandSaver.SaveGame
// writes <SaveLocation>/<Profile.Name>.json), so the name is the save slot's
// identity. Each car is keyed by its CarInfo.ItemId and keeps its own tank
// fraction (so a later tank-size change keeps the level). Litres and capacity
// are written too, for reading the file only.
//
// It also keeps which tool slots of each car hold the jerry can: the can's id
// is taken out of the game's save while it is written (JerryCan), so removing
// the mod leaves a save the game knows, and it is put back from here on load.
// A missing or broken sidecar just means full tanks and no can fitted.
internal static class Store
{
    internal sealed class Entry
    {
        public float Fraction;

        public float Litres;

        public float Capacity;

        public string Car;
    }

    private const int Version = 1;

    // The profile whose sidecar is loaded (null before a save is selected).
    internal static string Profile { get; private set; }

    private static readonly Dictionary<uint, Entry> cars = new();

    // Car (VehicleItem.ItemId) -> tool slots holding the jerry can, as last
    // saved; and the ones read from disk that aren't back in the game yet.
    private static readonly Dictionary<uint, List<int>> canSlots = new();

    private static readonly Dictionary<uint, List<int>> pendingSlots = new();

    // Last text written or read, so unchanged saves (autosaves) skip the disk.
    private static string lastText;

    private static bool warnedWrite;

    private static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "Fuel");

    internal static string CurrentProfileName()
    {
        ProfileSave save = OverlandSaver.CurrentProfileSave;
        Profile p = save != null ? save.Profile : null;
        return p != null && !string.IsNullOrEmpty(p.Name) ? p.Name : null;
    }

    private static string PathFor(string profile)
    {
        StringBuilder sb = new(profile.Length);
        char[] bad = Path.GetInvalidFileNameChars();
        foreach (char c in profile)
        {
            sb.Append(Array.IndexOf(bad, c) >= 0 || c == '/' || c == '\\' || c == ':' ? '_' : c);
        }
        return Path.Combine(Dir, sb + ".json");
    }

    // A save was selected (loaded, switched to, or started). Reads its sidecar
    // from disk, dropping anything in memory: if the game didn't save, the fuel
    // shouldn't either. A new save starts with no cars (full tanks) and
    // overwrites any sidecar left by an older save of the same name.
    internal static void Select(string profile, bool fresh)
    {
        Load(profile, fresh);
        Tank.ResetRespawnWindow();
        Tank t = Tank.Local;
        if (t != null && t.Alive)
        {
            t.Reselect();
        }
    }

    private static void Load(string profile, bool fresh)
    {
        cars.Clear();
        canSlots.Clear();
        pendingSlots.Clear();
        lastText = null;
        Profile = profile;
        if (profile == null)
        {
            return;
        }
        string path = PathFor(profile);
        if (fresh)
        {
            Mod.Log.Msg($"save '{profile}': new save, all tanks start full");
            Write();
            return;
        }
        if (!File.Exists(path))
        {
            Mod.Log.Msg($"save '{profile}': no fuel file yet, tanks start full");
            return;
        }
        try
        {
            string text = File.ReadAllText(path);
            Parse(text, profile);
            foreach (KeyValuePair<uint, List<int>> kv in canSlots)
            {
                pendingSlots[kv.Key] = new List<int>(kv.Value);
            }
            lastText = text;
            Mod.Log.Msg($"save '{profile}': fuel loaded for {cars.Count} car(s) from {path}");
        }
        catch (Exception ex)
        {
            cars.Clear();
            canSlots.Clear();
            pendingSlots.Clear();
            // Keep the broken file for inspection instead of overwriting it.
            try
            {
                File.Copy(path, path + ".bad", true);
            }
            catch (Exception)
            {
                // Best effort; the warning below says what happened.
            }
            Mod.Log.Warning($"save '{profile}': fuel file unreadable, tanks start full (copy kept as .bad): {ex.Message}");
        }
    }

    private static void Parse(string text, string profile)
    {
        using JsonDocument doc = JsonDocument.Parse(text);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("not a JSON object");
        }
        if (root.TryGetProperty("profile", out JsonElement p) && p.ValueKind == JsonValueKind.String && p.GetString() != profile)
        {
            Mod.Log.Warning($"fuel file for '{profile}' says profile '{p.GetString()}'; using it anyway");
        }
        if (root.TryGetProperty("jerryCanSlots", out JsonElement js) && js.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty car in js.EnumerateObject())
            {
                if (!uint.TryParse(car.Name, out uint id) || car.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                List<int> slots = new();
                foreach (JsonElement x in car.Value.EnumerateArray())
                {
                    if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out int n) && n >= 0 && n < 64 && !slots.Contains(n))
                    {
                        slots.Add(n);
                    }
                }
                if (slots.Count > 0)
                {
                    canSlots[id] = slots;
                }
            }
        }
        if (!root.TryGetProperty("cars", out JsonElement list) || list.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        foreach (JsonProperty car in list.EnumerateObject())
        {
            if (!uint.TryParse(car.Name, out uint id) || car.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            JsonElement v = car.Value;
            // Each field is type-checked: a bad level skips only this car
            // (it starts full), other bad fields fall back to defaults.
            float fraction = Float(v, "fraction", float.NaN);
            if (!float.IsFinite(fraction))
            {
                Mod.Log.Warning($"fuel file for '{profile}': car {id} has no valid level; skipped");
                continue;
            }
            Entry e = new()
            {
                Fraction = fraction,
                Litres = Float(v, "litres", 0f),
                Capacity = Float(v, "capacity", 0f),
                Car = v.TryGetProperty("car", out JsonElement name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null,
            };
            e.Fraction = Math.Clamp(e.Fraction, 0f, 1f);
            cars[id] = e;
        }
    }

    private static float Float(JsonElement v, string key, float dflt)
    {
        return v.TryGetProperty(key, out JsonElement x) && x.ValueKind == JsonValueKind.Number && x.TryGetSingle(out float f) ? f : dflt;
    }

    internal static Entry Get(uint itemId)
    {
        return itemId != 0 && cars.TryGetValue(itemId, out Entry e) ? e : null;
    }

    // The jerry can slots of every car, taken from the game's save as it is
    // written (replaces the previous set); written with the next Write.
    internal static void SetCanSlots(Dictionary<uint, List<int>> slots)
    {
        canSlots.Clear();
        foreach (KeyValuePair<uint, List<int>> kv in slots)
        {
            canSlots[kv.Key] = new List<int>(kv.Value);
        }
    }

    // Slots read from disk that still have to go back into the game's
    // inventory; JerryCan removes each car once it is put back.
    internal static Dictionary<uint, List<int>> PendingCanSlots => pendingSlots;

    // Records a car's current level in memory; written on the next save.
    internal static void Put(Tank t)
    {
        if (t == null || t.ItemId == 0 || Profile == null || t.Profile != Profile || t.Capacity <= 0f)
        {
            return;
        }
        if (!cars.TryGetValue(t.ItemId, out Entry e))
        {
            cars[t.ItemId] = e = new Entry();
        }
        e.Fraction = t.Empty ? 0f : Math.Clamp(t.Fuel / t.Capacity, 0f, 1f);
        e.Litres = t.Empty ? 0f : Math.Max(0f, t.Fuel);
        e.Capacity = t.Capacity;
        e.Car = t.CarName;
    }

    // Called after the game saves. Skips the disk if nothing changed.
    internal static void Write()
    {
        if (Profile == null)
        {
            return;
        }
        string text = Serialize();
        if (text == lastText)
        {
            return;
        }
        string path = PathFor(Profile);
        try
        {
            Directory.CreateDirectory(Dir);
            // Write a temp file and swap it in, so a crash mid-write can't
            // leave a half-written sidecar.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, true);
            lastText = text;
            if (Mod.DebugLog)
            {
                Mod.Log.Msg($"save '{Profile}': fuel written for {cars.Count} car(s)");
            }
        }
        catch (Exception ex)
        {
            if (!warnedWrite)
            {
                warnedWrite = true;
                Mod.Log.Warning($"Could not write {path}: {ex.Message}");
            }
        }
    }

    private static string Serialize()
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter w = new(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", Version);
            w.WriteString("profile", Profile);
            w.WriteStartObject("cars");
            List<uint> ids = new(cars.Keys);
            ids.Sort();
            foreach (uint id in ids)
            {
                Entry e = cars[id];
                w.WriteStartObject(id.ToString());
                if (e.Car != null)
                {
                    w.WriteString("car", e.Car);
                }
                w.WriteNumber("fraction", MathF.Round(e.Fraction, 4));
                w.WriteNumber("litres", MathF.Round(e.Litres, 3));
                w.WriteNumber("capacity", MathF.Round(e.Capacity, 3));
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteStartObject("jerryCanSlots");
            List<uint> canCars = new(canSlots.Keys);
            canCars.Sort();
            foreach (uint id in canCars)
            {
                w.WriteStartArray(id.ToString());
                foreach (int slot in canSlots[id])
                {
                    w.WriteNumberValue(slot);
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
