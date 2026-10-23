using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace VillagePlebs.Shared
{
    internal static class ServerCheck
    {
        private const string HelloRpc = "VillagePlebs_ServerHello";
        private const float AskEvery = 10f;

        private static ZRoutedRpc _registeredOn;
        private static bool _heard;
        private static float _askedAt = -1f;

        public static bool Installed => ZNet.instance == null || ZNet.instance.IsServer() || _heard;

        public static void Tick()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc != _registeredOn)
            {
                _registeredOn = rpc;
                _heard = false;
                _askedAt = -1f;
                Unregister();
                rpc?.Register<bool>(HelloRpc, OnHello);
            }
            if (rpc == null || ZNet.instance == null || ZNet.instance.IsServer() || _heard || ZNet.instance.GetServerPeer() == null) return;
            if (_askedAt >= 0f && Time.time - _askedAt < AskEvery) return;
            _askedAt = Time.time;
            rpc.InvokeRoutedRPC(rpc.GetServerPeerID(), HelloRpc, false);
        }

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
