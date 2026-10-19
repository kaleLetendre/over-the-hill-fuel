using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader.NativeUtils;
using Il2Cpp;
using Il2CppFusion;
using Il2CppFusion.Sockets;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace OTH.Fuel;

// Fuel messages between players, over Fusion reliable data with our own key.
//
// Fuel lives with each car's driver (Tank.Local on their machine), so two
// things travel between players:
// - Status: every modded player sends their tank fraction every 2 s. It is
//   how the others know the player runs the mod, and what the jerry can
//   picker shows on that player's car.
// - Refuel: a jerry can used on another player's car sends its litres to
//   that car's driver, who adds them to their own tank.
//
// Routing: the game runs as host and clients. A client sends to the host; the
// host applies what is for itself and forwards the rest (status to every
// other modded player, refuel to its target). The host only trusts the origin
// a client claims if it is that client.
//
// Unmodded players: the game's OnReliableDataReceived throws on unknown keys,
// so nothing is ever sent to a player who hasn't sent us a status first
// (the host learns who is modded from their status messages). Everyone in a
// session needs the mod anyway: an equipped jerry can is in the car's
// networked tool slots.
internal static class Net
{
    // "OTHF", "uel!": our ReliableKey; the game uses 1 and 2 in key0.
    private const int Key0 = 0x4F544846;

    private const int Key1 = 0x75656C21;

    private const int Status = 1;

    private const int Refuel = 2;

    // Host to itself once per session: checks send and receive work.
    private const int Ping = 3;

    // Diagnostics (always logged, a few times only): what was sent and received.
    private static int receivedLogged;

    private static bool sentLogged;

    private static bool pinged;

    private static string lastSkip;

    private const int Version = 1;

    private const float StatusInterval = 2f;

    // A player whose last status is older than this counts as gone/unmodded.
    private const float StatusTimeout = 8f;

    private sealed class Peer
    {
        public float Fraction;

        public bool Empty;

        public float At;
    }

    // By PlayerRef.RawEncoded.
    private static readonly Dictionary<int, Peer> peers = new();

    private static float nextStatus;

    private static readonly HashSet<string> warned = new();

    private static NetworkRunner Runner()
    {
        OverlandNetworkRunner onr = OverlandNetworkRunner.Instance;
        NetworkRunner r = onr != null ? onr.NetworkRunner : null;
        return r != null && r.IsRunning ? r : null;
    }

    internal static PlayerRef LocalPlayer => OverlandNetworkRunner.LocalPlayerRef;

    // The last fuel status from a modded player, if recent.
    internal static bool TryFuel(PlayerRef p, out float fraction)
    {
        fraction = 0f;
        if (!peers.TryGetValue(p.RawEncoded, out Peer peer) || Time.unscaledTime - peer.At > StatusTimeout)
        {
            return false;
        }
        fraction = peer.Empty ? 0f : peer.Fraction;
        return true;
    }

    // Players we've had a recent status from (modded, in the session).
    internal static List<PlayerRef> KnownPlayers()
    {
        List<PlayerRef> list = new();
        float now = Time.unscaledTime;
        foreach (KeyValuePair<int, Peer> kv in peers)
        {
            if (now - kv.Value.At <= StatusTimeout)
            {
                list.Add(PlayerRef.FromEncoded(kv.Key));
            }
        }
        return list;
    }

    // False until the receive hook is in place: without it, a modded peer's
    // game would throw on our messages just like an unmodded one.
    private static bool ready;

    // Called at 10 Hz from Mod.OnUpdate.
    internal static void Tick(Tank tank)
    {
        if (!ready)
        {
            return;
        }
        float now = Time.unscaledTime;
        if (now < nextStatus)
        {
            return;
        }
        nextStatus = now + StatusInterval;
        try
        {
            NetworkRunner r = Runner();
            string skip = r == null ? "no running network session" : r.IsSinglePlayer ? "single player" : tank == null ? "no car tracked" : tank.Capacity <= 0f ? "car not known yet" : null;
            if (skip != null)
            {
                if (skip != lastSkip)
                {
                    lastSkip = skip;
                    Mod.Log.Msg($"fuel status not sent: {skip}");
                }
                return;
            }
            lastSkip = null;
            if (r.IsServer && !pinged)
            {
                pinged = true;
                Send(r, LocalPlayer, Pack(Ping, LocalPlayer, LocalPlayer, 0f, 0));
                Mod.Log.Msg("fuel network self-test sent (host to itself)");
            }
            float frac = tank.Empty ? 0f : Mathf.Clamp01(tank.Fuel / tank.Capacity);
            byte[] msg = Pack(Status, PlayerRef.None, LocalPlayer, frac, tank.Empty ? (byte)1 : (byte)0);
            if (r.IsServer)
            {
                Broadcast(r, msg, LocalPlayer);
            }
            else
            {
                Send(r, null, msg);
            }
            if (!sentLogged)
            {
                sentLogged = true;
                Mod.Log.Msg($"fuel status: sending every {StatusInterval} s as {(r.IsServer ? "host, to known players" : "client, to the host")} (player {LocalPlayer.PlayerId})");
            }
        }
        catch (Exception ex)
        {
            Warn("status", ex);
        }
    }

