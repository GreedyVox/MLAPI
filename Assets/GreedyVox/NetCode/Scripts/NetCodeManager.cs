﻿using Opsive.Shared.Events;
using Unity.Netcode;
using UnityEngine;

namespace GreedyVox.NetCode
{
    [DisallowMultipleComponent]
    public class NetCodeManager : AbstractSingletonBehaviour<NetCodeManager>
    {
        [SerializeField] private NetCodeSettingsAbstract m_NetworkSettings = null;
        public NetCodeSettingsAbstract NetworkSettings { get { return m_NetworkSettings; } }
        private AudioSource m_AudioSource;
        private NetworkManager _Connection;
        public NetworkManager Connection
        {
            get
            {
                if (_Connection == null)
                    _Connection = NetworkManager.Singleton;
                return _Connection;
            }
        }
        protected override void Awake()
        {
            Persist = false;
            base.Awake();
            m_AudioSource = GetComponent<AudioSource>();
            if (m_AudioSource == null)
                m_AudioSource = gameObject.AddComponent<AudioSource>();
        }
        private void Start()
        {
            if (m_NetworkSettings == null) return;
            Connection.OnServerStarted += () =>
            {
                if (NetworkManager.Singleton.IsHost)
                    Debug.Log("<color=white>Server Started</color>");
            };
            Connection.OnClientDisconnectCallback += ID =>
            {
                m_NetworkSettings?.PlayDisconnect(m_AudioSource);
                var net = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(ID);
                EventHandler.ExecuteEvent<ulong, NetworkObjectReference>("OnPlayerDisconnected", ID, net);
                Debug.LogFormat("<color=white>Server Client Disconnected ID: [<b><color=red><b>{0}</b></color></b>]</color>", ID);
            };
            Connection.OnClientConnectedCallback += ID =>
            {
                m_NetworkSettings?.PlayConnect(m_AudioSource);
                var net = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(ID);
                if (net == null) return;
                net.gameObject.name = $"[{ID}]{net.gameObject.name}[{net.NetworkObjectId}]";
                EventHandler.ExecuteEvent<ulong, NetworkObjectReference>("OnPlayerConnected", ID, net);
                NetworkLog.LogInfoServer($"<color=white>Server Client Connected {net.gameObject.name} ID: [<b><color=blue><b>{ID}</b></color></b>]</color>");
            };
            if (m_NetworkSettings == null)
            {
                Debug.LogErrorFormat("NullReferenceException: There is no network settings manager\n{0}", typeof(NetCodeSettingsAbstract));
                Quit();
            }
            else
            {
                StartCoroutine(m_NetworkSettings.NetworkSyncUpdate());
                StartCoroutine(m_NetworkSettings.NetworkSyncClient());
                StartCoroutine(m_NetworkSettings.NetworkSyncServer());
                StartCoroutine(m_NetworkSettings.NetworkSyncFixedUpdate());
            }
        }
        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit ();
#endif
        }
    }
}