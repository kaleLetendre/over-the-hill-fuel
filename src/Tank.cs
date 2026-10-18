using System.Collections.Generic;
using Il2Cpp;
using Il2CppFusion;
using Il2CppVehiclePhysics;
using UnityEngine;

namespace OTH.Fuel;

// Fuel state and engine control for one car. Only the local player's car is
// tracked (Tank.Local); every other car is just described in the log.
//
// References are looked up once when the car is adopted, so the per-frame tick
// only reads a few properties.
//
// Saving: once the car's CarInfo is known, its level is
// restored from the save's sidecar (Store), or start full if nothing is saved.
// A saved empty tank starts empty and blocked. The level goes back into the
// Store whenever the game saves and when the car stops being tracked, so each
// car keeps its own level across car swaps.
//
// Start blocks: EngineStartBlocked is shared with the flooding system, so we
// remember whether *we* set it (ownBlock) and only ever clear it then. A
// refuel while flooded waits (pendingUnblock) until the engine is dry.
internal sealed class Tank
{
    internal static Tank Local;

    public readonly NetworkedVehicleStatus Status;

    public readonly CarIgnition Ignition;

    public readonly EngineFloodingSystem Flooding;

    public readonly CarInventory Inventory;

    public readonly VPVehicleController Vehicle;

    public readonly string Name;

    // CarInfo.ItemId and asset name once known (0 / null until then); the
    // save key for this car.
    public uint ItemId;

    public string CarName;

    // The save (Store.Profile) this car's level was restored from.
    public string Profile;

    public float Capacity = CarSpecs.DefaultCapacity;

    public float Fuel = CarSpecs.DefaultCapacity;

    public bool Empty;

    // The car's CarInfo can arrive after the car spawns; until then the
    // default capacity is used and the lookup is retried.
    private bool specResolved;

    // A toolbox refill (fuel / can) that came before the car model was known;
    // re-applied over the saved level once it is (Restore).
    private bool refilledEarly;

    private float nextSpecTry;

    private int specTries;

    private readonly VehicleCustomizationController customization;

    // When to check next whether the car model changed under us.
    private float nextCarCheck;

    // We set EngineStartBlocked, so we may clear it.
    private bool ownBlock;

    // Refuelled while flooded: clear our block once the engine is dry.
    private bool pendingUnblock;

    // We set Engine.allowedFuelRatio to 0, so we may restore it.
    private bool ownFuelCut;

    // How often something reset allowedFuelRatio while we held it at 0.
    private int fuelCutOverwrites;

    private bool warnedNoAuthority;

    private float nextRefusalLog;

    private int blockCount;

    private float nextStop;

    private float nextEnforceTrace;

    // Burn statistics for the DebugLog line.
    private const float BurnLogInterval = 30f;

    private float burnLogAt;

    private float burnLitres;

    private float burnSeconds;

    private float runSeconds;

    private string burnSource = "?";

    private bool sawRate;

    private bool warnedNoRate;

    private Tank(NetworkedVehicleStatus status)
    {
        Status = status;
        Name = status.name;
        Ignition = Find<CarIgnition>(status);
        Flooding = Find<EngineFloodingSystem>(status);
        Vehicle = status.vehicle?.TryCast<VPVehicleController>() ?? Find<VPVehicleController>(status);
        Inventory = Find<CarInventory>(status);
        customization = Find<VehicleCustomizationController>(status);
        if (Inventory == null)
        {
            // The inventory may live on the player object rather than the car.
            FusionPlayer player = status.vehicle != null ? status.vehicle.CachedFusionPlayer : null;
            Inventory = player != null ? player.CarInventory : null;
        }
    }

    // Fusion's networked properties throw once the car is despawned.
    public bool Alive
    {
        get
        {
            if (Status == null || Ignition == null)
            {
                return false;
            }
            NetworkObject obj = Status.Object;
            return obj != null && obj.IsValid;
        }
    }

