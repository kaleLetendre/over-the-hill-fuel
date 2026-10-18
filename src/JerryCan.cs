using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OTH.Fuel;

// Jerry can: a toolbox tool, equipped at the mechanic like the others, that
// adds a fixed amount of fuel (CarSpecs.JerryCanLitres) to a car's tank.
//
// Item: an InventoryItemScriptableObject made at runtime (UsableItem, our
// icon, a fixed id), registered in the item catalog and added to every car's
// tool list (ToolsLists.VehicleTools). The mechanic's tools screen lists every
// tool in that list, locked unless the player owns it (an InventoryItem in
// PlayerInventory), so the can is also given to the player. It is equipped
// into a slot the normal way (VehicleItem.ToolsetIDs). From there the
// game does the rest: CarInventory gives it a charge per slot, the toolbox
// shows it, uses a charge (AskForItemConsumption) and refills it with the
// other tools (RPC_RefillItems).
//
// Use: the toolbox maps each tool to an operation (usableItemOperations); the
// can uses the repair kit's (OpenRepairingUI), which opens the car picker
// (StateCarRepair) with our id. The prefix on StateCarRepair.HealPlayer
// catches our id before the game heals or consumes anything, adds the can's
// fuel to the chosen car and uses one charge of the can.
//
// Save safety: the game saves equipped tools with the car (ToolsetIDs). While
// the game writes its save, our id is taken out of every car's slots (SaveGame
// prefix) and put back right after (postfix), and so is the owned item; the
// slots are kept in the fuel
// sidecar (Store) and put back on load. So the game's save never holds an id
// it doesn't know, and removing the mod just leaves those slots empty.
//
// Delivery: fuel lives with each car's driver (Tank.Local on their machine).
// The local car is refuelled here; another player's car gets the litres sent
// to its driver (Net), who must run the mod. Every player in a session needs
// the mod anyway, since an equipped can is in the car's networked tool slots.
internal static class JerryCan
{
    // Game ids are 32-bit hashes; this one is checked against the catalog
    // before it is registered.
    internal const uint ItemId = 0xF0E1CA11u;

    private const string ItemName = "OTH_Fuel_JerryCan";

    private const string Title = "Jerry can";

    private const string Blurb = "Adds fuel to your car's tank (half a Burro tank). Refills with the rest of the toolbox.";

    private static InventoryItemScriptableObject item;

    private static LocalizedString title;

    private static LocalizedString blurb;

    // Null until checked; false disables the can for the session.
    private static bool? usable;

    private static IntPtr lastCatalog;

    private static readonly HashSet<string> warned = new();

    // Slots taken out of the game's inventory for the save being written.
    private static readonly List<(VehicleItem Car, int Slot)> stripped = new();

    // Our owned item, taken out of the inventory for the save being written.
    private static InventoryItem strippedOwned;

    // How many cans the player owns. The tools screen shows a tool as locked
    // unless the player owns it (PlayerInventory, UsableItem entries); Count
    // is how many can be equipped on one car.
    private const int Owned = 2;

    private static IntPtr lastPicker;

    // Registers the item, adds it to the tool lists (when asked) and puts
    // saved slots back. Called before every place the game reads tools.
    internal static void Prepare(bool toolLists)
    {
        if (!Mod.Enabled)
        {
            return;
        }
        try
        {
            if (!Register())
            {
                return;
            }
            if (toolLists)
            {
                AddToToolLists();
            }
            Unstrip();
            EnsureOwned();
            RestoreSaved();
        }
        catch (Exception ex)
        {
            Warn("setup", ex);
        }
    }

    // Like Unstrip for the slots, this item never reaches the game's save.
    private static void EnsureOwned()
    {
        Il2CppSystem.Collections.Generic.List<BaseInventoryItem> items = InventoryItems();
        if (items == null)
        {
            return;
        }
        InventoryItem have = OwnedItem(items);
        if (have != null)
        {
            have.Count = Owned;
            return;
        }
        InventoryItem own = new() { ItemId = ItemId, Count = Owned, InUse = false, IsNew = false };
        items.Add(own);
        Mod.Log.Msg($"jerry can unlocked ({Owned} owned)");
    }

