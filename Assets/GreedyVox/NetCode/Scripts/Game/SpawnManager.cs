using GreedyVox.NetCode.Utilities;
using Unity.Netcode;
using UnityEngine;

namespace GreedyVox.NetCode.Game
{
    public class SpawnManager : NetworkBehaviour
    {
        [SerializeField] private GameObject m_GameObjectAi;
        [SerializeField] private GameObject m_SpawnPoint;
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer || m_GameObjectAi == null || m_SpawnPoint == null) return;
            var go = GameObject.Instantiate(m_GameObjectAi, m_SpawnPoint.transform.position, Quaternion.identity);
            if (ComponentUtility.TryAddGetComponent<NetworkObject>(go, out var net))
                net.Spawn();
        }
    }
}