    // Called from the HUD setup (the car the HUD follows) and, as a fallback,
    // from the spawn of a car this client has input authority over. Never
    // tracks another player's car.
    internal static void Adopt(NetworkedVehicleStatus nvs, string why)
    {
        if (nvs == null || (Local != null && Local.Alive && Local.Status.Pointer == nvs.Pointer))
        {
            return;
        }
        if (!nvs.HasInputAuthority)
        {
            Mod.Log.Msg($"'{nvs.name}' ({why}): no input authority here, not tracked");
            return;
        }
        Tank t = new(nvs);
        if (t.Ignition == null)
        {
            Mod.Log.Warning($"'{t.Name}': no CarIgnition found; fuel disabled for this car");
            return;
        }
        if (Local != null)
        {
            Local.Release("replaced by '" + t.Name + "'");
        }
        Local = t;
        Mod.Log.Msg($"tracking '{t.Name}' ({why}): state={nvs.HasStateAuthority} input={nvs.HasInputAuthority}, "
            + $"flooding={(t.Flooding != null)} inventory={(t.Inventory != null)} vpp={(t.Vehicle != null)}, save '{Store.Profile ?? "none"}'");
        t.ResolveSpec(Time.unscaledTime);
        if (t.Inventory == null)
        {
            Mod.Log.Warning($"'{t.Name}': NO CarInventory found on the car or its FusionPlayer; toolbox refills will NOT refuel this car");
        }
        t.LogEngineFuelSettings();
    }

    // The car is about to despawn (called from a prefix, so its state is still
    // valid): store its level, undo our engine changes, in case Fusion pools
    // and reuses the object, then drop it.
    internal static void Despawned(NetworkedVehicleStatus nvs)
    {
        pending.RemoveAll(p => p.nvs == null || p.nvs.Pointer == nvs.Pointer);
        if (Local != null && Local.Status != null && Local.Status.Pointer == nvs.Pointer)
        {
            Mod.Log.Msg($"'{Local.Name}' despawned; fuel tracking stopped");
            Local.Release("despawned");
            Local = null;
            lastDespawnAt = Time.unscaledTime;
            // Quitting to the menu may not save the game; write the level
            // now so loading again can't give back burnt fuel.
            SavePatches.Save("car despawned");
        }
    }

    // Undo our own engine changes (fuel cut, start block) on a car we stop
    // tracking while it still exists. Leaves blocks set by the game alone.
    internal void Release(string why)
    {
        // Keep this car's level for when it comes back (car swap, respawn).
        Store.Put(this);
        pendingUnblock = false;
        if (!Alive)
        {
            return;
        }
        try
        {
            Engine e = Vehicle != null ? Vehicle.m_engine : null;
            if (ownFuelCut && e != null && e.allowedFuelRatio == 0f)
            {
                e.allowedFuelRatio = 1f;
            }
            ownFuelCut = false;
            if (ownBlock && Status.HasStateAuthority && Ignition.EngineStartBlocked)
            {
                Ignition.EngineStartBlocked = false;
            }
            ownBlock = false;
            Mod.Log.Msg($"'{Name}': released ({why})");
        }
        catch (System.Exception ex)
        {
            Mod.Log.Warning($"'{Name}': release failed: {ex.Message}");
        }
    }

    internal static T Find<T>(Component c) where T : Component
    {
        T r = c.GetComponent<T>();
        if (r == null)
        {
            r = c.GetComponentInChildren<T>(true);
        }
        if (r == null)
        {
            r = c.GetComponentInParent<T>();
        }
        return r;
    }

    internal static CarInfo CarInfoOf(Component c)
    {
        VehicleCustomizationController vcc = Find<VehicleCustomizationController>(c);
        CarInfo info = vcc != null ? vcc.CurrentCarInfo : null;
        if (info == null)
        {
            Odometer odo = Find<Odometer>(c);
            info = odo != null ? odo.carInfo : null;
        }
        return info;
    }

    private void ResolveSpec(float now)
    {
        if (specResolved || now < nextSpecTry)
        {
            return;
        }
        nextSpecTry = now + 1f;
        CarInfo info = CarInfoOf(Status);
        if (info == null && ++specTries < 10)
        {
            return;
        }
        specResolved = true;
        nextCarCheck = now + 1f;
        Restore(info);
    }

    // Sets the capacity for this car model and its saved level (a full tank
    // if nothing is saved for it).
    private void Restore(CarInfo info)
    {
        (float cap, bool listed) = CarSpecs.Capacity(info != null ? info.name : null);
        Capacity = cap;
        ItemId = info != null ? info.ItemId : 0u;
        CarName = info != null ? info.name : null;
        Profile = Store.Profile;
        Mod.Log.Msg($"'{Name}': CarInfo '{CarName ?? "none"}' id {ItemId}, capacity {Mod.F(Capacity)} L ({(listed ? "table" : "default")})");
        Store.Entry saved = Store.Get(ItemId);
        bool early = refilledEarly;
        refilledEarly = false;
        if (saved == null)
        {
            string why = ItemId == 0 ? "no CarInfo, not saved" : Profile == null ? "no save selected, not saved" : "nothing saved for this car";
            SetFuel(Capacity, $"full tank ({why})");
            return;
        }
        SetFuel(saved.Fraction * Capacity, $"restored from save '{Profile}' ({Mod.F(saved.Fraction * 100f)}%)");
        if (early)
        {
            Refill("toolbox refill from before the car model was known");
        }
        ApplyEarlyRefill();
    }

