using GreedyVox.NetCode.Data;
using GreedyVox.NetCode.Game;
using Opsive.Shared.Game;
using Opsive.UltimateCharacterController.Networking.Game;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GreedyVox.NetCode
{
    public class NetCodeMessenger : NetworkBehaviour
    {
        private static NetCodeMessenger _Instance;
        public static NetCodeMessenger Instance { get { return _Instance; } }
        private ObjectPoolBase.PreloadedPrefab[] m_PreloadedPrefab;
        private CustomMessagingManager m_CustomMessagingManager;
        private const string MsgServerNameDespawn = "MsgServerDespawnObject";
        private const string MsgServerNameSpawn = "MsgServerSpawnObject";
        /// <summary>
        /// The object has awaken.
        /// </summary>
        private void Awake()
        {
            if (_Instance != null && _Instance != this)
                Destroy(this.gameObject);
            else _Instance = this;
            m_PreloadedPrefab = FindObjectOfType<ObjectPool>()?.PreloadedPrefabs;
        }
        public override void OnNetworkDespawn()
        {
            m_CustomMessagingManager?.UnregisterNamedMessageHandler(MsgServerNameDespawn);
            base.OnNetworkDespawn();
        }
        public override void OnNetworkSpawn()
        {
            m_CustomMessagingManager = NetworkManager.CustomMessagingManager;
            if (IsServer)
            {
                // Listening for client side network pooling calls, then forwards message to despawn the object.
                m_CustomMessagingManager?.RegisterNamedMessageHandler(MsgServerNameDespawn, (sender, reader) =>
                {
                    ByteUnpacker.ReadValuePacked(reader, out ulong id);
                    if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var net)
                     && NetworkObjectPool.IsNetworkActive())
                        NetworkObjectPool.Destroy(net.gameObject);
                });
                // Listening for client side network pooling calls, then forwards message to spawn the object.
                m_CustomMessagingManager?.RegisterNamedMessageHandler(MsgServerNameSpawn, (sender, reader) =>
                {
                    ByteUnpacker.ReadValuePacked(reader, out int idx);
                    if (TryGetNetworkPoolObject(idx, out var go))
                    {
                        var spawn = ObjectPoolBase.Instantiate(go);
                        spawn?.GetComponent<IPayload>()?.PayLoad(reader);
                        NetCodeObjectPool.NetworkSpawn(go, spawn, true);
                    }
                });
            }
            base.OnNetworkSpawn();
        }
        /// <summary>
        /// Listening for client side network pooling calls, then forwards message to spawn the object.
        /// </summary>
        public void ClientSpawnObject(GameObject go, IPayload dat)
        {
            // Client sending custom message to the server using the NetCode Messagenger.
            if (TryGetNetworkPoolObjectIndex(go, out var idx) && dat.PayLoad(out var writer))
            {
                writer.WriteValueSafe(idx);
                m_CustomMessagingManager?.SendNamedMessage(
                    MsgServerNameSpawn, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
            }
        }
        /// <summary>
        /// Listening for client side network pooling calls, then forwards message to despawn the object.
        /// </summary>
        public void ClientDespawnObject(ulong id)
        {
            // Client sending custom message to the server using the NetCode Messagenger.
            using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(id), Allocator.Temp);
            BytePacker.WriteValuePacked(writer, id);
            m_CustomMessagingManager?.SendNamedMessage(
                MsgServerNameDespawn, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
        }
        /// <summary>
        /// Find the index of the GameObject inside the pooling list
        /// </summary>
        public bool TryGetNetworkPoolObjectIndex(GameObject go, out int idx)
        {
            for (idx = 0; idx < m_PreloadedPrefab?.Length; idx++)
                if (m_PreloadedPrefab[idx].Prefab == go)
                    return true;
            idx = default;
            return false;
        }
        /// <summary>
        /// Find the GameObject index inside the pooling list
        /// </summary>
        public bool TryGetNetworkPoolObject(int idx, out GameObject go)
        {
            if (idx > -1 && idx < m_PreloadedPrefab?.Length)
            {
                go = m_PreloadedPrefab[idx].Prefab;
                return true;
            }
            go = default;
            return false;
        }
    }
}