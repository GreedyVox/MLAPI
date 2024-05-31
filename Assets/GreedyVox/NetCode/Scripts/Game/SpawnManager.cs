using System.Collections;
using GreedyVox.NetCode.Utilities;
using Opsive.Shared.Game;
using Unity.Netcode;
using UnityEngine;

namespace GreedyVox.NetCode.Game
{
    public class SpawnManager : NetworkBehaviour
    {
        [SerializeField] private GameObject m_GameObjectAi;
        [SerializeField] private GameObject m_SpawnPoint;
        [SerializeField] private GameObject m_SpawnTest;
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer || m_GameObjectAi == null || m_SpawnPoint == null) return;
            var go = GameObject.Instantiate(m_GameObjectAi, m_SpawnPoint.transform.position, Quaternion.identity);
            if (ComponentUtility.TryAddGetComponent<NetworkObject>(go, out var net)) net.Spawn();
            if (m_SpawnTest != null)
                StartCoroutine(UpdateSpawner());
        }
        private IEnumerator UpdateSpawner()
        {
            while (isActiveAndEnabled)
            {
                yield return null;
                if (Input.GetMouseButtonUp(2))
                    SpawnObject(m_SpawnTest);
            }
        }
        private void SpawnObject(GameObject go)
        {
            if (Physics.Raycast(Camera.main.ScreenPointToRay(
            new Vector3(Screen.width / 2, Screen.height / 2, 0)), out var hit, 100.0f))
                NetCodeObjectPool.NetworkSpawn(go,
                ObjectPoolBase.Instantiate(go, hit.point, Quaternion.identity), true);
        }
    }
}