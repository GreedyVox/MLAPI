using System.Collections.Generic;
using GreedyVox.NetCode.Utilities;
using Opsive.Shared.Game;
using Opsive.Shared.Utility;
using Opsive.UltimateCharacterController.Inventory;
using Opsive.UltimateCharacterController.Items.Actions;
using Opsive.UltimateCharacterController.Networking.Game;
using Opsive.UltimateCharacterController.Networking.Traits;
using Opsive.UltimateCharacterController.Objects;
using Opsive.UltimateCharacterController.Traits;
using Opsive.UltimateCharacterController.Traits.Damage;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Synchronizes the Health component over the network.
/// </summary>
namespace GreedyVox.NetCode.Traits
{
    [DisallowMultipleComponent]
    public class NetCodeHealthMonitor : NetworkBehaviour, INetworkHealthMonitor
    {
        [Tooltip("Spawn objects on death over the network.")]
        [SerializeField] private GameObject[] m_SpawnObjectsOnDeath;
        private Health m_Health;
        private InventoryBase m_Inventory;
        private GameObject m_GamingObject;
        private NetCodeSettingsAbstract m_Settings;
        private Dictionary<ulong, NetworkObject> m_NetworkObjects;
        /// <summary>
        /// Initializes the default values.
        /// </summary>
        private void Awake()
        {
            m_GamingObject = gameObject;
            m_Settings = NetCodeManager.Instance.NetworkSettings;
            m_Health = m_GamingObject.GetCachedComponent<Health>();
        }
        /// <summary>
        /// Gets called when message handlers are ready to be registered and the networking is setup
        /// </summary>
        public override void OnNetworkSpawn() =>
        m_NetworkObjects = NetworkManager.Singleton.SpawnManager.SpawnedObjects;
        /// <summary>
        /// Spawn objects on death over the network.
        /// <param name="position">The position of the damage.</param>
        /// <param name="direction">The direction that the object took damage from.</param>
        /// </summary>
        private void SpawnObjectsOnDeath(Vector3 position, Vector3 force)
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
        /// <summary>
        /// The object has taken been damaged on the network.
        /// </summary>
        /// <param name="amount">The amount of damage taken.</param>
        /// <param name="position">The position of the damage.</param>
        /// <param name="direction">The direction that the object took damage from.</param>
        /// <param name="forceMagnitude">The magnitude of the force that is applied to the object.</param>
        /// <param name="frames">The number of frames to add the force to.</param>
        /// <param name="radius">The radius of the explosive damage. If 0 then a non-explosive force will be used.</param>
        /// <param name="sourceNetworkObjectID">The PhotonView ID of the object that did the damage.</param>
        /// <param name="sourceItemIdentifierID">The ID of the source's Item Identifier.</param>
        /// <param name="sourceSlotID">The ID of the source's slot.</param>
        /// <param name="sourceItemActionID">The ID of the source's ItemAction.</param>
        /// <param name="hitColliderID">The PhotonView or ObjectIdentifier ID of the Collider that was hit.</param>
        /// <param name="hitItemSlotID">If the hit collider is an item then the slot ID of the item will be specified.</param>
        public void OnDamage(float amount, Vector3 position, Vector3 direction, float forceMagnitude, int frames, float radius, IDamageSource source, Collider hitCollider)
        {
            // A source is not required. If one exists it must have a NetworkObject component attached for identification purposes.
            var sourceSlotID = -1;
            var sourceItemActionID = -1;
            var sourceItemIdentifierID = 0U;
            NetworkObjectReference sourceNetworkObject = default;
            if (source != null)
            {
                // If the originator is an item then more data needs to be sent.
                if (source is CharacterItemAction)
                {
                    var itemAction = source as CharacterItemAction;
                    sourceItemActionID = itemAction.ID;
                    sourceSlotID = itemAction.CharacterItem.SlotID;
                    sourceItemIdentifierID = itemAction.CharacterItem.ItemIdentifier.ID;
                }
                if (source.SourceGameObject != null)
                {
                    var originatorNetworkObject = source.SourceGameObject.GetCachedComponent<NetworkObject>();
                    if (originatorNetworkObject == null)
                    {
                        originatorNetworkObject = source.SourceOwner.GetCachedComponent<NetworkObject>();
                        if (originatorNetworkObject == null)
                        {
                            Debug.LogError($"Error: The attacker {source.SourceOwner.name} must have a PhotonView component.");
                            return;
                        }
                    }
                    sourceNetworkObject = originatorNetworkObject;
                }
            }
            // A hit collider is not required. If one exists it must have an ObjectIdentifier or PhotonView attached for identification purposes.
            (ulong ID, bool) hitColliderPair;
            var hitItemSlotID = -1;
            if (hitCollider != null)
                hitColliderPair = NetCodeUtility.GetID(hitCollider.gameObject, out hitItemSlotID);
            else
                hitColliderPair = (0UL, false);
            if (IsServer)
                DamageClientRpc(amount, position, direction, forceMagnitude, frames, radius, sourceNetworkObject,
                sourceItemIdentifierID, sourceSlotID, sourceItemActionID, hitColliderPair.ID, hitItemSlotID);
            else
                DamageServerRpc(amount, position, direction, forceMagnitude, frames, radius, sourceNetworkObject,
                sourceItemIdentifierID, sourceSlotID, sourceItemActionID, hitColliderPair.ID, hitItemSlotID);
        }
        /// <summary>
        /// The object has taken been damaged on the network.
        /// </summary>
        /// <param name="amount">The amount of damage taken.</param>
        /// <param name="position">The position of the damage.</param>
        /// <param name="direction">The direction that the object took damage from.</param>
        /// <param name="forceMagnitude">The magnitude of the force that is applied to the object.</param>
        /// <param name="frames">The number of frames to add the force to.</param>
        /// <param name="radius">The radius of the explosive damage. If 0 then a non-explosive force will be used.</param>
        /// <param name="sourceNetworkObjectID">The PhotonView ID of the object that did the damage.</param>
        /// <param name="sourceItemIdentifierID">The ID of the source's Item Identifier.</param>
        /// <param name="sourceSlotID">The ID of the source's slot.</param>
        /// <param name="sourceItemActionID">The ID of the source's ItemAction.</param>
        /// <param name="hitColliderID">The PhotonView or ObjectIdentifier ID of the Collider that was hit.</param>
        /// <param name="hitItemSlotID">If the hit collider is an item then the slot ID of the item will be specified.</param>
        private void DamageRpc(float amount, Vector3 position, Vector3 direction, float forceMagnitude, int frames, float radius,
        NetworkObjectReference sourceNetworkObject, uint sourceItemIdentifierID, int sourceSlotID, int sourceItemActionID, ulong hitColliderID, int hitItemSlotID)
        {
            IDamageSource source = null;
            if (sourceNetworkObject.TryGet(out var net))
            {
                var sourceView = net.gameObject;
                source = sourceView?.GetComponent<IDamageSource>();
                // If the originator is null then it may have come from an item.
                if (source == null)
                {
                    var itemType = ItemIdentifierTracker.GetItemIdentifier(sourceItemIdentifierID);
                    m_Inventory = sourceView.GetComponent<InventoryBase>();
                    if (itemType != null && m_Inventory != null)
                    {
                        var item = m_Inventory.GetCharacterItem(itemType, sourceSlotID);
                        source = item?.GetItemAction(sourceItemActionID) as IDamageSource;
                    }
                }
            }
            var hitCollider = NetCodeUtility.RetrieveGameObject(m_GamingObject, hitColliderID, hitItemSlotID);
            var pooledDamageData = GenericObjectPool.Get<DamageData>();
            pooledDamageData.SetDamage(source, amount, position, direction, forceMagnitude, frames, radius,
            hitCollider?.GetCachedComponent<Collider>());
            m_Health.OnDamage(pooledDamageData);
            GenericObjectPool.Return(pooledDamageData);
        }
        [ServerRpc(RequireOwnership = false)]
        private void DamageServerRpc(float amount, Vector3 position, Vector3 direction, float forceMagnitude, int frames, float radius, NetworkObjectReference sourceNetworkObject,
        uint sourceItemIdentifierID, int sourceSlotID, int sourceItemActionID, ulong hitColliderID, int hitItemSlotID)
        {
            if (!IsClient) DamageRpc(amount, position, direction, forceMagnitude, frames, radius, sourceNetworkObject, sourceItemIdentifierID,
                           sourceSlotID, sourceItemActionID, hitColliderID, hitItemSlotID);
            DamageClientRpc(amount, position, direction, forceMagnitude, frames, radius, sourceNetworkObject, sourceItemIdentifierID,
                           sourceSlotID, sourceItemActionID, hitColliderID, hitItemSlotID);
        }