    // Another save became current while this car is tracked (Store.Select).
    // A different save restores this car from it; the same save keeps the
    // live level, which is newer than the file.
    internal void Reselect()
    {
        if (!specResolved || Profile == Store.Profile)
        {
            return;
        }
        Mod.Log.Msg($"'{Name}': save changed from '{Profile ?? "none"}' to '{Store.Profile ?? "none"}'");
        Restore(customization != null && customization.CurrentCarInfo != null ? customization.CurrentCarInfo : CarInfoOf(Status));
    }

    // The respawn window must not survive into a newly loaded save.
    internal static void ResetRespawnWindow()
    {
        earlyRefillAt = -100f;
        lastDespawnAt = -100f;
    }

    // The tracked car vanished without a despawn call (Mod.OnUpdate): treat
    // it like a despawn.
    internal static void Lost(Tank t)
    {
        Store.Put(t);
        lastDespawnAt = Time.unscaledTime;
        if (Local == t)
        {
            Local = null;
        }
        SavePatches.Save("car gone");
    }

    // A respawn may replace the car object, and the toolbox refill can reach
    // the new car before it is tracked. Without this, a car that was empty
    // would come back empty with no way out. Applied only shortly after our
    // previous car despawned, so a refill at session start (if the game does
    // one) can't top up a loaded tank.
    private static float earlyRefillAt = -100f;

    private static float lastDespawnAt = -100f;

    private static string earlyRefillContext;

    internal static void EarlyRefill(string context)
    {
        earlyRefillAt = Time.unscaledTime;
        earlyRefillContext = context;
        bool after = earlyRefillAt - lastDespawnAt < 15f;
        Mod.Log.Msg($"RPC_RefillItems before our car is tracked ({(after ? "car despawned " + Mod.F(earlyRefillAt - lastDespawnAt) + " s ago, will apply" : "no recent despawn, ignored")}); context: {context}");
    }

    private void ApplyEarlyRefill()
    {
        float now = Time.unscaledTime;
        if (now - earlyRefillAt > 10f || earlyRefillAt - lastDespawnAt > 15f || earlyRefillAt < lastDespawnAt)
        {
            return;
        }
        earlyRefillAt = -100f;
        Refill("toolbox refill that arrived before tracking; " + earlyRefillContext);
    }

    // The car model can change on the same network object (car swap at the
    // garage); then the old model's level is stored and the new one's restored.
    private void CheckCarChange(float now)
    {
        if (!specResolved || now < nextCarCheck || customization == null)
        {
            return;
        }
        nextCarCheck = now + 1f;
        CarInfo info = customization.CurrentCarInfo;
        if (info == null || info.ItemId == ItemId)
        {
            return;
        }
        Mod.Log.Msg($"'{Name}': car changed from '{CarName ?? "none"}' to '{info.name}'");
        Store.Put(this);
        Restore(info);
    }

    // VPP engine settings that define the fuel units; logged once per car.
    private void LogEngineFuelSettings()
    {
        Engine e = Vehicle != null ? Vehicle.m_engine : null;
        Engine.Settings s = e?.settings;
        if (s == null)
        {
            Mod.Log.Msg($"'{Name}': no VPP engine found; burn uses NetworkedVehicleStatus.EngineFuelRate");
            return;
        }
        float density = Density(s);
        float peakLpm = e.GetMaxFuelRate(s.peakRpm) / density * 60f;
        Mod.Log.Msg($"'{Name}' engine: maxFuelPerRev {Mod.F(s.maxFuelPerRev)}, fuelDensity {Mod.F(s.fuelDensity)}, "
            + $"correction {Mod.F(s.fuelConsumptionCorrection)}, idle {Mod.F(s.idleRpm)} / peak {Mod.F(s.peakRpm)} rpm, "
            + $"max rate at peak {Mod.F(e.GetMaxFuelRate(s.peakRpm))} (= {Mod.F(peakLpm)} L/min if g/s and g/L)");
    }

