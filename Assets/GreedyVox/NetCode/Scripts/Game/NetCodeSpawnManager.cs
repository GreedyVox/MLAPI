using Opsive.Shared.Game;
using Unity.Netcode;
using UnityEngine;

namespace GreedyVox.NetCode.Game
{
    public class NetCodeSpawnManager : INetworkPrefabInstanceHandler
    {
        private bool m_IsPooled;
        private GameObject m_Prefab;
        private Transform m_Transform;
        public NetCodeSpawnManager(GameObject fab, Transform tran = null, bool pool = true)
        {
            m_Prefab = fab;
            m_Transform = tran;
            m_IsPooled = pool;
        }
        public NetworkObject Instantiate(ulong ID, Vector3 pos, Quaternion rot)
        {
            var go = ObjectPoolBase.Instantiate(m_Prefab, pos, rot);
            return go?.GetComponent<NetworkObject>();
        }
        public void Destroy(NetworkObject net)
        {
            var go = net?.gameObject;
            if (m_IsPooled) ObjectPool.Destroy(go);
            else go?.SetActive(false);
        }
    }
}