        [ClientRpc]
        private void DamageClientRpc(float amount, Vector3 position, Vector3 direction, float forceMagnitude, int frames, float radius, NetworkObjectReference sourceNetworkObject,
        uint sourceItemIdentifierID, int sourceSlotID, int sourceItemActionID, ulong hitColliderID, int hitItemSlotID) =>
        DamageRpc(amount, position, direction, forceMagnitude, frames, radius, sourceNetworkObject,
        sourceItemIdentifierID, sourceSlotID, sourceItemActionID, hitColliderID, hitItemSlotID);
        /// <summary>
        /// The object is no longer alive.
        /// </summary>
        /// <param name="position">The position of the damage.</param>
        /// <param name="force">The amount of force applied to the object while taking the damage.</param>
        /// <param name="attacker">The GameObject that killed the character.</param>
        public void Die(Vector3 position, Vector3 force, GameObject attacker)
        {
            // An attacker is not required. If one exists it must have a NetworkObject component attached for identification purposes.
            var attackerID = -1L;
            if (attacker != null)
            {
                var attackerObject = attacker.GetCachedComponent<NetworkObject>();
                if (attackerObject == null)
                {
                    Debug.LogError("Error: The attacker " + attacker.name + " must have a NetworkObject component.");
                    return;
                }
                attackerID = (long)attackerObject.NetworkObjectId;
            }
            if (IsServer)
            {
                SpawnObjectsOnDeath(position, force);
                NetworkObjectPool.Destroy(gameObject);
                DieClientRpc(position, force, attackerID);
            }
            else { DieServerRpc(position, force, attackerID); }
        }
        /// <summary>
        /// The object is no longer alive on the network.
        /// </summary>
        /// <param name="position">The position of the damage.</param>
        /// <param name="force">The amount of force applied to the object while taking the damage.</param>
        /// <param name="attackerID">The NetworkObject ID of the GameObject that killed the object.</param>
        private void DieRpc(Vector3 position, Vector3 force, long attackerID)
        {
            GameObject attacker = null;
            if (attackerID != -1
             && m_NetworkObjects.TryGetValue((ulong)attackerID, out var obj))
                attacker = obj.gameObject;
            m_Health.Die(position, force, attacker != null ? attacker.gameObject : null);
        }
        [ServerRpc]
        private void DieServerRpc(Vector3 position, Vector3 force, long attackerID)
        {
            SpawnObjectsOnDeath(position, force);
            NetworkObjectPool.Destroy(gameObject);
            if (!IsClient) DieRpc(position, force, attackerID);
            DieClientRpc(position, force, attackerID);
        }
        [ClientRpc]
        private void DieClientRpc(Vector3 position, Vector3 force, long attackerID)
        {
            if (!IsOwner) DieRpc(position, force, attackerID);
        }
        /// <summary>
        /// Adds amount to health and then to the shield if there is still an amount remaining. Will not go over the maximum health or shield value.
        /// </summary>
        /// <param name="amount">The amount of health or shield to add.</param>
        public void Heal(float amount)
        {
            if (IsServer) HealClientRpc(amount);
            else HealServerRpc(amount);
        }
        /// <summary>
        /// Adds amount to health and then to the shield if there is still an amount remaining on the network.
        /// </summary>
        /// <param name="amount">The amount of health or shield to add.</param>
        private void HealRpc(float amount) => m_Health.Heal(amount);
        [ServerRpc]
        private void HealServerRpc(float amount)
        {
            if (!IsClient) HealRpc(amount);
            HealClientRpc(amount);
        }
        [ClientRpc]
        private void HealClientRpc(float amount)
        {
            if (!IsOwner) HealRpc(amount);
        }
    }
}