    private static float Density(Engine.Settings s)
    {
        // Gasoline, in VPP's g/L, if the setting is missing or bogus.
        return s != null && float.IsFinite(s.fuelDensity) && s.fuelDensity > 0f ? s.fuelDensity : 0.745f;
    }

    // Finite and non-negative, else 0 (Mathf.Max lets NaN through).
    internal static float Sane(float x)
    {
        return float.IsFinite(x) && x > 0f ? x : 0f;
    }

    // Fuel burn in litres per second, before FuelScale.
    //
    // Units (assumed from VPP field names; check the logged settings): Engine.
    // sensorFuelRate is fuel mass per second (g/s) and Engine.Settings.fuelDensity
    // is in kg/L (the game logs 0.745 for gasoline), so rate / density is mL/s;
    // divide by 1000 for L/s.
    // sensorFuelRate already depends on rpm, load and idle, and is 0 when the
    // engine is off.
    private float BurnLitresPerSecond(out string source)
    {
        Engine e = Vehicle != null ? Vehicle.m_engine : null;
        if (e != null)
        {
            source = "engine";
            return Sane(e.sensorFuelRate / Density(e.settings) / 1000f);
        }
        source = "status";
        Engine.Settings s = Vehicle != null ? Vehicle.engine : null;
        return Sane(Status.EngineFuelRate / Density(s) / 1000f);
    }

    // Used only if VPP reports no fuel rate at all for a running engine: a rough
    // load and rpm model that gives about 0.1 L/min in normal driving.
    private float FallbackLitresPerSecond()
    {
        Engine e = Vehicle != null ? Vehicle.m_engine : null;
        if (e == null)
        {
            return 0.08f / 60f;
        }
        float peak = e.settings != null && e.settings.peakRpm > 0f ? e.settings.peakRpm : 5000f;
        float rpmFrac = Mathf.Clamp01(Sane(e.sensorRpm) / peak);
        return (0.03f + 0.6f * Mathf.Clamp01(Sane(e.sensorLoad)) * rpmFrac) / 60f;
    }

    public void Tick(float dt, float now)
    {
        ResolveSpec(now);
        CheckCarChange(now);
        if (Empty)
        {
            Enforce(null);
            return;
        }
        TrackBlockOwnership();
        if (pendingUnblock)
        {
            TryPendingUnblock();
        }
        // The first test never saw CarIgnition.EngineRunning go true, so also
        // accept VPP's own "engine working" sensor.
        if (EngineRunning() && dt > 0f)
        {
            float lps = BurnLitresPerSecond(out string source);
            runSeconds += dt;
            sawRate |= lps > 0f;
            // Zero now and then is normal (overrun fuel cut); zero for the first
            // 10 s of running means VPP's fuel model isn't active on this car.
            if (!sawRate && !warnedNoRate && runSeconds > 10f)
            {
                warnedNoRate = true;
                Mod.Log.Warning($"'{Name}': VPP reports no fuel rate while running; using the fallback load/rpm model");
            }
            if (warnedNoRate && lps <= 0f)
            {
                lps = FallbackLitresPerSecond();
                source = "fallback";
            }
            float used = Sane(lps * Mod.FuelScale * dt);
            Fuel -= used;
            burnLitres += used;
            burnSeconds += dt;
            burnSource = source;
            if (Fuel <= 0f)
            {
                GoEmpty();
                return;
            }
        }
        if (Mod.DebugLog && now >= burnLogAt)
        {
            LogBurn(now);
        }
    }

    // While fuelled, our block only survives until someone else clears it.
    private void TrackBlockOwnership()
    {
        if (ownBlock && !Ignition.EngineStartBlocked)
        {
            ownBlock = false;
            if (pendingUnblock)
            {
                pendingUnblock = false;
                Mod.Log.Msg($"'{Name}': start block cleared by the game while waiting for the engine to dry");
            }
        }
    }

    private void TryPendingUnblock()
    {
        if (!ownBlock)
        {
            pendingUnblock = false;
            return;
        }
        if (Flooding != null && Flooding.IsFlooded)
        {
            return;
        }
        pendingUnblock = false;
        ClearOwnBlock("engine dry after refuel");
    }

    private void ClearOwnBlock(string why)
    {
        if (ownBlock && Status.HasStateAuthority && Ignition.EngineStartBlocked)
        {
            Ignition.EngineStartBlocked = false;
            Mod.Log.Msg($"'{Name}': engine start unblocked ({why})");
        }
        ownBlock = false;
    }

