using Opsive.UltimateCharacterController.Networking.Objects;
using Opsive.UltimateCharacterController.Objects;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Destroys a Destructible over the network.
/// </summary>
namespace GreedyVox.NetCode
{
    // [RequireComponent(typeof(NetCodeInfo))]
    public class NetCodeDestructibleMonitor : NetworkBehaviour, IDestructibleMonitor
    {
        private ProjectileBase m_Destructible;
        /// <summary>
        /// Initializes the default values.
        /// </summary>
        private void Awake() => m_Destructible = GetComponent<ProjectileBase>();
        /// <summary>
        /// Destroys the object.
        /// </summary>
        /// <param name="hitPosition">The position of the destruction.</param>
        /// <param name="hitNormal">The normal direction of the destruction.</param>
        public void Destruct(Vector3 hitPosition, Vector3 hitNormal) => DestructRpc(hitPosition, hitNormal);
        /// <summary>
        /// Destroys the object over the network.
        /// </summary>
        /// <param name="hitPosition">The position of the destruction.</param>
        /// <param name="hitNormal">The normal direction of the destruction.</param>
        [Rpc(SendTo.NotOwner)]
        private void DestructRpc(Vector3 hitPosition, Vector3 hitNormal) => m_Destructible.Destruct(hitPosition, hitNormal);
    }
}