    // Sends a jerry can's fuel to another player's car. False if it couldn't
    // be sent (the can is then kept).
    internal static bool SendRefuel(PlayerRef target, float litres)
    {
        try
        {
            NetworkRunner r = Runner();
            if (!ready || r == null || !TryFuel(target, out _))
            {
                return false;
            }
            byte[] msg = Pack(Refuel, target, LocalPlayer, litres, 0);
            Send(r, r.IsServer ? target : null, msg);
            Mod.Log.Msg($"jerry can: {Mod.F(litres)} L sent to player {target.PlayerId}");
            return true;
        }
        catch (Exception ex)
        {
            Warn("refuel send", ex);
            return false;
        }
    }

    private static byte[] Pack(int type, PlayerRef target, PlayerRef origin, float value, byte flag)
    {
        byte[] b = new byte[21];
        BitConverter.TryWriteBytes(b.AsSpan(0), Version);
        BitConverter.TryWriteBytes(b.AsSpan(4), type);
        BitConverter.TryWriteBytes(b.AsSpan(8), target.RawEncoded);
        BitConverter.TryWriteBytes(b.AsSpan(12), origin.RawEncoded);
        BitConverter.TryWriteBytes(b.AsSpan(16), value);
        b[20] = flag;
        return b;
    }

    // To one player (host only), or to the host when target is null.
    //
    // Called through il2cpp_runtime_invoke with our own argument buffers, not
    // the interop wrappers: the interop ReliableKey is 1 byte (its fixed
    // buffer collapses to one field), so the wrapper sent our key with only
    // its first byte right and garbage after it (seen in the first online
    // test). runtime_invoke takes a pointer to each argument's value: the
    // 16-byte key and the span {byte* pointer, int length}.
    private static unsafe void Send(NetworkRunner r, PlayerRef? target, byte[] msg)
    {
        IntPtr info = target.HasValue ? ToPlayerInfo : ToServerInfo;
        if (info == IntPtr.Zero)
        {
            throw new MissingMethodException("NetworkRunner.SendReliableData method info not found");
        }
        int* key = stackalloc int[4];
        key[0] = Key0;
        key[1] = Key1;
        key[2] = 0;
        key[3] = 0;
        IntPtr buf = Marshal.AllocHGlobal(msg.Length);
        try
        {
            Marshal.Copy(msg, 0, buf, msg.Length);
            long* span = stackalloc long[2];
            span[0] = (long)buf;
            span[1] = msg.Length;
            int player = target.HasValue ? target.Value.RawEncoded : 0;
            void** args = stackalloc void*[3];
            if (target.HasValue)
            {
                args[0] = &player;
                args[1] = key;
                args[2] = span;
            }
            else
            {
                args[0] = key;
                args[1] = span;
            }
            IntPtr exc = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(info, r.Pointer, args, ref exc);
            Il2CppInterop.Runtime.Il2CppException.RaiseExceptionIfNecessary(exc);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static IntPtr toPlayerInfo, toServerInfo;

    private static IntPtr ToPlayerInfo => toPlayerInfo != IntPtr.Zero ? toPlayerInfo
        : toPlayerInfo = MethodInfoField(typeof(NetworkRunner), "NativeMethodInfoPtr_SendReliableDataToPlayer_Public_Void_PlayerRef_ReliableKey_ReadOnlySpan_1_Byte_0");

    private static IntPtr ToServerInfo => toServerInfo != IntPtr.Zero ? toServerInfo
        : toServerInfo = MethodInfoField(typeof(NetworkRunner), "NativeMethodInfoPtr_SendReliableDataToServer_Public_Void_ReliableKey_ReadOnlySpan_1_Byte_0");

    private static IntPtr MethodInfoField(Type t, string name)
    {
        FieldInfo f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        return f != null ? (IntPtr)f.GetValue(null) : IntPtr.Zero;
    }

    // Host: a status to every modded player except its origin.
    private static void Broadcast(NetworkRunner r, byte[] msg, PlayerRef origin)
    {
        PlayerRef local = LocalPlayer;
        foreach (PlayerRef p in KnownPlayers())
        {
            if (p != origin && p != local)
            {
                Send(r, p, msg);
            }
        }
    }

    // The receive hook is native (MelonLoader NativeHook), not Harmony.
    // Harmony's Il2Cpp trampoline passes ReliableKey (16 bytes) by value,
    // but the native code passes it (and the ReadOnlySpan) by pointer, so a
    // Harmony patch garbled every message, the game's own ones included
    // (loading stalled with ArgumentOutOfRangeException). Here both are read
    // as pointers. Native signature (Windows x64, IL2CPP):
    // (this, NetworkRunner runner, PlayerRef player (int), ReliableKey* key,
    //  ReadOnlySpan<byte>* data {byte* pointer at 0, int length at 8}, MethodInfo*).
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReceiveFn(IntPtr self, IntPtr runner, int player, IntPtr key, IntPtr data, IntPtr method);

    private static NativeHook<ReceiveFn> hook;

    // Kept alive: the native side calls it.
    private static ReceiveFn detour;

    internal static unsafe void Hook()
    {
        try
        {
            const string name = "NativeMethodInfoPtr_OnReliableDataReceived_Public_Virtual_Final_New_Void_NetworkRunner_PlayerRef_ReliableKey_ReadOnlySpan_1_Byte_0";
            FieldInfo f = typeof(OverlandNetworkRunner).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            IntPtr info = f != null ? (IntPtr)f.GetValue(null) : IntPtr.Zero;
            if (info == IntPtr.Zero)
            {
                Mod.Log.Warning("Fuel network unavailable (multiplayer jerry cans off): OnReliableDataReceived not found");
                return;
            }
            // Il2CppMethodInfo starts with the method pointer.
            IntPtr target = *(IntPtr*)info;
            detour = Receive;
            hook = new NativeHook<ReceiveFn>(target, Marshal.GetFunctionPointerForDelegate(detour));
            hook.Attach();
            ready = true;
            Mod.Log.Msg("fuel network ready");
        }
        catch (Exception ex)
        {
            Mod.Log.Warning("Fuel network unavailable (multiplayer jerry cans off): " + ex);
        }
    }

    private static unsafe void Receive(IntPtr self, IntPtr runner, int player, IntPtr key, IntPtr data, IntPtr method)
    {
        bool ours = false;
        try
        {
            int* k = (int*)key;
            if (receivedLogged < 10 && key != IntPtr.Zero)
            {
                receivedLogged++;
                int n = data != IntPtr.Zero ? *(int*)((byte*)data + 8) : -1;
                Mod.Log.Msg($"network data from player {PlayerRef.FromEncoded(player).PlayerId}: key {k[0]:X8} {k[1]:X8} {k[2]:X8} {k[3]:X8}, {n} bytes");
            }
            if (key != IntPtr.Zero && k[0] == Key0 && k[1] == Key1)
            {
                // Ours: never let the game's handler see it (it throws).
                ours = true;
                byte* p = *(byte**)data;
                int len = *(int*)((byte*)data + 8);
                byte[] b = new byte[Math.Clamp(len, 0, 256)];
                for (int i = 0; i < b.Length; i++)
                {
                    b[i] = p[i];
                }
                Handle(runner != IntPtr.Zero ? new NetworkRunner(runner) : null, PlayerRef.FromEncoded(player), b);
            }
        }
        catch (Exception ex)
        {
            Warn("receive", ex);
        }
        if (!ours)
        {
            hook.Trampoline(self, runner, player, key, data, method);
        }
    }

    private static void Handle(NetworkRunner r, PlayerRef sender, byte[] b)
    {
        if (b.Length < 21 || BitConverter.ToInt32(b, 0) != Version)
        {
            Warn("receive", new FormatException($"message from player {sender.PlayerId}: {b.Length} bytes, unknown version"));
            return;
        }
        int type = BitConverter.ToInt32(b, 4);
        PlayerRef target = PlayerRef.FromEncoded(BitConverter.ToInt32(b, 8));
        PlayerRef origin = PlayerRef.FromEncoded(BitConverter.ToInt32(b, 12));
        float value = BitConverter.ToSingle(b, 16);
        bool server = r != null && r.IsServer;
        PlayerRef local = LocalPlayer;
        // The host only accepts what a client says about itself.
        if (server && origin != sender)
        {
            Mod.Log.Warning($"fuel message from player {sender.PlayerId} claims to be from player {origin.PlayerId}; dropped");
            return;
        }
        if (!float.IsFinite(value))
        {
            return;
        }
        switch (type)
        {
            case Status:
                if (origin != local)
                {
                    bool fresh = !peers.ContainsKey(origin.RawEncoded);
                    peers[origin.RawEncoded] = new Peer { Fraction = Mathf.Clamp01(value), Empty = b[20] != 0, At = Time.unscaledTime };
                    if (fresh)
                    {
                        Mod.Log.Msg($"player {origin.PlayerId} runs the fuel mod");
                    }
                }
                if (server)
                {
                    Broadcast(r, b, origin);
                }
                break;
            case Ping:
                Mod.Log.Msg("fuel network self-test received: sending and receiving work");
                break;
            case Refuel:
                if (target == local)
                {
                    Tank t = Tank.Local;
                    if (t == null)
                    {
                        Mod.Log.Msg($"jerry can from player {origin.PlayerId}: no car tracked here; fuel lost");
                        return;
                    }
                    t.AddFuel(Mathf.Clamp(value, 0f, 1000f), $"jerry can from player {origin.PlayerId}");
                }
                else if (server)
                {
                    if (!TryFuel(target, out _))
                    {
                        Mod.Log.Msg($"jerry can from player {origin.PlayerId} for player {target.PlayerId}: they don't run the fuel mod; dropped");
                        return;
                    }
                    Send(r, target, b);
                }
                break;
        }
    }

    private static void Warn(string where, Exception ex)
    {
        if (warned.Add(where + ": " + ex.GetType().Name + ": " + ex.Message))
        {
            Mod.Log.Warning($"Fuel network ({where}) failed: " + ex);
        }
    }
}