    private bool EngineRunning()
    {
        Engine e = Vehicle != null ? Vehicle.m_engine : null;
        return Ignition.EngineRunning || (e != null && e.sensorWorking);
    }

    private void LogBurn(float now)
    {
        if (burnLogAt > 0f && burnSeconds <= 0f)
        {
            Engine e = Vehicle != null ? Vehicle.m_engine : null;
            Mod.Log.Msg($"'{Name}' no burn in the last {Mod.F(BurnLogInterval)} s: ignition running={(bool)Ignition.EngineRunning}, "
                + $"status running={Status.EngineRunning}, vpp working={(e != null && e.sensorWorking)} rate {Mod.F(e != null ? e.sensorFuelRate : 0f)} rpm {Mod.F(e != null ? e.sensorRpm : 0f)}");
        }
        if (burnLogAt > 0f && burnSeconds > 0f)
        {
            float lpm = burnLitres / burnSeconds * 60f;
            Engine e = Vehicle != null ? Vehicle.m_engine : null;
            string engine = e != null ? $", now {Mod.F(e.sensorRpm)} rpm load {Mod.F(e.sensorLoad)}" : "";
            Mod.Log.Msg($"'{Name}' burn {Mod.F(lpm)} L/min over {Mod.F(burnSeconds)} s running ({burnSource}, scale {Mod.F(Mod.FuelScale)}{engine}); "
                + $"fuel {Mod.F(Fuel)}/{Mod.F(Capacity)} L, full tank = {Mod.F(lpm > 0f ? Capacity / lpm : 0f)} min at this rate");
        }
        burnLogAt = now + BurnLogInterval;
        burnLitres = 0f;
        burnSeconds = 0f;
    }

    private void GoEmpty()
    {
        Fuel = 0f;
        Empty = true;
        pendingUnblock = false;
        bool flooded = Flooding != null && Flooding.IsFlooded;
        Mod.Log.Msg($"'{Name}' out of fuel: stopping engine and blocking restarts (flooded={flooded}, "
            + $"startBlocked={(bool)Ignition.EngineStartBlocked}, state={Status.HasStateAuthority})");
        Enforce(null);
    }

    // Keep an empty engine off: cut VPP's fuel, stop it, and set the game's
    // start block. Runs every frame and network tick while empty, and after any
    // flooding call that may have cleared the block. `after` names that call.
    //
    // Only the car's state authority enforces; elsewhere Fusion would overwrite
    // it, so that peer just refuses local starts (see Refuse).
    internal void Enforce(string after)
    {
        if (!Empty)
        {
            return;
        }
        if (!Status.HasStateAuthority)
        {
            if (!warnedNoAuthority)
            {
                warnedNoAuthority = true;
                Mod.Log.Msg($"'{Name}': no state authority on this peer; empty tank not enforced here, local starts refused only");
            }
            return;
        }
        Engine e = Vehicle != null ? Vehicle.m_engine : null;
        if (e != null && e.allowedFuelRatio != 0f)
        {
            if (ownFuelCut)
            {
                // Something reset it while we held it at 0.
                fuelCutOverwrites++;
            }
            e.allowedFuelRatio = 0f;
            ownFuelCut = true;
        }
        float now = Time.unscaledTime;
        if (Ignition.EngineRunning && now >= nextStop)
        {
            nextStop = now + 0.5f;
            Ignition.StopEngine();
        }
        if (!Ignition.EngineStartBlocked)
        {
            Ignition.EngineStartBlocked = true;
            ownBlock = true;
            // Something clearing the block every frame would flood the log.
            if (++blockCount <= 3 || blockCount % 100 == 0)
            {
                string what = after != null ? $"{after} cleared the start block; re-asserted" : "engine start blocked";
                Mod.Log.Msg($"'{Name}': {what} (tank empty, #{blockCount})");
            }
        }
        else if (after != null && Mod.DebugLog && now >= nextEnforceTrace)
        {
            nextEnforceTrace = now + 2f;
            Mod.Log.Msg($"'{Name}': after {after}, start block still set (tank empty, ours={ownBlock})");
        }
    }

    // True if StartEngine / ToggleEngine on this ignition must be refused.
    internal bool Refuse(CarIgnition ig, string what)
    {
        if (!Empty || ig == null || ig.Pointer != Ignition.Pointer)
        {
            return false;
        }
        float now = Time.unscaledTime;
        if (now >= nextRefusalLog)
        {
            nextRefusalLog = now + 2f;
            Mod.Log.Msg($"'{Name}': {what} refused, tank empty");
        }
        return true;
    }

