using Opsive.Shared.Game;
using Opsive.UltimateCharacterController.Networking.Game;
using Opsive.UltimateCharacterController.Objects;
using Unity.Netcode;
using UnityEngine;

namespace GreedyVox.NetCode.Traits
{
    /// <summary>
    /// Synchronizes the Health component over the network.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetCodeHealthMonitor : NetCodeHealthAbstract
    {
        [Tooltip("Spawn objects on death over the network.")]
        [SerializeField] protected GameObject[] m_SpawnObjectsOnDeath;
        public override void Die(Vector3 position, Vector3 force, GameObject attacker)
        {
            base.Die(position, force, attacker);
            if (IsServer)
            {
                SpawnObjectsOnDeath(position, force);
                NetworkObjectPool.Destroy(gameObject);
            }
        }
        /// <summary>
        /// Spawn objects on death over the network.
        /// <param name="position">The position of the damage.</param>
        /// <param name="direction">The direction that the object took damage from.</param>
        /// </summary>
        protected virtual void SpawnObjectsOnDeath(Vector3 position, Vector3 force)
        {
            // Spawn any objects on death, such as an explosion if the object is an explosive barrel.
            if (m_SpawnObjectsOnDeath != null)
            {
                Explosion exp;
                for (int n = 0; n < m_SpawnObjectsOnDeath.Length; n++)
                {
                    var go = m_SpawnObjectsOnDeath[n];
                    var obj = ObjectPool.Instantiate(go, transform.position, transform.rotation);
                    if (obj == null || obj.GetComponent<NetworkObject>() == null)
                    {
                        Debug.LogError($"Spawning Obect {obj} over network requires having the NetCodeObject component.");
                        continue;
                    }
                    NetworkObjectPool.NetworkSpawn(go, obj, true);
                    if ((exp = obj.GetCachedComponent<Explosion>()) != null)
                        exp.Explode(gameObject);
                    var rigs = obj.GetComponentsInChildren<Rigidbody>();
                    for (int i = 0; i < rigs.Length; i++)
                        rigs[i].AddForceAtPosition(force, position);
                }
            }
        }
    }
}