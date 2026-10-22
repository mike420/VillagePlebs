using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace VillagePlebs.Shared
{
    /// <summary>
    /// Whether the server this client plays on runs the mod too: a server without it deletes a companion (and the gear it carries) as soon
    /// as its area loads. Jötunn turns away a server with Jötunn but not this mod; this covers one without Jötunn at all. Once connected,
    /// the client asks for a hello (again every 10 seconds until one comes); only a server with the mod answers.
    /// </summary>
    internal static class ServerCheck
    {
        private const string HelloRpc = "VillagePlebs_ServerHello";
        private const float AskEvery = 10f;

        private static ZRoutedRpc _registeredOn;
        private static bool _heard;
        private static float _askedAt = -1f;

        /// <summary>Single player, the host, or a server that has answered.</summary>
        public static bool Installed => ZNet.instance == null || ZNet.instance.IsServer() || _heard;

        public static void Tick()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc != _registeredOn)
            {
                _registeredOn = rpc;
                _heard = false;
                _askedAt = -1f;
                Unregister();   // (the game throws if a name is registered twice: one left by a copy of the mod that was reloaded)
                rpc?.Register<bool>(HelloRpc, OnHello);
            }
            if (rpc == null || ZNet.instance == null || ZNet.instance.IsServer() || _heard || ZNet.instance.GetServerPeer() == null) return;
            if (_askedAt >= 0f && Time.time - _askedAt < AskEvery) return;
            _askedAt = Time.time;
            rpc.InvokeRoutedRPC(rpc.GetServerPeerID(), HelloRpc, false);
        }

        /// <summary>The mod unloading (a reload): its answer is no longer heard here, so a reloaded copy registers its own (as Net does).</summary>
        public static void Stop()
        {
            Unregister();
            _registeredOn = null;
            _heard = false;
        }

        private static void Unregister()
        {
            if (ZRoutedRpc.instance == null) return;
            (AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(ZRoutedRpc.instance) as IDictionary)?.Remove(HelloRpc.GetStableHashCode());
        }

        // The server answers a client's question; the client hears the answer
        private static void OnHello(long sender, bool answer)
        {
            if (ZNet.instance == null) return;
            if (ZNet.instance.IsServer())
            {
                if (!answer) ZRoutedRpc.instance.InvokeRoutedRPC(sender, HelloRpc, true);
            }
            else if (answer)
            {
                _heard = true;
            }
        }
    }
}