    // Adds fuel, up to a full tank (the jerry can).
    internal void AddFuel(float litres, string cause)
    {
        SetFuel((Empty ? 0f : Fuel) + Sane(litres), $"{cause} (+{Mod.F(litres)} L)");
    }

    internal void Refill(string cause)
    {
        refilledEarly |= !specResolved;
        SetFuel(Capacity, $"refuelled ({cause})");
    }

    // Sets the level. 0 empties the tank (engine stopped and blocked); any
    // fuel in an empty tank lifts our fuel block.
    internal void SetFuel(float litres, string what)
    {
        float before = Empty ? 0f : Fuel;
        litres = Mathf.Clamp(Sane(litres), 0f, Capacity);
        Mod.Log.Msg($"'{Name}' {what}: {Mod.F(before)} -> {Mod.F(litres)} L");
        if (litres <= 0f)
        {
            if (!Empty)
            {
                GoEmpty();
            }
            return;
        }
        Fuel = litres;
        if (!Empty)
        {
            return;
        }
        Empty = false;
        Engine e = Vehicle != null ? Vehicle.m_engine : null;
        if (ownFuelCut)
        {
            Mod.Log.Msg($"'{Name}': allowedFuelRatio " + (fuelCutOverwrites == 0
                ? "stayed at 0 while empty"
                : $"was reset {fuelCutOverwrites} time(s) while empty (the game or VPP writes it)"));
            if (e != null && e.allowedFuelRatio == 0f)
            {
                e.allowedFuelRatio = 1f;
            }
        }
        ownFuelCut = false;
        fuelCutOverwrites = 0;
        if (!ownBlock)
        {
            Mod.Log.Msg($"'{Name}': fuel block cleared; the start block is not ours, left as is "
                + $"(startBlocked={(bool)Ignition.EngineStartBlocked}, flooded={Flooding != null && Flooding.IsFlooded})");
            return;
        }
        if (Flooding != null && Flooding.IsFlooded)
        {
            pendingUnblock = true;
            Mod.Log.Msg($"'{Name}': fuel block cleared; engine still flooded, start block stays until it is dry");
            return;
        }
        ClearOwnBlock("refuelled");
    }

    internal bool OwnsInventory(CarInventory inv)
    {
        return inv != null && Inventory != null && inv.Pointer == Inventory.Pointer;
    }

    internal bool OwnsFlooding(EngineFloodingSystem fs)
    {
        return fs != null && Flooding != null && fs.Pointer == Flooding.Pointer;
    }

    internal bool OwnsIgnition(CarIgnition ig)
    {
        return ig != null && ig.Pointer == Ignition.Pointer;
    }

    // True if this player is the one driving our car.
    internal bool IsDriver(PlayerRef player)
    {
        NetworkObject obj = Status.Object;
        return obj != null && obj.IsValid && obj.InputAuthority == player;
    }

    // Cars seen spawning, described once their CarInfo is known.
    private static readonly List<(NetworkedVehicleStatus nvs, int tries)> pending = new();

    private static float nextDescribe;

    internal static void Seen(NetworkedVehicleStatus nvs)
    {
        pending.Add((nvs, 0));
    }

    internal static void DescribePending(float now)
    {
        if (pending.Count == 0 || now < nextDescribe)
        {
            return;
        }
        nextDescribe = now + 1f;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            (NetworkedVehicleStatus nvs, int tries) = pending[i];
            if (nvs == null || nvs.Object == null || !nvs.Object.IsValid)
            {
                pending.RemoveAt(i);
                continue;
            }
            CarInfo info = CarInfoOf(nvs);
            if (info == null && tries < 10)
            {
                pending[i] = (nvs, tries + 1);
                continue;
            }
            pending.RemoveAt(i);
            bool local = Local != null && Local.Status.Pointer == nvs.Pointer;
            Mod.Log.Msg($"car '{nvs.name}': CarInfo '{(info != null ? info.name : "none")}' id {(info != null ? info.ItemId : 0)}, "
                + $"state={nvs.HasStateAuthority} input={nvs.HasInputAuthority} proxy={nvs.IsProxy}, local={local}, "
                + $"capacity {Mod.F(CarSpecs.Capacity(info != null ? info.name : null).capacity)} L");
            if (Local == null && nvs.HasInputAuthority)
            {
                Adopt(nvs, "input authority");
            }
        }
    }

    internal static void ClearPending()
    {
        pending.Clear();
    }
}