    private static InventoryItem OwnedItem(Il2CppSystem.Collections.Generic.List<BaseInventoryItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            BaseInventoryItem b = items[i];
            if (b != null && b.ItemId == ItemId)
            {
                return b.TryCast<InventoryItem>();
            }
        }
        return null;
    }

    private static bool Register()
    {
        if (usable == false)
        {
            return false;
        }
        InventoryItemsCatalog catalog = PlayerInventoryManager.InventoryItemsCatalog;
        if (catalog == null || catalog.IdToSO == null || catalog.IdToName == null || catalog.NameToId == null)
        {
            return false;
        }
        if (usable == null)
        {
            // Without the HealPlayer prefix the game would run a real repair.
            Patches info = HarmonyLib.Harmony.GetPatchInfo(AccessTools.Method(typeof(StateCarRepair), nameof(StateCarRepair.HealPlayer)));
            if (info == null || info.Prefixes.Count == 0)
            {
                usable = false;
                Mod.Log.Warning("Jerry can disabled: the StateCarRepair.HealPlayer patch is missing");
                return false;
            }
            if (catalog.IdToSO.ContainsKey(ItemId))
            {
                usable = false;
                InventoryItemScriptableObject other = catalog.IdToSO[ItemId];
                Mod.Log.Warning($"Jerry can disabled: id {ItemId} is a game item ('{(other != null ? other.name : "?")}')");
                return false;
            }
            usable = true;
        }
        if (item == null)
        {
            title = new LocalizedString();
            blurb = new LocalizedString();
            item = ScriptableObject.CreateInstance(Il2CppInterop.Runtime.Il2CppType.Of<InventoryItemScriptableObject>()).Cast<InventoryItemScriptableObject>();
            item.name = ItemName;
            item.hideFlags = HideFlags.HideAndDontSave;
            item.ItemId = ItemId;
            item.Type = InventoryItemScriptableObject.ItemType.UsableItem;
            item.Icon = FuelBar.LoadIcon();
            item.Price = 0;
            item.RequiredDlc = DlcId.None;
            item.Name = title;
            item.Description = blurb;
        }
        // The catalog can be rebuilt (PlayerInventoryManager.ClearOnReload).
        if (catalog.Pointer != lastCatalog || !catalog.IdToSO.ContainsKey(ItemId))
        {
            catalog.IdToSO[ItemId] = item;
            catalog.IdToName[ItemId] = ItemName;
            catalog.NameToId[ItemName] = ItemId;
            lastCatalog = catalog.Pointer;
            Mod.Log.Msg($"jerry can registered as tool id {ItemId}");
        }
        return true;
    }

    // Every car's tool list (cars may share one).
    private static void AddToToolLists()
    {
        int added = 0, lists = 0;
        foreach (ToolsLists tl in Resources.FindObjectsOfTypeAll<ToolsLists>())
        {
            Il2CppSystem.Collections.Generic.List<InventoryItemScriptableObject> tools = tl != null ? tl.VehicleTools : null;
            if (tools == null)
            {
                continue;
            }
            lists++;
            bool has = false;
            for (int i = 0; i < tools.Count && !has; i++)
            {
                has = tools[i] != null && tools[i].ItemId == ItemId;
            }
            if (!has)
            {
                tools.Add(item);
                added++;
            }
        }
        if (added > 0)
        {
            Mod.Log.Msg($"jerry can added to {added} of {lists} tool list(s)");
        }
    }

    private static Il2CppSystem.Collections.Generic.List<BaseInventoryItem> InventoryItems()
    {
        PlayerInventoryManager pim = PlayerInventoryManager.Instance;
        PlayerInventory inv = pim != null ? pim.Inventory : null;
        return inv != null ? inv.Items : null;
    }

    // Saved slots (Store) go back into the game's inventory. A slot another
    // tool has taken meanwhile is left alone.
    private static void RestoreSaved()
    {
        Dictionary<uint, List<int>> pending = Store.PendingCanSlots;
        if (pending.Count == 0)
        {
            return;
        }
        Il2CppSystem.Collections.Generic.List<BaseInventoryItem> items = InventoryItems();
        if (items == null)
        {
            return;
        }
        for (int i = 0; i < items.Count; i++)
        {
            VehicleItem car = items[i]?.TryCast<VehicleItem>();
            if (car == null || !pending.TryGetValue(car.ItemId, out List<int> slots))
            {
                continue;
            }
            pending.Remove(car.ItemId);
            Il2CppSystem.Collections.Generic.List<uint> ids = car.ToolsetIDs;
            if (ids == null)
            {
                continue;
            }
            foreach (int slot in slots)
            {
                while (ids.Count <= slot)
                {
                    ids.Add(0u);
                }
                if (ids[slot] == 0u)
                {
                    ids[slot] = ItemId;
                    Mod.Log.Msg($"jerry can put back in tool slot {slot} of car {car.ItemId} (from the fuel file)");
                }
                else if (ids[slot] != ItemId)
                {
                    Mod.Log.Msg($"jerry can not put back in tool slot {slot} of car {car.ItemId}: slot holds tool {ids[slot]}");
                }
            }
        }
    }

    // Before the game writes its save: our id out of every car's tool slots,
    // remembered for Unstrip and recorded in the Store.
    private static void Strip()
    {
        Unstrip();
        Il2CppSystem.Collections.Generic.List<BaseInventoryItem> items = InventoryItems();
        if (items == null)
        {
            return;
        }
        // Every entry with our id, in case one was ever added twice.
        for (int i = items.Count - 1; i >= 0; i--)
        {
            BaseInventoryItem b = items[i];
            if (b != null && b.ItemId == ItemId)
            {
                strippedOwned ??= b.TryCast<InventoryItem>();
                items.RemoveAt(i);
            }
        }
        Dictionary<uint, List<int>> slots = new();
        for (int i = 0; i < items.Count; i++)
        {
            VehicleItem car = items[i]?.TryCast<VehicleItem>();
            Il2CppSystem.Collections.Generic.List<uint> ids = car != null ? car.ToolsetIDs : null;
            for (int s = 0; ids != null && s < ids.Count; s++)
            {
                if (ids[s] != ItemId)
                {
                    continue;
                }
                ids[s] = 0u;
                stripped.Add((car, s));
                if (!slots.TryGetValue(car.ItemId, out List<int> list))
                {
                    slots[car.ItemId] = list = new List<int>();
                }
                list.Add(s);
            }
        }
        // Only a save whose sidecar is loaded takes the slots: a pending load
        // that didn't reach the game yet still counts as fitted.
        foreach (KeyValuePair<uint, List<int>> kv in Store.PendingCanSlots)
        {
            if (!slots.ContainsKey(kv.Key))
            {
                slots[kv.Key] = new List<int>(kv.Value);
            }
        }
        Store.SetCanSlots(slots);
        if (Mod.DebugLog && stripped.Count > 0)
        {
            Mod.Log.Msg($"jerry can kept out of the game save ({stripped.Count} slot(s))");
        }
    }

    private static void Unstrip()
    {
        foreach ((VehicleItem car, int slot) in stripped)
        {
            Il2CppSystem.Collections.Generic.List<uint> ids = car != null ? car.ToolsetIDs : null;
            if (ids != null && slot < ids.Count && ids[slot] == 0u)
            {
                ids[slot] = ItemId;
            }
        }
        stripped.Clear();
        if (strippedOwned != null)
        {
            Il2CppSystem.Collections.Generic.List<BaseInventoryItem> items = InventoryItems();
            if (items != null && OwnedItem(items) == null)
            {
                items.Add(strippedOwned);
            }
            strippedOwned = null;
        }
    }

    [HarmonyPatch(typeof(OverlandSaver), nameof(OverlandSaver.SaveGame))]
    private static class SaveGamePatch
    {
        // Runs whether or not the mod is enabled, so a can fitted earlier
        // never reaches the save.
        private static void Prefix()
        {
            try
            {
                Strip();
            }
            catch (Exception ex)
            {
                Warn("save", ex);
            }
        }

        private static void Postfix()
        {
            try
            {
                Unstrip();
            }
            catch (Exception ex)
            {
                Warn("save", ex);
            }
        }
    }

    // At spawn FusionPlayer.Spawned reads the current car's ToolsetIDs and
    // sends them (RPC_SetEquippedItems, then RPC_RefillItems). Saved slots go
    // back here, no earlier, so they land in the loaded save's inventory.
    [HarmonyPatch(typeof(FusionPlayer), nameof(FusionPlayer.Spawned))]
    private static class PlayerSpawnedPatch
    {
        private static void Prefix()
        {
            Prepare(false);
        }
    }

    // In case the slots came back after the ids were read: our own car's
    // equipped list gets the can wherever its saved slots hold it.
    [HarmonyPatch(typeof(CarInventory), nameof(CarInventory.RPC_SetEquippedItems))]
    private static class SetEquippedPatch
    {
        private static void Prefix(CarInventory __instance, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<uint> toolsetIDs)
        {
            Prepare(false);
            try
            {
                if (item == null || __instance == null || toolsetIDs == null || !__instance.HasInputAuthority)
                {
                    return;
                }
                VehicleItem car = InventoryUtility.GetCurrentVehicle()?.TryCast<VehicleItem>();
                Il2CppSystem.Collections.Generic.List<uint> ids = car != null ? car.ToolsetIDs : null;
                for (int i = 0; ids != null && i < ids.Count && i < toolsetIDs.Length; i++)
                {
                    if (ids[i] == ItemId && toolsetIDs[i] == 0u)
                    {
                        toolsetIDs[i] = ItemId;
                        Mod.Log.Msg($"jerry can added to tool slot {i} as the car's tools were sent");
                    }
                }
            }
            catch (Exception ex)
            {
                Warn("equip", ex);
            }
        }
    }

    // Also read from ToolsetIDs here (merchant, car changes).
    [HarmonyPatch(typeof(CarInventory), nameof(CarInventory.Initialize))]
    private static class CarInventoryInitializePatch
    {
        private static void Prefix()
        {
            Prepare(false);
        }
    }

    [HarmonyPatch(typeof(StateMerchantMenu), nameof(StateMerchantMenu.InitializeToolsScreen))]
    private static class ToolsScreenPatch
    {
        private static void Prefix()
        {
            Prepare(true);
        }
    }

    // The toolbox finds what a tool does in usableItemOperations.
    [HarmonyPatch(typeof(ContextUIAvailableToolsPanel), nameof(ContextUIAvailableToolsPanel.Initialize))]
    private static class ToolsPanelPatch
    {
        private static void Prefix(ContextUIAvailableToolsPanel __instance)
        {
            Prepare(false);
            if (item == null || usable != true || __instance == null)
            {
                return;
            }
            try
            {
                Il2CppSystem.Collections.Generic.List<ContextUIAvailableToolsPanel.UsableItemOperationCouple> ops = __instance.usableItemOperations;
                if (ops == null)
                {
                    return;
                }
                for (int i = 0; i < ops.Count; i++)
                {
                    if (ops[i] != null && ops[i].ScriptableObject != null && ops[i].ScriptableObject.ItemId == ItemId)
                    {
                        return;
                    }
                }
                ops.Add(new ContextUIAvailableToolsPanel.UsableItemOperationCouple
                {
                    ScriptableObject = item,
                    Operation = ContextUIAvailableToolsPanel.UsableItemsOperation.OpenRepairingUI,
                });
            }
            catch (Exception ex)
            {
                Warn("toolbox", ex);
            }
        }
    }

    // The item's name and description are empty LocalizedStrings; these
    // answer for them. Every other string goes to the game.
    [HarmonyPatch(typeof(LocalizedString), nameof(LocalizedString.GetLocalizedString), new Type[0])]
    private static class LocalizePatch
    {
        private static bool Prefix(LocalizedString __instance, ref string __result)
        {
            if (__instance == null || item == null)
            {
                return true;
            }
            if (title != null && __instance.Pointer == title.Pointer)
            {
                __result = Title;
                return false;
            }
            if (blurb != null && __instance.Pointer == blurb.Pointer)
            {
                __result = Blurb;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(StateCarRepair), nameof(StateCarRepair.HealPlayer))]
    private static class HealPlayerPatch
    {
        private static bool Prefix(StateCarRepair __instance, Il2CppFusion.PlayerRef playerToHeal, CarDamageSystem carDamageSystem)
        {
            if (__instance == null || __instance.repairKitID != ItemId)
            {
                return true;
            }
            // Never let the game run its repair with our id, whatever happens.
            try
            {
                Use(playerToHeal, carDamageSystem);
            }
            catch (Exception ex)
            {
                Mod.Log.Warning("Jerry can use failed: " + ex.Message);
            }
            // What the picker's back action runs (StateUIPoppableState.PopState
            // calls it); GameStateManager.PopState from here throws.
            try
            {
                __instance.ClosingCallback();
            }
            catch (Exception ex)
            {
                Warn("closing the car picker", ex);
            }
            return false;
        }
    }

    // The picker lists every car in range; only the local car can be refuelled
    // here, so the other buttons are disabled. The game re-enables them the
    // next time it builds the picker for a real repair kit.
    [HarmonyPatch(typeof(StateCarRepair), nameof(StateCarRepair.SetupCarRepairButtons))]
    private static class SetupButtonsPatch
    {
        private static void Postfix(StateCarRepair __instance)
        {
            if (__instance == null || __instance.repairKitID != ItemId)
            {
                return;
            }
            try
            {
                SetupPicker(__instance);
            }
            catch (Exception ex)
            {
                Warn("picker", ex);
            }
            try
            {
                Relabel(__instance);
            }
            catch (Exception ex)
            {
                Warn("picker label", ex);
            }
        }
    }

    [HarmonyPatch(typeof(StateCarRepair), nameof(StateCarRepair.Close))]
    private static class PickerClosePatch
    {
        private static void Postfix()
        {
            try
            {
                RestoreLabels();
                RestoreButton();
            }
            catch (Exception ex)
            {
                Warn("picker label", ex);
            }
        }
    }

    // Texts changed to "Refill fuel" while our picker is open, with what they said.
    private static readonly List<(TMP_Text Text, string Was)> relabelled = new();

    private const string Label = "Refill fuel";

    // The picker's prompt says "Repair vehicle": any visible text saying
    // "repair" under the picker, else anywhere on screen (the input prompt
    // bar may live elsewhere), says "Refill fuel" until the picker closes.
    private static void Relabel(StateCarRepair state)
    {
        RestoreLabels();
        ContextUICarRepair ui = state.contextUICarRepair;
        IEnumerable<TMP_Text> texts = ui != null ? ui.GetComponentsInChildren<TMP_Text>(false) : null;
        if (texts == null || !Swap(texts))
        {
            Swap(Object.FindObjectsOfType<TMP_Text>());
        }
        if (relabelled.Count == 0)
        {
            Mod.Log.Msg("jerry can picker: no 'repair' prompt found to relabel");
        }
    }

    private static bool Swap(IEnumerable<TMP_Text> texts)
    {
        bool any = false;
        foreach (TMP_Text t in texts)
        {
            if (t == null || !t.isActiveAndEnabled)
            {
                continue;
            }
            string was = t.text;
            if (string.IsNullOrEmpty(was) || was.IndexOf("repair", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            relabelled.Add((t, was));
            t.text = Label;
            any = true;
            if (Mod.DebugLog)
            {
                Mod.Log.Msg($"jerry can picker: '{t.name}' text '{was}' -> '{Label}'");
            }
        }
        return any;
    }

    private static void RestoreLabels()
    {
        foreach ((TMP_Text t, string was) in relabelled)
        {
            if (t != null && t.text == Label)
            {
                t.text = was;
            }
        }
        relabelled.Clear();
    }

    // Each button follows its car through WorldTrackingRectTransform.
    // transformToTrack, set to that player's object transform.
    // Only cars whose fuel this machine knows can be refuelled: our own, and
    // those of modded players (Net status). Each of those buttons shows its
    // fuel; the others are disabled. The game re-enables them the next time
    // it builds the picker for a real repair kit.
    private static void SetupPicker(StateCarRepair state)
    {
        RestoreButton();
        OverlandNetworkRunner onr = OverlandNetworkRunner.Instance;
        Il2CppFusion.NetworkRunner runner = onr != null ? onr.NetworkRunner : null;
        ContextUICarRepair ui = state.contextUICarRepair;
        if (onr == null || ui == null || ui.CarRepairButtons == null)
        {
            return;
        }
        // Each button follows its car through WorldTrackingRectTransform.
        // transformToTrack, set to that player's object transform.
        Dictionary<IntPtr, float> known = new();
        Il2CppFusion.NetworkObject localObj = onr.LocalPlayerNetworkObject;
        Tank t = Tank.Local;
        if (localObj != null && t != null && t.Capacity > 0f)
        {
            known[localObj.transform.Pointer] = t.Empty ? 0f : Mathf.Clamp01(t.Fuel / t.Capacity);
        }
        foreach (Il2CppFusion.PlayerRef p in Net.KnownPlayers())
        {
            if (runner != null && runner.TryGetPlayerObject(p, out Il2CppFusion.NetworkObject obj) && obj != null && Net.TryFuel(p, out float f))
            {
                known[obj.transform.Pointer] = f;
            }
        }
        int shown = 0, disabled = 0;
        Il2CppSystem.Collections.Generic.List<ContextUICarHealthStatusButton> buttons = ui.CarRepairButtons;
        for (int i = 0; i < buttons.Count; i++)
        {
            ContextUICarHealthStatusButton b = buttons[i];
            if (b == null || b.Button == null || !b.gameObject.activeSelf || b.WorldTrackingRectTransform == null)
            {
                continue;
            }
            Transform tracked = b.WorldTrackingRectTransform.transformToTrack;
            if (tracked != null && known.TryGetValue(tracked.Pointer, out float frac))
            {
                ShowFuel(b, frac);
                shown++;
                continue;
            }
            if (b.Button.interactable)
            {
                b.Button.interactable = false;
                disabled++;
            }
        }
        // SetupCarRepairButtons may run more than once per picker; log once.
        if (state.Pointer != lastPicker)
        {
            lastPicker = state.Pointer;
            Mod.Log.Msg($"jerry can picker: {shown} car(s) with known fuel, {disabled} disabled (driver not running the fuel mod, or no status yet)");
        }
    }

    // The picker button shows the car's damage (a round fill with a wrench).
    // For the can, each refuellable car's button shows its fuel level in
    // green (red when low) with the jerry-can icon. Restored when the picker
    // closes.
    private sealed class ImageState
    {
        public Image Image;
        public Sprite Sprite;
        public Color Color;
        public Image.Type Type;
        public Image.FillMethod Method;
        public int Origin;
        public float Amount;
        public bool Aspect;
    }

    private static readonly List<ImageState> changedImages = new();



    private static void Remember(Image img)
    {
        if (img == null || changedImages.Exists(x => x.Image.Pointer == img.Pointer))
        {
            return;
        }
        changedImages.Add(new ImageState
        {
            Image = img,
            Sprite = img.sprite,
            Color = img.color,
            Type = img.type,
            Method = img.fillMethod,
            Origin = img.fillOrigin,
            Amount = img.fillAmount,
            Aspect = img.preserveAspect,
        });
    }

    private static void ShowFuel(ContextUICarHealthStatusButton b, float frac)
    {
        ContextUIFill fill = b != null ? b.HealthFill : null;
        Sprite icon = FuelBar.LoadIcon();
        if (fill == null || fill.fillImage == null || icon == null)
        {
            return;
        }
        // The button (from the log): FillCircle_Mask holds the background
        // (with a blurred backdrop, the dark see-through look) and the fill
        // (FillCircle_Fill); the wrench is a separate 'Icon' image drawn over
        // them. The background stays as the game made it; the fill shows the
        // fuel level and the wrench becomes the jerry can.
        Image fg = fill.fillImage;
        Remember(fg);
        if (fg.type != Image.Type.Filled)
        {
            fg.type = Image.Type.Filled;
            fg.fillMethod = Image.FillMethod.Vertical;
            fg.fillOrigin = 0;
        }
        fg.fillAmount = frac;
        fg.color = frac < Mod.LowFuelThreshold ? FuelBar.Low : FuelBar.Normal;
        bool iconSet = false;
        foreach (Image img in b.GetComponentsInChildren<Image>(true))
        {
            if (img == null || img.sprite == null || img.Pointer == fg.Pointer)
            {
                continue;
            }
            if (img.name == "Icon" || img.sprite.name.IndexOf("repair", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Remember(img);
                img.sprite = icon;
                img.preserveAspect = true;
                img.color = Color.white;
                iconSet = true;
            }
        }
        if (Mod.DebugLog)
        {
            Mod.Log.Msg($"jerry can picker: '{b.name}' shows fuel {Mod.F(frac * 100f)}% ({(iconSet ? "icon replaced" : "no icon found")}, fill {fg.type}/{fg.fillMethod})");
        }
    }

    private static void RestoreButton()
    {
        foreach (ImageState st in changedImages)
        {
            Image img = st.Image;
            if (img == null)
            {
                continue;
            }
            img.sprite = st.Sprite;
            img.type = st.Type;
            img.fillMethod = st.Method;
            img.fillOrigin = st.Origin;
            img.fillAmount = st.Amount;
            img.color = st.Color;
            img.preserveAspect = st.Aspect;
        }
        changedImages.Clear();
    }

    private static void Use(Il2CppFusion.PlayerRef player, CarDamageSystem target)
    {
        Tank t = Tank.Local;
        NetworkedVehicleStatus nvs = target != null ? target.vehicleStatus : null;
        string name = nvs != null ? nvs.name : "?";
        float litres = CarSpecs.JerryCanLitres;
        CarInventory mine = t != null ? t.Inventory : null;
        if (mine == null)
        {
            OverlandNetworkRunner onr = OverlandNetworkRunner.Instance;
            mine = onr != null ? onr.PlayerCarInventory(Net.LocalPlayer) : null;
        }
        if (mine == null)
        {
            Mod.Log.Warning("jerry can: no toolbox found for the local player; nothing done");
            return;
        }
        if (t != null && nvs != null && nvs.Pointer == t.Status.Pointer)
        {
            if (!t.Empty && t.Fuel >= t.Capacity - 0.001f)
            {
                Mod.Log.Msg($"jerry can: '{t.Name}' tank already full; can kept");
                return;
            }
            t.AddFuel(litres, "jerry can");
            mine.AskForItemConsumption(ItemId);
            Mod.Log.Msg($"jerry can used on '{t.Name}'");
            return;
        }
        // Another player's car: its fuel lives on their machine.
        if (!Net.TryFuel(player, out float frac))
        {
            Mod.Log.Msg($"jerry can: can't refuel '{name}' (player {player.PlayerId}): no fuel status from them (not running the fuel mod?); can kept");
            return;
        }
        if (frac >= 0.999f)
        {
            Mod.Log.Msg($"jerry can: '{name}' tank already full; can kept");
            return;
        }
        if (!Net.SendRefuel(player, litres))
        {
            Mod.Log.Msg($"jerry can: couldn't reach player {player.PlayerId}; can kept");
            return;
        }
        mine.AskForItemConsumption(ItemId);
        Mod.Log.Msg($"jerry can used on '{name}' (player {player.PlayerId})");
    }

    // Once per distinct failure, so a second, different problem still shows.
    private static void Warn(string where, Exception ex)
    {
        if (warned.Add(where + ": " + ex.GetType().Name + ": " + ex.Message))
        {
            Mod.Log.Warning($"Jerry can ({where}) failed: " + ex);
        }
    }
}
