using GreedyVox.NetCode.Utilities;
using Opsive.Shared.Events;
using Opsive.Shared.Game;
using Opsive.UltimateCharacterController.Camera;
using Opsive.UltimateCharacterController.Character;
using Opsive.UltimateCharacterController.Character.Abilities;
using Opsive.UltimateCharacterController.Inventory;
using Opsive.UltimateCharacterController.Items.Actions;
using Opsive.UltimateCharacterController.Items.Actions.Impact;
using Opsive.UltimateCharacterController.Items.Actions.Modules;
using Opsive.UltimateCharacterController.Items.Actions.Modules.Melee;
using Opsive.UltimateCharacterController.Items.Actions.Modules.Shootable;
using Opsive.UltimateCharacterController.Networking.Character;
using Opsive.UltimateCharacterController.Traits;
using Unity.Netcode;
using UnityEngine;
using Opsive.Shared.Utility;
using Opsive.UltimateCharacterController.Items.Actions.Modules.Throwable;
using Opsive.UltimateCharacterController.Items;
using Opsive.UltimateCharacterController.Items.Actions.Modules.Magic;

/// <summary>
/// The NetCode Character component manages the RPCs and state of the character on the network.
/// </summary>
namespace GreedyVox.NetCode.Character
{
    [DisallowMultipleComponent]
    public class NetCodeCharacter : NetworkBehaviour, INetworkCharacter
    {
        private UltimateCharacterLocomotion m_CharacterLocomotion;
        private ModelManager m_ModelManager;
        private NetCodeEvent m_NetworkEvent;
        private InventoryBase m_Inventory;
        private GameObject m_GameObject;
        private bool m_ItemsPickedUp;
        /// <summary>
        /// The character has been destroyed.
        /// </summary>
        public override void OnDestroy()
        {
            base.OnDestroy();
            EventHandler.UnregisterEvent<Ability, bool>(m_GameObject, "OnCharacterAbilityActive", OnAbilityActive);
        }
        /// <summary>
        /// Initialize the default values.
        /// </summary>
        private void Awake()
        {
            m_GameObject = gameObject;
            m_NetworkEvent = GetComponent<NetCodeEvent>();
            m_Inventory = m_GameObject.GetCachedComponent<InventoryBase>();
            m_ModelManager = m_GameObject.GetCachedComponent<ModelManager>();
            m_CharacterLocomotion = m_GameObject.GetCachedComponent<UltimateCharacterLocomotion>();
        }
        /// <summary>
        /// Registers for any interested events.
        /// </summary>
        private void Start()
        {
            if (IsOwner)
                EventHandler.RegisterEvent<Ability, bool>(m_GameObject, "OnCharacterAbilityActive", OnAbilityActive);
            else
                PickupItems();
            // AI agents should be disabled on the client.
            if (!NetworkManager.IsServer && m_GameObject.GetCachedComponent<LocalLookSource>() != null)
                m_CharacterLocomotion.enabled = false;
        }
        /// <summary>
        /// The object has been despawned.
        /// </summary>
        public override void OnNetworkDespawn()
        {
            if (IsOwner)
                EventHandler.UnregisterEvent<ulong, NetworkObjectReference>("OnPlayerConnected", OnPlayerConnected);
            EventHandler.UnregisterEvent<ulong, NetworkObjectReference>("OnPlayerDisconnected", OnPlayerDisconnected);
        }
        /// <summary>
        /// Gets called when message handlers are ready to be registered and the networking is setup.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            if (IsOwner)
                EventHandler.RegisterEvent<ulong, NetworkObjectReference>("OnPlayerConnected", OnPlayerConnected);
            EventHandler.RegisterEvent<ulong, NetworkObjectReference>("OnPlayerDisconnected", OnPlayerDisconnected);
        }
        /// <summary>
        /// Pickup isn't called on unequipped items. Ensure pickup is called before the item is equipped.
        /// </summary>
        private void PickupItems()
        {
            if (m_ItemsPickedUp) return;
            m_ItemsPickedUp = true;
            var items = m_GameObject.GetComponentsInChildren<CharacterItem>(true);
            for (int i = 0; i < items.Length; i++)
                items[i].Pickup();
        }
        /// <summary>
        /// Loads the inventory's default loadout.
        /// </summary>
        public void LoadDefaultLoadout()
        {
            if (IsServer)
                LoadoutDefaultClientRpc();
            else
                LoadoutDefaultServerRpc();
        }
        /// <summary>
        /// Loads the inventory's default loadout on the network.
        /// </summary>
        private void LoadoutDefaultRpc()
        {
            m_Inventory.LoadDefaultLoadout();
            EventHandler.ExecuteEvent(m_GameObject, "OnCharacterSnapAnimator");
        }
        [ServerRpc]
        private void LoadoutDefaultServerRpc()
        {
            if (!IsClient) LoadoutDefaultRpc();
            LoadoutDefaultClientRpc();
        }
        [ClientRpc]
        private void LoadoutDefaultClientRpc()
        {
            if (!IsOwner) LoadoutDefaultRpc();
        }
        /// <summary>
        /// A player has disconnected. Perform any cleanup.
        /// </summary>
        /// <param name="id">The Client networking ID that disconnected.</param>
        /// /// <param name="net">The Player networking Object that connected.</param>
        private void OnPlayerDisconnected(ulong id, NetworkObjectReference net)
        {
            if (OwnerClientId == net.NetworkObjectId && m_CharacterLocomotion.LookSource != null &&
                m_CharacterLocomotion.LookSource.GameObject != null)
            {
                // The local character has disconnected. The character no longer has a look source.
                var cameraController = m_CharacterLocomotion.LookSource.GameObject.GetComponent<CameraController>();
                if (cameraController != null)
                    cameraController.Character = null;
                EventHandler.ExecuteEvent<ILookSource>(m_GameObject, "OnCharacterAttachLookSource", null);
            }
        }
        /// <summary>
        /// A player has joined. Ensure the joining player is in sync with the current game state.
        /// </summary>
        /// <param name="id">The Client networking ID that connected.</param>
        /// <param name="net">The Player networking Object that connected.</param>
        private void OnPlayerConnected(ulong id, NetworkObjectReference net)
        {
            // Notify the joining player of the ItemIdentifiers that the player has within their inventory.
            if (m_Inventory != null)
            {
                var items = m_Inventory.GetAllCharacterItems();
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (IsServer)
                        PickupItemIdentifierServerRpc(item.ItemIdentifier.ID, m_Inventory.GetItemIdentifierAmount(item.ItemIdentifier));
                    else
                        PickupItemIdentifierClientRpc(item.ItemIdentifier.ID, m_Inventory.GetItemIdentifierAmount(item.ItemIdentifier));
                    // Usable Items have a separate ItemIdentifiers amount.
                    if (item.DropPrefab != null)
                    {
                        var itemActions = item.ItemActions;
                        for (int j = 0; j < itemActions.Length; j++)
                        {
                            var usableAction = itemActions[j] as UsableAction;
                            if (usableAction == null) continue;
                            usableAction.InvokeOnModulesWithType<IModuleItemDefinitionConsumer>(module =>
                            {
                                var amount = module.GetItemDefinitionRemainingCount();
                                if (amount > 0)
                                    if (IsServer)
                                        PickupUsableItemActionServerRpc(item.ItemIdentifier.ID, item.SlotID, itemActions[j].ID, (module as ActionModule).ModuleGroup.ID,
                                        (module as ActionModule).ID, m_Inventory.GetItemIdentifierAmount(module.ItemDefinition.CreateItemIdentifier()), amount);
                                    else
                                        PickupUsableItemActionClientRpc(item.ItemIdentifier.ID, item.SlotID, itemActions[j].ID, (module as ActionModule).ModuleGroup.ID,
                                        (module as ActionModule).ID, m_Inventory.GetItemIdentifierAmount(module.ItemDefinition.CreateItemIdentifier()), amount);
                            });
                        }
                    }
                }
                // Ensure the correct item is equipped in each slot.
                for (int i = 0; i < m_Inventory.SlotCount; i++)
                {
                    var item = m_Inventory.GetActiveCharacterItem(i);
                    if (item != null)
                        if (IsServer)
                            EquipUnequipItemServerRpc(item.ItemIdentifier.ID, i, true);
                        else
                            EquipUnequipItemClientRpc(item.ItemIdentifier.ID, i, true);
                }
            }
            // The active character model needs to be synced.
            if (m_ModelManager != null && m_ModelManager.ActiveModelIndex != 0)
                ChangeModels(m_ModelManager.ActiveModelIndex);
            // ULTIMATE_CHARACTER_CONTROLLER_MULTIPLAYER will be defined, but it is required here to allow the add-on to be compiled for the first time.
#if ULTIMATE_CHARACTER_CONTROLLER_MULTIPLAYER
            // The remote character should have the same abilities active.
            for (int i = 0; i < m_CharacterLocomotion.ActiveAbilityCount; i++) 
            {
                var activeAbility = m_CharacterLocomotion.ActiveAbilities[i];
                var dat = activeAbility?.GetNetworkStartData ();
                if (dat != null) 
                    if(IsServer)
                        StartAbilityServerRpc (activeAbility.Index, SerializerObjectArray.Serialize (dat));
                    else
                        StartAbilityClientRpc (activeAbility.Index, SerializerObjectArray.Serialize (dat));
            }
#endif
        }
        /// <summary>
        /// The character's ability has been started or stopped.
        /// </summary>
        /// <param name="ability">The ability which was started or stopped.</param>
        /// <param name="active">True if the ability was started, false if it was stopped.</param>
        private void OnAbilityActive(Ability ability, bool active)
        {
            if (IsServer)
                AbilityActiveClientRpc(ability.Index, active);
            else
                AbilityActiveServerRpc(ability.Index, active);
        }
        /// <summary>
        /// Activates or deactivates the ability on the network at the specified index.
        /// </summary>
        /// <param name="abilityIndex">The index of the ability.</param>
        /// <param name="active">Should the ability be activated?</param>
        private void AbilityActiveRpc(int abilityIndex, bool active)
        {
            if (active)
                m_CharacterLocomotion.TryStartAbility(m_CharacterLocomotion.Abilities[abilityIndex]);
            else
                m_CharacterLocomotion.TryStopAbility(m_CharacterLocomotion.Abilities[abilityIndex], true);
        }
        [ServerRpc]
        private void AbilityActiveServerRpc(int abilityIndex, bool active)
        {
            if (!IsClient) AbilityActiveRpc(abilityIndex, active);
            AbilityActiveClientRpc(abilityIndex, active);
        }
        [ClientRpc]
        private void AbilityActiveClientRpc(int abilityIndex, bool active)
        {
            if (!IsOwner) AbilityActiveRpc(abilityIndex, active);
        }
        /// <summary>
        /// Starts the ability on the remote player.
        /// </summary>
        /// <param name="abilityIndex">The index of the ability.</param>
        /// <param name="startData">Any data associated with the ability start.</param>
        private void StartAbilityRpc(int abilityIndex, SerializableObjectArray startData)
        {
            var ability = m_CharacterLocomotion.Abilities[abilityIndex];
#if ULTIMATE_CHARACTER_CONTROLLER_MULTIPLAYER
            if (startData != null) 
                ability.SetNetworkStartData (DeserializerObjectArray.Deserialize (startData));
#endif
            m_CharacterLocomotion.TryStartAbility(ability, true, true);
        }
        [ServerRpc]
        private void StartAbilityServerRpc(int abilityIndex, SerializableObjectArray startData)
        {
            if (!IsClient) StartAbilityRpc(abilityIndex, startData);
            StartAbilityClientRpc(abilityIndex, startData);
        }
        [ClientRpc]
        private void StartAbilityClientRpc(int abilityIndex, SerializableObjectArray startData)
        {
            if (!IsOwner) StartAbilityRpc(abilityIndex, startData);
        }
        /// <summary>
        /// Picks up the ItemIdentifier on the network.
        /// </summary>
        /// <param name="itemIdentifierID">The ID of the ItemIdentifiers that should be equipped.</param>
        /// <param name="amount">The number of ItemIdnetifiers to pickup.</param>
        private void PickupItemIdentifierRpc(uint itemIdentifierID, int amount)
        {
            var itemIdentifier = ItemIdentifierTracker.GetItemIdentifier(itemIdentifierID);
            if (itemIdentifier != null)
                m_Inventory.PickupItem(itemIdentifier, -1, amount, false, false, false, true);
        }
        [ServerRpc]
        private void PickupItemIdentifierServerRpc(uint itemIdentifierID, int amount)
        {
            if (!IsClient) PickupItemIdentifierRpc(itemIdentifierID, amount);
            PickupItemIdentifierClientRpc(itemIdentifierID, amount);
        }
        [ClientRpc]
        private void PickupItemIdentifierClientRpc(uint itemIdentifierID, int amount)
        {
            if (!IsOwner) PickupItemIdentifierRpc(itemIdentifierID, amount);
        }
        /// <summary>
        /// Picks up the IUsableItem ItemIdentifier on the network.
        /// </summary>
        /// <param name="itemIdentifierID">The ID of the ItemIdentifier that should be equipped.</param>
        /// <param name="slotID">The slot of the item being picked up.</param>
        /// <param name="itemActionID">The ID of the IUsableItem being picked up.</param>
        /// <param name="moduleGroupID">The ID of the module group containing the ItemIdentifier.</param>
        /// <param name="moduleID">The ID of the module containing the ItemIdentifier.</param>
        /// <param name="moduleAmount">The module amount within the inventory.</param>
        /// <param name="moduleItemIdentifierAmount">The ItemIdentifier amount loaded within the module.</param>
        private void PickupUsableItemActionRpc(uint itemIdentifierID, int slotID, int itemActionID, int moduleGroupID, int moduleID, int moduleAmount, int moduleItemIdentifierAmount)
        {
            var itemType = ItemIdentifierTracker.GetItemIdentifier(itemIdentifierID);
            if (itemType == null) return;
            var item = m_Inventory.GetCharacterItem(itemType, slotID);
            if (item == null) return;
            var usableItemAction = item.GetItemAction(itemActionID) as UsableAction;
            if (usableItemAction == null) return;
            usableItemAction.InvokeOnModulesWithTypeConditional<IModuleItemDefinitionConsumer>(module =>
            {
                var actionModule = module as ActionModule;
                if (actionModule.ModuleGroup.ID != moduleGroupID || actionModule.ID != moduleID)
                    return false;
                // The UsableAction has two counts: the first count is from the inventory, and the second count is set on the actual ItemAction.
                m_Inventory.PickupItem(module.ItemDefinition.CreateItemIdentifier(), -1, moduleAmount, false, false, false, false);
                module.SetItemDefinitionRemainingCount(moduleItemIdentifierAmount);
                return true;
            }, true, true);
        }
        [ServerRpc]
        private void PickupUsableItemActionServerRpc(uint itemIdentifierID, int slotID, int itemActionID, int moduleGroupID, int moduleID, int moduleAmount, int moduleItemIdentifierAmount)
        {
            if (!IsClient) PickupUsableItemActionRpc(itemIdentifierID, slotID, itemActionID, moduleGroupID, moduleID, moduleAmount, moduleItemIdentifierAmount);
            PickupUsableItemActionClientRpc(itemIdentifierID, slotID, itemActionID, moduleGroupID, moduleID, moduleAmount, moduleItemIdentifierAmount);
        }
        [ClientRpc]
        private void PickupUsableItemActionClientRpc(uint itemIdentifierID, int slotID, int itemActionID, int moduleGroupID, int moduleID, int moduleAmount, int moduleItemIdentifierAmount)
        {
            if (!IsOwner) PickupUsableItemActionRpc(itemIdentifierID, slotID, itemActionID, moduleGroupID, moduleID, moduleAmount, moduleItemIdentifierAmount);
        }
        /// <summary>
        /// Equips or unequips the item with the specified ItemIdentifier and slot.
        /// </summary>
        /// <param name="itemIdentifierID">The ID of the ItemIdentifier that should be equipped.</param>
        /// <param name="slotID">The slot of the item that should be equipped.</param>
        /// <param name="equip">Should the item be equipped? If false it will be unequipped.</param>
        public void EquipUnequipItem(uint itemIdentifierID, int slotID, bool equip)
        {
            if (IsServer)
                EquipUnequipItemClientRpc(itemIdentifierID, slotID, equip);
            else
                EquipUnequipItemServerRpc(itemIdentifierID, slotID, equip);
        }
        /// <summary>
        /// Equips or unequips the item on the network with the specified ItemIdentifier and slot.
        /// </summary>
        /// <param name="itemIdentifierID">The ID of the ItemIdentifier that should be equipped.</param>
        /// <param name="slotID">The slot of the item that should be equipped.</param>
        /// <param name="equip">Should the item be equipped? If false it will be unequipped.</param>
        private void EquipUnequipItemRpc(uint itemIdentifierID, int slotID, bool equip)
        {
            if (equip)
            {
                // The character has to be alive to equip.
                if (!m_CharacterLocomotion.Alive) return;
                // Ensure pickup is called before the item is equipped.
                PickupItems();
            }
            var itemIdentifier = ItemIdentifierTracker.GetItemIdentifier(itemIdentifierID);
            if (itemIdentifier == null) return;
            var item = m_Inventory.GetCharacterItem(itemIdentifier, slotID);
            if (item == null) return;
            if (equip)
            {
                if (m_Inventory.GetActiveCharacterItem(slotID) != item)
                {
                    EventHandler.ExecuteEvent<CharacterItem, int>(m_GameObject, "OnAbilityWillEquipItem", item, slotID);
                    m_Inventory.EquipItem(itemIdentifier, slotID, true);
                }
            }
            else
            {
                EventHandler.ExecuteEvent<CharacterItem, int>(m_GameObject, "OnAbilityUnequipItemComplete", item, slotID);
                m_Inventory.UnequipItem(itemIdentifier, slotID);
            }
        }
        [ServerRpc]
        private void EquipUnequipItemServerRpc(uint itemIdentifierID, int slotID, bool equip)
        {
            if (!IsClient) EquipUnequipItemRpc(itemIdentifierID, slotID, equip);
            EquipUnequipItemClientRpc(itemIdentifierID, slotID, equip);
        }
        [ClientRpc]
        private void EquipUnequipItemClientRpc(uint itemIdentifierID, int slotID, bool equip)
        {
            if (!IsOwner) EquipUnequipItemRpc(itemIdentifierID, slotID, equip);
        }
        /// <summary>
        /// The ItemIdentifier has been picked up.
        /// </summary>
        /// <param name="itemIdentifierID">The ID of the ItemIdentifier that was picked up.</param>
        /// <param name="amount">The number of ItemIdentifier picked up.</param>
        /// <param name="slotID">The ID of the slot which the item belongs to.</param>
        /// <param name="immediatePickup">Was the item be picked up immediately?</param>
        /// <param name="forceEquip">Should the item be force equipped?</param>
        public void ItemIdentifierPickup(uint itemIdentifierID, int amount, int slotID, bool immediatePickup, bool forceEquip)
        {
            if (IsServer)
                ItemIdentifierPickupClientRpc(itemIdentifierID, amount, slotID, immediatePickup, forceEquip);
            else
                ItemIdentifierPickupServerRpc(itemIdentifierID, amount, slotID, immediatePickup, forceEquip);
        }
        /// <summary>
        /// The ItemIdentifier has been picked up on the network.
        /// </summary>
        /// <param name="itemIdentifierID">The ID of the ItemIdentifier that was picked up.</param>
        /// <param name="amount">The number of ItemIdentifier picked up.</param>
        /// <param name="slotID">The ID of the slot which the item belongs to.</param>
        /// <param name="immediatePickup">Was the item be picked up immediately?</param>
        /// <param name="forceEquip">Should the item be force equipped?</param>
        private void ItemIdentifierPickupRpc(uint itemIdentifierID, int amount, int slotID, bool immediatePickup, bool forceEquip)
        {
            var itemIdentifier = ItemIdentifierTracker.GetItemIdentifier(itemIdentifierID);
            if (itemIdentifier != null)
                m_Inventory.PickupItem(itemIdentifier, amount, slotID, immediatePickup, forceEquip);
        }
        [ServerRpc]
        private void ItemIdentifierPickupServerRpc(uint itemIdentifierID, int amount, int slotID, bool immediatePickup, bool forceEquip)
        {
            if (!IsClient) ItemIdentifierPickupRpc(itemIdentifierID, amount, slotID, immediatePickup, forceEquip);
            ItemIdentifierPickupClientRpc(itemIdentifierID, amount, slotID, immediatePickup, forceEquip);
        }
        [ClientRpc]
        private void ItemIdentifierPickupClientRpc(uint itemIdentifierID, int amount, int slotID, bool immediatePickup, bool forceEquip)
        {
            if (!IsOwner) ItemIdentifierPickupRpc(itemIdentifierID, amount, slotID, immediatePickup, forceEquip);
        }
        /// <summary>
        /// Remove an item amount from the inventory.
        /// </summary>
        /// <param name="itemIdentifierID">The ID of the ItemIdentifier that was removed.</param>
        /// <param name="slotID">The ID of the slot which the item belongs to.</param>
        /// <param name="amount">The amount of ItemIdentifier to adjust.</param>
        /// <param name="drop">Should the item be dropped?</param>
        /// <param name="removeCharacterItem">Should the character item be removed?</param>
        /// <param name="destroyCharacterItem">Should the character item be destroyed?</param>
        public void RemoveItemIdentifierAmount(uint itemIdentifierID, int slotID, int amount, bool drop, bool removeCharacterItem, bool destroyCharacterItem)
        {
            if (IsServer)
                RemoveItemIdentifierAmountClientRpc(itemIdentifierID, slotID, amount, drop, removeCharacterItem, destroyCharacterItem);
            else
                RemoveItemIdentifierAmountServerRpc(itemIdentifierID, slotID, amount, drop, removeCharacterItem, destroyCharacterItem);
        }
        /// <summary>
        /// Remove an item amount from the inventory on the network.
        /// </summary>
        /// <param name="itemIdentifierID">The ID of the ItemIdentifier that was removed.</param>
        /// <param name="slotID">The ID of the slot which the item belongs to.</param>
        /// <param name="amount">The amount of ItemIdentifier to adjust.</param>
        /// <param name="drop">Should the item be dropped?</param>
        /// <param name="removeCharacterItem">Should the character item be removed?</param>
        /// <param name="destroyCharacterItem">Should the character item be destroyed?</param>
        private void RemoveItemIdentifierAmountRpc(uint itemIdentifierID, int slotID, int amount, bool drop, bool removeCharacterItem, bool destroyCharacterItem)
        {
            var itemIdentifier = ItemIdentifierTracker.GetItemIdentifier(itemIdentifierID);
            if (itemIdentifier != null)
                m_Inventory?.RemoveItemIdentifierAmount(itemIdentifier, slotID, amount, drop, removeCharacterItem, destroyCharacterItem);
        }
        [ServerRpc]
        private void RemoveItemIdentifierAmountServerRpc(uint itemIdentifierID, int slotID, int amount, bool drop, bool removeCharacterItem, bool destroyCharacterItem)
        {
            if (!IsClient) RemoveItemIdentifierAmountRpc(itemIdentifierID, slotID, amount, drop, removeCharacterItem, destroyCharacterItem);
            RemoveItemIdentifierAmountClientRpc(itemIdentifierID, slotID, amount, drop, removeCharacterItem, destroyCharacterItem);
        }
        [ClientRpc]
        private void RemoveItemIdentifierAmountClientRpc(uint itemIdentifierID, int slotID, int amount, bool drop, bool removeCharacterItem, bool destroyCharacterItem)
        {
            if (!IsOwner) RemoveItemIdentifierAmountRpc(itemIdentifierID, slotID, amount, drop, removeCharacterItem, destroyCharacterItem);
        }
        /// <summary>
        /// Removes all of the items from the inventory.
        /// </summary>
        public void RemoveAllItems()
        {
            if (IsServer)
                RemoveAllItemsClientRpc();
            else
                RemoveAllItemsServerRpc();
        }
        /// <summary>
        /// Removes all of the items from the inventory on the network.
        /// </summary>
        private void RemoveAllItemsRpc() => m_Inventory.RemoveAllItems(true);
        [ServerRpc]
        private void RemoveAllItemsServerRpc()
        {
            if (!IsClient) { RemoveAllItemsRpc(); }
            RemoveAllItemsClientRpc();
        }
        [ClientRpc]
        private void RemoveAllItemsClientRpc()
        {
            if (!IsOwner) RemoveAllItemsRpc();
        }
        /// <summary>
        /// Returns the ItemAction with the specified slot and ID.
        /// </summary>
        /// <param name="slotID">The slot that the ItemAction belongs to.</param>
        /// <param name="actionID">The ID of the ItemAction being retrieved.</param>
        /// <returns>The ItemAction with the specified slot and ID</returns>
        private CharacterItemAction GetItemAction(int slotID, int actionID)
        {
            var item = m_Inventory.GetActiveCharacterItem(slotID);
            return item?.GetItemAction(actionID);
        }
        /// <summary>
        /// Returns the module with the specified IDs.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="slotID">The SlotID of the module that is being retrieved.</param>
        /// <param name="actionID">The ID of the ItemAction being retrieved.</param>
        /// <param name="actionID">The ID of the ModuleGroup being retrieved.</param>
        /// <param name="moduleID">The ID of the module being retrieved.</param>
        /// <returns>The module with the specified IDs (can be null).</returns>
        private T GetModule<T>(int slotID, int actionID, int moduleGroupID, int moduleID) where T : ActionModule
        {
            var itemAction = GetItemAction(slotID, actionID);
            if (itemAction == null)
                return null;
            if (!itemAction.ModuleGroupsByID.TryGetValue(moduleGroupID, out var moduleGroup))
                return null;
            if (moduleGroup.GetBaseModuleByID(moduleID) is not T module)
                return null;
            return module;
        }
        /// <summary>
        /// Returns the module group with the specified IDs.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being retrieved.</param>
        /// <param name="actionID">The ID of the ItemAction being retrieved.</param>
        /// <param name="actionID">The ID of the ModuleGroup being retrieved.</param>
        /// <returns>The module group with the specified IDs (can be null).</returns>
        private ActionModuleGroupBase GetModuleGroup(int slotID, int actionID, int moduleGroupID)
        {
            var itemAction = GetItemAction(slotID, actionID);
            if (itemAction == null)
                return null;
            if (!itemAction.ModuleGroupsByID.TryGetValue(moduleGroupID, out var moduleGroup))
                return null;
            return moduleGroup;
        }
        /// <summary>
        /// Initializes the ImpactCollisionData object.
        /// </summary>
        /// <param name="collisionData">The ImpactCollisionData resulting object.</param>
        /// <param name="sourceID">The ID of the impact.</param>
        /// <param name="sourceCharacterLocomotionViewID">The ID of the CharacterLocomotion component that caused the collision.</param>
        /// <param name="sourceGameObjectID">The ID of the GameObject that caused the collision.</param>
        /// <param name="sourceGameObjectSlotID">The slot ID if an item caused the collision.</param>
        /// <param name="impactGameObjectID">The ID of the GameObject that was impacted.</param>
        /// <param name="impactGameObjectSlotID">The slot ID of the item that was impacted.</param>
        /// <param name="impactColliderID">The ID of the Collider that was impacted.</param>
        /// <param name="impactPosition">The position of impact.</param>
        /// <param name="impactDirection">The direction of impact.</param>
        /// <param name="impactStrength">The strength of the impact.</param>
        /// <returns>True if the data structure was successfully initialized.</returns>
        private bool InitializeImpactCollisionData(ref ImpactCollisionData collisionData, ulong sourceID, int sourceCharacterLocomotionViewID, ulong sourceGameObjectID, int sourceGameObjectSlotID,
                                                   ulong impactGameObjectID, int impactGameObjectSlotID, ulong impactColliderID, Vector3 impactPosition, Vector3 impactDirection, float impactStrength)
        {
            UltimateCharacterLocomotion sourceCharacterLocomotion = null;
            if (sourceCharacterLocomotionViewID != -1)
                if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue((ulong)sourceCharacterLocomotionViewID, out NetworkObject net))
                    sourceCharacterLocomotion = net.gameObject.GetCachedComponent<UltimateCharacterLocomotion>();
            var sourceGameObject = NetCodeUtility.RetrieveGameObject(sourceCharacterLocomotion?.gameObject, sourceGameObjectID, sourceGameObjectSlotID);
            if (sourceGameObject == null) return false;
            var impactGameObject = NetCodeUtility.RetrieveGameObject(null, impactGameObjectID, impactGameObjectSlotID);
            if (impactGameObject == null) return false;
            var impactColliderGameObject = NetCodeUtility.RetrieveGameObject(null, impactColliderID, -1);
            if (impactColliderGameObject == null)
            {
                var impactCollider = impactGameObject.GetCachedComponent<Collider>();
                if (impactCollider == null) return false;
                impactColliderGameObject = impactCollider.gameObject;
            }
            collisionData.ImpactCollider = impactColliderGameObject.GetCachedComponent<Collider>();
            if (collisionData.ImpactCollider == null) return false;
            // A RaycastHit cannot be sent over the network. Try to recreate it locally based on the position and normal values.
            impactDirection.Normalize();
            var ray = new Ray(impactPosition - impactDirection, impactDirection);
            if (!collisionData.ImpactCollider.Raycast(ray, out var hit, 3f))
            {
                // The object has moved. Do a larger cast to try to find the object.
                if (!Physics.SphereCast(ray, 0.1f, out hit, 2f, 1 << impactGameObject.layer, QueryTriggerInteraction.Ignore))
                    // The object can't be found. Return.
                    return false;
            }
            collisionData.SetRaycast(hit);
            collisionData.SourceID = (uint)sourceID;
            collisionData.SourceCharacterLocomotion = sourceCharacterLocomotion;
            collisionData.SourceGameObject = sourceGameObject;
            collisionData.ImpactGameObject = impactGameObject;
            collisionData.ImpactPosition = impactPosition;
            collisionData.ImpactDirection = impactDirection;
            collisionData.ImpactStrength = impactStrength;
            return true;
        }
        /// <summary>
        /// Invokes the Shootable Action Fire Effect modules.
        /// </summary>
        /// <param name="itemAction">The Item Action that is invoking the modules.</param>
        /// <param name="moduleGroup">The group that the modules belong to.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="data">The data being sent to the module.</param>
        public void InvokeShootableFireEffectModules(CharacterItemAction itemAction, ActionModuleGroupBase moduleGroup, int invokedBitmask, ShootableUseDataStream data)
        {
            if (IsServer)
                InvokeShootableFireEffectModulesClientRpc(itemAction.CharacterItem.SlotID, itemAction.ID,
                moduleGroup.ID, invokedBitmask, data.FireData.FirePoint, data.FireData.FireDirection);
            else
                InvokeShootableFireEffectModulesServerRpc(itemAction.CharacterItem.SlotID, itemAction.ID,
                moduleGroup.ID, invokedBitmask, data.FireData.FirePoint, data.FireData.FireDirection);
        }
        /// <summary>
        /// Invokes the Shootable Action Fire Effect module on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being invoked.</param>
        /// <param name="actionID">The ID of the ItemAction being invoked.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being invoked.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="firePoint">The fire point that is sent to the module.</param>
        /// <param name="fireDirection">The fire direction that is sent to the module.</param>
        private void InvokeShootableFireEffectModulesRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, Vector3 firePoint, Vector3 fireDirection)
        {
            var moduleGroup = GetModuleGroup(slotID, actionID, moduleGroupID) as ActionModuleGroup<ShootableFireEffectModule>;
            if (moduleGroup == null || moduleGroup.ModuleCount == 0) return;
            var data = GenericObjectPool.Get<ShootableUseDataStream>();
            data.FireData ??= new ShootableFireData();
            // The action will be the same across all modules.
            data.ShootableAction = moduleGroup.Modules[0].ShootableAction;
            data.FireData.FirePoint = firePoint;
            data.FireData.FireDirection = fireDirection;
            for (int i = 0; i < moduleGroup.ModuleCount; i++)
            {
                // Not all modules are invoked.
                if (((1 << moduleGroup.Modules[i].ID) & invokedBitmask) == 0)
                    continue;
                moduleGroup.Modules[i].InvokeEffects(data);
            }
            GenericObjectPool.Return(data);
        }
        [ServerRpc]
        private void InvokeShootableFireEffectModulesServerRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, Vector3 firePoint, Vector3 fireDirection)
        {
            if (!IsClient) InvokeShootableFireEffectModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, firePoint, fireDirection);
            InvokeShootableFireEffectModulesClientRpc(slotID, actionID, moduleGroupID, invokedBitmask, firePoint, fireDirection);
        }
        [ClientRpc]
        private void InvokeShootableFireEffectModulesClientRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, Vector3 firePoint, Vector3 fireDirection)
        {
            if (!IsOwner) InvokeShootableFireEffectModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, firePoint, fireDirection);
        }
        /// <summary>
        /// Invokes the Shootable Action Dry Fire Effect modules.
        /// </summary>
        /// <param name="itemAction">The Item Action that is invoking the modules.</param>
        /// <param name="moduleGroup">The group that the modules belong to.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="data">The data being sent to the module.</param>
        public void InvokeShootableDryFireEffectModules(CharacterItemAction itemAction, ActionModuleGroupBase moduleGroup, int invokedBitmask, ShootableUseDataStream data)
        {
            if (IsServer)
                InvokeShootableDryFireEffectModulesClientRpc(itemAction.CharacterItem.SlotID, itemAction.ID,
                moduleGroup.ID, invokedBitmask, data.FireData.FirePoint, data.FireData.FireDirection);
            else
                InvokeShootableDryFireEffectModulesServerRpc(itemAction.CharacterItem.SlotID, itemAction.ID,
                moduleGroup.ID, invokedBitmask, data.FireData.FirePoint, data.FireData.FireDirection);
        }
        /// <summary>
        /// Invokes the Shootable Action Dry Fire Effect module on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being invoked.</param>
        /// <param name="actionID">The ID of the ItemAction being invoked.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being invoked.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="firePoint">The fire point that is sent to the module.</param>
        /// <param name="fireDirection">The fire direction that is sent to the module.</param>
        private void InvokeShootableDryFireEffectModulesRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, Vector3 firePoint, Vector3 fireDirection)
        {
            var moduleGroup = GetModuleGroup(slotID, actionID, moduleGroupID) as ActionModuleGroup<ShootableFireEffectModule>;
            if (moduleGroup == null || moduleGroup.ModuleCount == 0) return;
            var data = GenericObjectPool.Get<ShootableUseDataStream>();
            data.FireData ??= new ShootableFireData();
            // The action will be the same across all modules.
            data.ShootableAction = moduleGroup.Modules[0].ShootableAction;
            data.FireData.FirePoint = firePoint;
            data.FireData.FireDirection = fireDirection;
            for (int i = 0; i < moduleGroup.ModuleCount; i++)
            {
                // Not all modules are invoked.
                if (((1 << moduleGroup.Modules[i].ID) & invokedBitmask) == 0)
                    continue;
                moduleGroup.Modules[i].InvokeEffects(data);
            }
            GenericObjectPool.Return(data);
        }
        [ServerRpc]
        private void InvokeShootableDryFireEffectModulesServerRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, Vector3 firePoint, Vector3 fireDirection)
        {
            if (!IsClient) InvokeShootableDryFireEffectModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, firePoint, fireDirection);
            InvokeShootableDryFireEffectModulesClientRpc(slotID, actionID, moduleGroupID, invokedBitmask, firePoint, fireDirection);
        }
        [ClientRpc]
        private void InvokeShootableDryFireEffectModulesClientRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, Vector3 firePoint, Vector3 fireDirection)
        {
            if (!IsOwner) InvokeShootableDryFireEffectModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, firePoint, fireDirection);
        }
        /// <summary>
        /// Invokes the Shootable Action Impact modules.
        /// </summary>
        /// <param name="itemAction">The Item Action that is invoking the modules.</param>
        /// <param name="moduleGroup">The group that the modules belong to.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="context">The context being sent to the module.</param>
        public void InvokeShootableImpactModules(CharacterItemAction itemAction, ActionModuleGroupBase moduleGroup, int invokedBitmask, ShootableImpactCallbackContext context)
        {
            var sourceCharacterLocomotionViewID = -1;
            if (context.ImpactCollisionData.SourceCharacterLocomotion != null)
            {
                var sourceCharacterLocomotionView = context.ImpactCollisionData.SourceCharacterLocomotion.gameObject.GetCachedComponent<NetworkObject>();
                if (sourceCharacterLocomotionView == null)
                {
                    Debug.LogError($"Error: The character {context.ImpactCollisionData.SourceCharacterLocomotion.gameObject} must have a NetworkObject component added.");
                    return;
                }
                sourceCharacterLocomotionViewID = (int)sourceCharacterLocomotionView.NetworkObjectId;
            }
            var sourceGameObject = NetCodeUtility.GetID(context.ImpactCollisionData.SourceGameObject, out var sourceGameObjectSlotID);
            if (!sourceGameObject.HasID) return;
            var impactGameObject = NetCodeUtility.GetID(context.ImpactCollisionData.ImpactGameObject, out var impactGameObjectSlotID);
            if (!impactGameObject.HasID) return;
            var impactCollider = NetCodeUtility.GetID(context.ImpactCollisionData.ImpactCollider.gameObject, out var colliderSlotID);
            if (!impactCollider.HasID) return;
            if (IsServer)
                InvokeShootableImpactModulesClientRpc(itemAction.CharacterItem.SlotID, itemAction.ID, moduleGroup.ID, invokedBitmask,
                context.ImpactCollisionData.SourceID, sourceCharacterLocomotionViewID, sourceGameObject.ID, sourceGameObjectSlotID, impactGameObject.ID, impactGameObjectSlotID,
                impactCollider.ID, context.ImpactCollisionData.ImpactPosition, context.ImpactCollisionData.ImpactDirection, context.ImpactCollisionData.ImpactStrength);
            else
                InvokeShootableImpactModulesServerRpc(itemAction.CharacterItem.SlotID, itemAction.ID, moduleGroup.ID, invokedBitmask,
                context.ImpactCollisionData.SourceID, sourceCharacterLocomotionViewID, sourceGameObject.ID, sourceGameObjectSlotID, impactGameObject.ID, impactGameObjectSlotID,
                impactCollider.ID, context.ImpactCollisionData.ImpactPosition, context.ImpactCollisionData.ImpactDirection, context.ImpactCollisionData.ImpactStrength);
        }
        /// <summary>
        /// Invokes the Shootable Action Impact modules on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being invoked.</param>
        /// <param name="actionID">The ID of the ItemAction being invoked.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being invoked.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="sourceID">The ID of the impact.</param>
        /// <param name="sourceCharacterLocomotionViewID">The ID of the CharacterLocomotion component that caused the collision.</param>
        /// <param name="sourceGameObjectID">The ID of the GameObject that caused the collision.</param>
        /// <param name="sourceGameObjectSlotID">The slot ID if an item caused the collision.</param>
        /// <param name="impactGameObjectID">The ID of the GameObject that was impacted.</param>
        /// <param name="impactGameObjectSlotID">The slot ID of the item that was impacted.</param>
        /// <param name="impactColliderID">The ID of the Collider that was impacted.</param>
        /// <param name="impactPosition">The position of impact.</param>
        /// <param name="impactDirection">The direction of impact.</param>
        /// <param name="impactStrength">The strength of the impact.</param>
        private void InvokeShootableImpactModulesRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, uint sourceID, int sourceCharacterLocomotionViewID,
                                                     ulong sourceGameObjectID, int sourceGameObjectSlotID, ulong impactGameObjectID, int impactGameObjectSlotID,
                                                     ulong impactColliderID, Vector3 impactPosition, Vector3 impactDirection, float impactStrength)
        {
            var moduleGroup = GetModuleGroup(slotID, actionID, moduleGroupID) as ActionModuleGroup<ShootableImpactModule>;
            if (moduleGroup == null || moduleGroup.ModuleCount == 0) return;
            var context = GenericObjectPool.Get<ShootableImpactCallbackContext>();
            if (context.ImpactCollisionData == null)
            {
                context.ImpactCollisionData = new ImpactCollisionData();
                context.ImpactDamageData = new ImpactDamageData();
            }
            var collisionData = context.ImpactCollisionData;
            if (!InitializeImpactCollisionData(ref collisionData, sourceID, sourceCharacterLocomotionViewID, sourceGameObjectID, sourceGameObjectSlotID, impactGameObjectID, impactGameObjectSlotID,
                                               impactColliderID, impactPosition, impactDirection, impactStrength))
            {
                GenericObjectPool.Return(context);
                return;
            }
            collisionData.SourceComponent = GetItemAction(slotID, actionID);
            context.ImpactCollisionData = collisionData;
            context.ShootableAction = moduleGroup.Modules[0].ShootableAction; // The action will be the same across all modules.
            for (int i = 0; i < moduleGroup.ModuleCount; i++)
            {
                // Not all modules are invoked.
                if (((1 << moduleGroup.Modules[i].ID) & invokedBitmask) == 0)
                    continue;
                moduleGroup.Modules[i].OnImpact(context);
            }
            GenericObjectPool.Return(context);
        }
        [ServerRpc]
        private void InvokeShootableImpactModulesServerRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, uint sourceID, int sourceCharacterLocomotionViewID,
                                                           ulong sourceGameObjectID, int sourceGameObjectSlotID, ulong impactGameObjectID, int impactGameObjectSlotID,
                                                           ulong impactColliderID, Vector3 impactPosition, Vector3 impactDirection, float impactStrength)
        {
            if (!IsClient) InvokeShootableImpactModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, sourceID, sourceCharacterLocomotionViewID,
                                                           sourceGameObjectID, sourceGameObjectSlotID, impactGameObjectID, impactGameObjectSlotID,
                                                           impactColliderID, impactPosition, impactDirection, impactStrength);
            InvokeShootableImpactModulesClientRpc(slotID, actionID, moduleGroupID, invokedBitmask, sourceID, sourceCharacterLocomotionViewID,
                                                           sourceGameObjectID, sourceGameObjectSlotID, impactGameObjectID, impactGameObjectSlotID,
                                                           impactColliderID, impactPosition, impactDirection, impactStrength);
        }
        [ClientRpc]
        private void InvokeShootableImpactModulesClientRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, uint sourceID, int sourceCharacterLocomotionViewID,
                                                           ulong sourceGameObjectID, int sourceGameObjectSlotID, ulong impactGameObjectID, int impactGameObjectSlotID,
                                                           ulong impactColliderID, Vector3 impactPosition, Vector3 impactDirection, float impactStrength)
        {
            if (!IsOwner) InvokeShootableImpactModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, sourceID, sourceCharacterLocomotionViewID,
                                                           sourceGameObjectID, sourceGameObjectSlotID, impactGameObjectID, impactGameObjectSlotID,
                                                           impactColliderID, impactPosition, impactDirection, impactStrength);
        }
        /// <summary>
        /// Starts to reload the module.
        /// </summary>
        /// <param name="module">The module that is being reloaded.</param>
        public void StartItemReload(ShootableReloaderModule module)
        {
            if (IsServer)
                StartItemReloadClientRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID);
            else
                StartItemReloadServerRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID);
        }

        /// <summary>
        /// Starts to reload the item on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being reloaded.</param>
        /// <param name="actionID">The ID of the ItemAction being reloaded.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being reloaded.</param>
        /// <param name="moduleID">The ID of the module being reloaded.</param>
        private void StartItemReloadRpc(int slotID, int actionID, int moduleGroupID, int moduleID)
        {
            var module = GetModule<ShootableReloaderModule>(slotID, actionID, moduleGroupID, moduleID);
            module?.StartItemReload();
        }
        [ServerRpc]
        private void StartItemReloadServerRpc(int slotID, int actionID, int moduleGroupID, int moduleID)
        {
            if (!IsClient) StartItemReloadRpc(slotID, actionID, moduleGroupID, moduleID);
            StartItemReloadClientRpc(slotID, actionID, moduleGroupID, moduleID);
        }
        [ClientRpc]
        private void StartItemReloadClientRpc(int slotID, int actionID, int moduleGroupID, int moduleID)
        {
            if (!IsOwner) StartItemReloadRpc(slotID, actionID, moduleGroupID, moduleID);
        }
        /// <summary>
        /// Reloads the item.
        /// </summary>
        /// <param name="module">The module that is being reloaded.</param>
        /// <param name="fullClip">Should the full clip be force reloaded?</param
        public void ReloadItem(ShootableReloaderModule module, bool fullClip)
        {
            if (IsServer)
                ReloadItemClientRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID, fullClip);
            else
                ReloadItemServerRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID, fullClip);
        }
        /// <summary>
        /// Reloads the item on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being reloaded.</param>
        /// <param name="actionID">The ID of the ItemAction being reloaded.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being reloaded.</param>
        /// <param name="moduleID">The ID of the module being reloaded.</param>
        /// <param name="fullClip">Should the full clip be force reloaded?</param>
        private void ReloadItemRpc(int slotID, int actionID, int moduleGroupID, int moduleID, bool fullClip)
        {
            var module = GetModule<ShootableReloaderModule>(slotID, actionID, moduleGroupID, moduleID);
            module?.ReloadItem(fullClip);
        }
        [ServerRpc]
        private void ReloadItemServerRpc(int slotID, int actionID, int moduleGroupID, int moduleID, bool fullClip)
        {
            if (!IsClient) { ReloadItemRpc(slotID, actionID, moduleGroupID, moduleID, fullClip); }
            ReloadItemClientRpc(slotID, actionID, moduleGroupID, moduleID, fullClip);
        }
        [ClientRpc]
        private void ReloadItemClientRpc(int slotID, int actionID, int moduleGroupID, int moduleID, bool fullClip)
        {
            if (!IsOwner) { ReloadItemRpc(slotID, actionID, moduleGroupID, moduleID, fullClip); }
        }
        /// <summary>
        /// The item has finished reloading.
        /// </summary>
        /// <param name="module">The module that is being realoaded.</param>
        /// <param name="success">Was the item reloaded successfully?</param>
        /// <param name="immediateReload">Should the item be reloaded immediately?</param>
        public void ItemReloadComplete(ShootableReloaderModule module, bool success, bool immediateReload)
        {
            if (IsServer)
                ItemReloadCompleteClientRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID, success, immediateReload);
            else
                ItemReloadCompleteServerRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID, success, immediateReload);
        }
        /// <summary>
        /// The item has finished reloading on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being invoked.</param>
        /// <param name="actionID">The ID of the ItemAction being invoked.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being invoked.</param>
        /// <param name="moduleID">The ID of the module being invoked.</param>
        /// <param name="success">Was the item reloaded successfully?</param>
        /// <param name="immediateReload">Should the item be reloaded immediately?</param>
        private void ItemReloadCompleteRpc(int slotID, int actionID, int moduleGroupID, int moduleID, bool success, bool immediateReload)
        {
            var module = GetModule<ShootableReloaderModule>(slotID, actionID, moduleGroupID, moduleID);
            module?.ItemReloadComplete(success, immediateReload);
        }
        [ServerRpc]
        private void ItemReloadCompleteServerRpc(int slotID, int actionID, int moduleGroupID, int moduleID, bool success, bool immediateReload)
        {
            if (!IsClient) { ItemReloadCompleteClientRpc(slotID, actionID, moduleGroupID, moduleID, success, immediateReload); }
            ItemReloadCompleteClientRpc(slotID, actionID, moduleGroupID, moduleID, success, immediateReload);
        }
        [ClientRpc]
        private void ItemReloadCompleteClientRpc(int slotID, int actionID, int moduleGroupID, int moduleID, bool success, bool immediateReload)
        {
            if (!IsOwner) { ItemReloadCompleteRpc(slotID, actionID, moduleGroupID, moduleID, success, immediateReload); }
        }
        /// <summary>
        /// Invokes the Melee Action Attack module.
        /// </summary>
        /// <param name="module">The module that is being invoked.</param>
        /// <param name="data">The data being sent to the module.</param>
        public void InvokeMeleeAttackModule(MeleeAttackModule module, MeleeUseDataStream data)
        {
            if (IsServer)
                InvokeMeleeAttackModuleClientRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID);
            else
                InvokeMeleeAttackModuleServerRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID);
        }
        /// <summary>
        /// Invokes the Melee Action Attack modules over the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being retrieved.</param>
        /// <param name="actionID">The ID of the ItemAction being retrieved.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being retrieved.</param>
        /// <param name="moduleID">The ID of the module being retrieved.</param>
        private void InvokeMeleeAttackModuleRpc(int slotID, int actionID, int moduleGroupID, int moduleID)
        {
            var module = GetModule<MeleeAttackModule>(slotID, actionID, moduleGroupID, moduleID);
            if (module == null) return;
            var data = GenericObjectPool.Get<MeleeUseDataStream>();
            data.MeleeAction = module.MeleeAction;
            module.AttackStart(data);
            GenericObjectPool.Return(data);
        }
        [ServerRpc]
        private void InvokeMeleeAttackModuleServerRpc(int slotID, int actionID, int moduleGroupID, int moduleID)
        {
            if (!IsClient) InvokeMeleeAttackModuleRpc(slotID, actionID, moduleGroupID, moduleID);
            InvokeMeleeAttackModuleClientRpc(slotID, actionID, moduleGroupID, moduleID);
        }
        [ClientRpc]
        private void InvokeMeleeAttackModuleClientRpc(int slotID, int actionID, int moduleGroupID, int moduleID)
        {
            if (!IsOwner) InvokeMeleeAttackModuleRpc(slotID, actionID, moduleGroupID, moduleID);
        }
        /// <summary>
        /// Invokes the Melee Action Attack Effect modules.
        /// </summary>
        /// <param name="module">The module that is being invoked.</param>
        /// <param name="data">The data being sent to the module.</param>
        public void InvokeMeleeAttackEffectModule(ActionModule module, MeleeUseDataStream data)
        {
            if (IsServer)
                InvokeMeleeAttackEffectModulesClientRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID);
            else
                InvokeMeleeAttackEffectModulesServerRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID);
        }
        /// <summary>
        /// Invokes the Melee Action Attack Effects modules over the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being retrieved.</param>
        /// <param name="actionID">The ID of the ItemAction being retrieved.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being retrieved.</param>
        /// <param name="moduleID">The bitmask of the invoked modules.</param>
        private void InvokeMeleeAttackEffectModulesRpc(int slotID, int actionID, int moduleGroupID, int moduleID)
        {
            var module = GetModule<MeleeAttackEffectModule>(slotID, actionID, moduleGroupID, moduleID);
            module?.StartEffects();
        }
        [ServerRpc]
        private void InvokeMeleeAttackEffectModulesServerRpc(int slotID, int actionID, int moduleGroupID, int moduleID)
        {
            if (!IsClient) InvokeMeleeAttackEffectModulesRpc(slotID, actionID, moduleGroupID, moduleID);
            InvokeMeleeAttackEffectModulesClientRpc(slotID, actionID, moduleGroupID, moduleID);
        }
        [ClientRpc]
        private void InvokeMeleeAttackEffectModulesClientRpc(int slotID, int actionID, int moduleGroupID, int moduleID)
        {
            if (!IsOwner) InvokeMeleeAttackEffectModulesRpc(slotID, actionID, moduleGroupID, moduleID);
        }
        /// <summary>
        /// Invokes the Melee Action Impact modules.
        /// </summary>
        /// <param name="itemAction">The Item Action that is invoking the modules.</param>
        /// <param name="moduleGroup">The group that the modules belong to.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="context">The context being sent to the module.</param>
        public void InvokeMeleeImpactModules(CharacterItemAction itemAction, ActionModuleGroupBase moduleGroup, int invokedBitmask, MeleeImpactCallbackContext context)
        {
            var sourceCharacterLocomotionViewID = -1;
            if (context.ImpactCollisionData.SourceCharacterLocomotion != null)
            {
                var sourceCharacterLocomotionView = context.ImpactCollisionData.SourceCharacterLocomotion.gameObject.GetCachedComponent<NetworkObject>();
                if (sourceCharacterLocomotionView == null)
                {
                    Debug.LogError($"Error: The character {context.ImpactCollisionData.SourceCharacterLocomotion.gameObject} must have a NetworkObject component added.");
                    return;
                }
                sourceCharacterLocomotionViewID = (int)sourceCharacterLocomotionView.NetworkObjectId;
            }
            var sourceGameObject = NetCodeUtility.GetID(context.ImpactCollisionData.SourceGameObject, out var sourceGameObjectSlotID);
            if (!sourceGameObject.HasID) return;
            var impactGameObject = NetCodeUtility.GetID(context.ImpactCollisionData.ImpactGameObject, out var impactGameObjectSlotID);
            if (!impactGameObject.HasID) return;
            var impactCollider = NetCodeUtility.GetID(context.ImpactCollisionData.ImpactCollider.gameObject, out var colliderSlotID);
            if (!impactCollider.HasID) return;
            if (IsServer)
                InvokeMeleeImpactModulesClientRpc(itemAction.CharacterItem.SlotID, itemAction.ID, moduleGroup.ID, invokedBitmask,
                context.ImpactCollisionData.SourceID, sourceCharacterLocomotionViewID, sourceGameObject.ID, sourceGameObjectSlotID,
                impactGameObject.ID, impactGameObjectSlotID, impactCollider.ID, context.ImpactCollisionData.ImpactPosition,
                context.ImpactCollisionData.ImpactDirection, context.ImpactCollisionData.ImpactStrength);
            else
                InvokeMeleeImpactModulesServerRpc(itemAction.CharacterItem.SlotID, itemAction.ID, moduleGroup.ID, invokedBitmask,
                context.ImpactCollisionData.SourceID, sourceCharacterLocomotionViewID, sourceGameObject.ID, sourceGameObjectSlotID,
                impactGameObject.ID, impactGameObjectSlotID, impactCollider.ID, context.ImpactCollisionData.ImpactPosition,
                context.ImpactCollisionData.ImpactDirection, context.ImpactCollisionData.ImpactStrength);
        }
        /// <summary>
        /// Invokes the Melee Action Impact modules on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being invoked.</param>
        /// <param name="actionID">The ID of the ItemAction being invoked.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being invoked.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="sourceID">The ID of the impact.</param>
        /// <param name="sourceCharacterLocomotionViewID">The ID of the CharacterLocomotion component that caused the collision.</param>
        /// <param name="sourceGameObjectID">The ID of the GameObject that caused the collision.</param>
        /// <param name="sourceGameObjectSlotID">The slot ID if an item caused the collision.</param>
        /// <param name="impactGameObjectID">The ID of the GameObject that was impacted.</param>
        /// <param name="impactGameObjectSlotID">The slot ID of the item that was impacted.</param>
        /// <param name="impactColliderID">The ID of the Collider that was impacted.</param>
        /// <param name="impactPosition">The position of impact.</param>
        /// <param name="impactDirection">The direction of impact.</param>
        /// <param name="impactStrength">The strength of the impact.</param>
        private void InvokeMeleeImpactModulesRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, uint sourceID, int sourceCharacterLocomotionViewID,
                                                ulong sourceGameObjectID, int sourceGameObjectSlotID, ulong impactGameObjectID, int impactGameObjectSlotID,
                                                ulong impactColliderID, Vector3 impactPosition, Vector3 impactDirection, float impactStrength)
        {
            var moduleGroup = GetModuleGroup(slotID, actionID, moduleGroupID) as ActionModuleGroup<MeleeImpactModule>;
            if (moduleGroup == null || moduleGroup.ModuleCount == 0) return;
            var context = GenericObjectPool.Get<MeleeImpactCallbackContext>();
            if (context.ImpactCollisionData == null)
            {
                context.ImpactCollisionData = new ImpactCollisionData();
                context.ImpactDamageData = new ImpactDamageData();
            }
            var collisionData = context.ImpactCollisionData;
            if (!InitializeImpactCollisionData(ref collisionData, sourceID, sourceCharacterLocomotionViewID, sourceGameObjectID, sourceGameObjectSlotID,
                impactGameObjectID, impactGameObjectSlotID, impactColliderID, impactPosition, impactDirection, impactStrength))
            {
                GenericObjectPool.Return(context);
                return;
            }
            collisionData.SourceComponent = GetItemAction(slotID, actionID);
            context.ImpactCollisionData = collisionData;
            // The action will be the same across all modules.
            context.MeleeAction = moduleGroup.Modules[0].MeleeAction;
            for (int i = 0; i < moduleGroup.ModuleCount; i++)
            {
                // Not all modules are invoked.
                if (((1 << moduleGroup.Modules[i].ID) & invokedBitmask) == 0)
                    continue;
                moduleGroup.Modules[i].OnImpact(context);
            }
            GenericObjectPool.Return(context);
        }
        [ServerRpc]
        private void InvokeMeleeImpactModulesServerRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, uint sourceID, int sourceCharacterLocomotionViewID,
                                                       ulong sourceGameObjectID, int sourceGameObjectSlotID, ulong impactGameObjectID, int impactGameObjectSlotID,
                                                       ulong impactColliderID, Vector3 impactPosition, Vector3 impactDirection, float impactStrength)
        {
            if (!IsClient) InvokeMeleeImpactModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, sourceID, sourceCharacterLocomotionViewID,
                                                       sourceGameObjectID, sourceGameObjectSlotID, impactGameObjectID, impactGameObjectSlotID,
                                                       impactColliderID, impactPosition, impactDirection, impactStrength);
            InvokeMeleeImpactModulesClientRpc(slotID, actionID, moduleGroupID, invokedBitmask, sourceID, sourceCharacterLocomotionViewID,
                                              sourceGameObjectID, sourceGameObjectSlotID, impactGameObjectID, impactGameObjectSlotID,
                                              impactColliderID, impactPosition, impactDirection, impactStrength);
        }
        [ClientRpc]
        private void InvokeMeleeImpactModulesClientRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, uint sourceID, int sourceCharacterLocomotionViewID,
                                                       ulong sourceGameObjectID, int sourceGameObjectSlotID, ulong impactGameObjectID, int impactGameObjectSlotID,
                                                       ulong impactColliderID, Vector3 impactPosition, Vector3 impactDirection, float impactStrength)
        {
            if (!IsOwner) InvokeMeleeImpactModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, sourceID, sourceCharacterLocomotionViewID,
                                                      sourceGameObjectID, sourceGameObjectSlotID, impactGameObjectID, impactGameObjectSlotID,
                                                      impactColliderID, impactPosition, impactDirection, impactStrength);
        }
        /// <summary>
        /// Invokes the Throwable Action Effect modules.
        /// </summary>
        /// <param name="itemAction">The Item Action that is invoking the modules.</param>
        /// <param name="moduleGroup">The group that the modules belong to.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="data">The data being sent to the module.</param>
        public void InvokeThrowableEffectModules(CharacterItemAction itemAction, ActionModuleGroupBase moduleGroup, int invokedBitmask, ThrowableUseDataStream data)
        {
            if (IsServer)
                InvokeThrowableEffectModulesClientRpc(itemAction.CharacterItem.SlotID, itemAction.ID, moduleGroup.ID, invokedBitmask);
            else
                InvokeThrowableEffectModulesServerRpc(itemAction.CharacterItem.SlotID, itemAction.ID, moduleGroup.ID, invokedBitmask);
        }
        /// <summary>
        /// Invokes the Throwable Action Effect modules on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being invoked.</param>
        /// <param name="actionID">The ID of the ItemAction being invoked.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being invoked.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        private void InvokeThrowableEffectModulesRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask)
        {
            var moduleGroup = GetModuleGroup(slotID, actionID, moduleGroupID) as ActionModuleGroup<ThrowableThrowEffectModule>;
            if (moduleGroup == null || moduleGroup.ModuleCount == 0) return;
            var data = GenericObjectPool.Get<ThrowableUseDataStream>();
            // The action will be the same across all modules.
            data.ThrowableAction = moduleGroup.Modules[0].ThrowableAction;
            for (int i = 0; i < moduleGroup.ModuleCount; i++)
            {
                // Not all modules are invoked.
                if (((1 << moduleGroup.Modules[i].ID) & invokedBitmask) == 0)
                    continue;
                moduleGroup.Modules[i].InvokeEffect(data);
            }
            GenericObjectPool.Return(data);
        }
        [ServerRpc]
        private void InvokeThrowableEffectModulesServerRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask)
        {
            if (!IsClient) InvokeThrowableEffectModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask);
            InvokeThrowableEffectModulesClientRpc(slotID, actionID, moduleGroupID, invokedBitmask);
        }
        [ClientRpc]
        private void InvokeThrowableEffectModulesClientRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask)
        {
            if (!IsOwner) InvokeThrowableEffectModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask);
        }
        /// <summary>
        /// Enables the object mesh renderers for the Throwable Action.
        /// </summary>
        /// <param name="module">The module that is having the renderers enabled.</param>
        /// <param name="enable">Should the renderers be enabled?</param>
        public void EnableThrowableObjectMeshRenderers(ActionModule module, bool enable)
        {
            if (IsServer)
                EnableThrowableObjectMeshRenderersClientRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID, enable);
            else
                EnableThrowableObjectMeshRenderersServerRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID, enable);
        }
        /// <summary>
        /// Enables the object mesh renderers for the Throwable Action on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being invoked.</param>
        /// <param name="actionID">The ID of the ItemAction being invoked.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being invoked.</param>
        /// <param name="moduleID">The ID of the module being invoked.</param>
        /// <param name="enable">Should the renderers be enabled?</param>
        private void EnableThrowableObjectMeshRenderersRpc(int slotID, int actionID, int moduleGroupID, int moduleID, bool enable)
        {
            var module = GetModule<Opsive.UltimateCharacterController.Items.Actions.Modules.Throwable.SpawnProjectile>(slotID, actionID, moduleGroupID, moduleID);
            module?.EnableObjectMeshRenderers(enable);
        }
        [ServerRpc]
        private void EnableThrowableObjectMeshRenderersServerRpc(int slotID, int actionID, int moduleGroupID, int moduleID, bool enable)
        {
            if (!IsClient) { EnableThrowableObjectMeshRenderersRpc(slotID, actionID, moduleGroupID, moduleID, enable); }
            EnableThrowableObjectMeshRenderersClientRpc(slotID, actionID, moduleGroupID, moduleID, enable);
        }
        [ClientRpc]
        private void EnableThrowableObjectMeshRenderersClientRpc(int slotID, int actionID, int moduleGroupID, int moduleID, bool enable)
        {
            if (!IsOwner) { EnableThrowableObjectMeshRenderersRpc(slotID, actionID, moduleGroupID, moduleID, enable); }
        }
        /// <summary>
        /// Invokes the Magic Action Begin or End modules.
        /// </summary>
        /// <param name="itemAction">The Item Action that is invoking the modules.</param>
        /// <param name="moduleGroup">The group that the modules belong to.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="start">Should the module be started? If false the module will be stopped.</param>
        /// <param name="data">The data being sent to the module.</param>
        public void InvokeMagicBeginEndModules(CharacterItemAction itemAction, ActionModuleGroupBase moduleGroup, int invokedBitmask, bool start, MagicUseDataStream data)
        {
            if (IsServer)
                InvokeMagicBeginEndModulesClientRpc(itemAction.CharacterItem.SlotID, itemAction.ID, moduleGroup.ID, invokedBitmask, start);
            else
                InvokeMagicBeginEndModulesServerRpc(itemAction.CharacterItem.SlotID, itemAction.ID, moduleGroup.ID, invokedBitmask, start);
        }
        /// <summary>
        /// Invokes the Magic Begin or End modules on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being invoked.</param>
        /// <param name="actionID">The ID of the ItemAction being invoked.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being invoked.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="start">Should the module be started? If false the module will be stopped.</param>
        private void InvokeMagicBeginEndModulesRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, bool start)
        {
            var moduleGroup = GetModuleGroup(slotID, actionID, moduleGroupID) as ActionModuleGroup<MagicStartStopModule>;
            if (moduleGroup == null || moduleGroup.ModuleCount == 0) return;
            var data = GenericObjectPool.Get<MagicUseDataStream>();
            data.MagicAction = moduleGroup.Modules[0].MagicAction; // The action will be the same across all modules.
            for (int i = 0; i < moduleGroup.ModuleCount; i++)
            {
                // Not all modules are invoked.
                if (((1 << moduleGroup.Modules[i].ID) & invokedBitmask) == 0) continue;
                if (start)
                    moduleGroup.Modules[i].Start(data);
                else
                    moduleGroup.Modules[i].Stop(data);
            }
            GenericObjectPool.Return(data);
        }
        [ServerRpc]
        private void InvokeMagicBeginEndModulesServerRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, bool start)
        {
            if (!IsClient) InvokeMagicBeginEndModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, start);
            InvokeMagicBeginEndModulesClientRpc(slotID, actionID, moduleGroupID, invokedBitmask, start);
        }
        [ClientRpc]
        private void InvokeMagicBeginEndModulesClientRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, bool start)
        {
            if (!IsOwner) InvokeMagicBeginEndModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, start);
        }
        /// <summary>
        /// Invokes the Magic Cast Effect modules.
        /// </summary>
        /// <param name="itemAction">The Item Action that is invoking the modules.</param>
        /// <param name="moduleGroup">The group that the modules belong to.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="state">Specifies the state of the cast.</param>
        /// <param name="data">The data being sent to the module.</param>
        public void InvokeMagicCastEffectsModules(CharacterItemAction itemAction, ActionModuleGroupBase moduleGroup, int invokedBitmask, INetworkCharacter.CastEffectState state, MagicUseDataStream data)
        {
            var originTransform = NetCodeUtility.GetID(data.CastData.CastOrigin?.gameObject, out var originTransformSlotID);
            if (IsServer)
                InvokeMagicCastEffectsModulesClientRpc(itemAction.CharacterItem.SlotID, itemAction.ID, moduleGroup.ID, invokedBitmask, (short)state,
                data.CastData.CastID, data.CastData.StartCastTime, originTransform.ID, originTransformSlotID, data.CastData.CastPosition, data.CastData.CastNormal,
                data.CastData.Direction, data.CastData.CastTargetPosition);
            else
                InvokeMagicCastEffectsModulesServerRpc(itemAction.CharacterItem.SlotID, itemAction.ID, moduleGroup.ID, invokedBitmask, (short)state,
                data.CastData.CastID, data.CastData.StartCastTime, originTransform.ID, originTransformSlotID, data.CastData.CastPosition, data.CastData.CastNormal,
                data.CastData.Direction, data.CastData.CastTargetPosition);
        }
        /// <summary>
        /// Invokes the Magic Cast Effects modules on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being invoked.</param>
        /// <param name="actionID">The ID of the ItemAction being invoked.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being invoked.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="state">Specifies the state of the cast.</param>
        private void InvokeMagicCastEffectsModulesRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, short state, uint castID, float startCastTime,
                                                      ulong originTransformID, int originTransformSlotID, Vector3 castPosition, Vector3 castNormal, Vector3 direction, Vector3 castTargetPosition)
        {
            var moduleGroup = GetModuleGroup(slotID, actionID, moduleGroupID) as ActionModuleGroup<MagicCastEffectModule>;
            if (moduleGroup == null || moduleGroup.ModuleCount == 0) return;
            var data = GenericObjectPool.Get<MagicUseDataStream>();
            data.CastData ??= new MagicCastData();
            // The action will be the same across all modules.
            data.MagicAction = moduleGroup.Modules[0].MagicAction;
            data.CastData.CastID = castID;
            data.CastData.StartCastTime = startCastTime;
            data.CastData.CastPosition = castPosition;
            data.CastData.CastNormal = castNormal;
            data.CastData.Direction = direction;
            data.CastData.CastTargetPosition = castTargetPosition;
            var originGameObject = NetCodeUtility.RetrieveGameObject(null, originTransformID, originTransformSlotID);
            if (originGameObject != null)
                data.CastData.CastOrigin = originGameObject.transform;
            for (int i = 0; i < moduleGroup.ModuleCount; ++i)
            {
                // Not all modules are invoked.
                if ((moduleGroup.Modules[i].ID & invokedBitmask) == 0)
                    continue;
                switch ((INetworkCharacter.CastEffectState)state)
                {
                    case INetworkCharacter.CastEffectState.Start:
                        moduleGroup.Modules[i].StartCast(data);
                        break;
                    case INetworkCharacter.CastEffectState.Update:
                        moduleGroup.Modules[i].OnCastUpdate(data);
                        break;
                    case INetworkCharacter.CastEffectState.End:
                        moduleGroup.Modules[i].StopCast();
                        break;
                }
            }
            GenericObjectPool.Return(data);
        }
        [ServerRpc]
        private void InvokeMagicCastEffectsModulesServerRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, short state, uint castID, float startCastTime,
                                                            ulong originTransformID, int originTransformSlotID, Vector3 castPosition, Vector3 castNormal, Vector3 direction, Vector3 castTargetPosition)
        {
            if (!IsClient) InvokeMagicCastEffectsModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, state, castID, startCastTime,
                                                            originTransformID, originTransformSlotID, castPosition, castNormal, direction, castTargetPosition);
            InvokeMagicCastEffectsModulesClientRpc(slotID, actionID, moduleGroupID, invokedBitmask, state, castID, startCastTime,
                                                   originTransformID, originTransformSlotID, castPosition, castNormal, direction, castTargetPosition);
        }
        [ClientRpc]
        private void InvokeMagicCastEffectsModulesClientRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, short state, uint castID, float startCastTime,
                                                            ulong originTransformID, int originTransformSlotID, Vector3 castPosition, Vector3 castNormal, Vector3 direction, Vector3 castTargetPosition)
        {
            if (!IsOwner) InvokeMagicCastEffectsModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, state, castID, startCastTime,
                                                            originTransformID, originTransformSlotID, castPosition, castNormal, direction, castTargetPosition);
        }
        /// <summary>
        /// Invokes the Magic Action Impact modules.
        /// </summary>
        /// <param name="itemAction">The Item Action that is invoking the modules.</param>
        /// <param name="moduleGroup">The group that the modules belong to.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="context">The context being sent to the module.</param>
        public void InvokeMagicImpactModules(CharacterItemAction itemAction, ActionModuleGroupBase moduleGroup, int invokedBitmask, ImpactCallbackContext context)
        {
            var sourceCharacterLocomotionViewID = -1;
            if (context.ImpactCollisionData.SourceCharacterLocomotion != null)
            {
                var sourceCharacterLocomotionView = context.ImpactCollisionData.SourceCharacterLocomotion.gameObject.GetCachedComponent<NetworkObject>();
                if (sourceCharacterLocomotionView == null)
                {
                    Debug.LogError($"Error: The character {context.ImpactCollisionData.SourceCharacterLocomotion.gameObject} must have a NetworkObject component added.");
                    return;
                }
                sourceCharacterLocomotionViewID = (int)sourceCharacterLocomotionView.NetworkObjectId;
            }
            var sourceGameObject = NetCodeUtility.GetID(context.ImpactCollisionData.SourceGameObject, out var sourceGameObjectSlotID);
            if (!sourceGameObject.HasID) return;
            var impactGameObject = NetCodeUtility.GetID(context.ImpactCollisionData.ImpactGameObject, out var impactGameObjectSlotID);
            if (!impactGameObject.HasID) return;
            var impactCollider = NetCodeUtility.GetID(context.ImpactCollisionData.ImpactCollider.gameObject, out var colliderSlotID);
            if (!impactCollider.HasID) return;
            if (IsServer)
                InvokeMagicImpactModulesClientRpc(itemAction.CharacterItem.SlotID, itemAction.ID, moduleGroup.ID, invokedBitmask, context.ImpactCollisionData.SourceID,
                sourceCharacterLocomotionViewID, sourceGameObject.ID, sourceGameObjectSlotID, impactGameObject.ID, impactGameObjectSlotID, impactCollider.ID,
                context.ImpactCollisionData.ImpactPosition, context.ImpactCollisionData.ImpactDirection, context.ImpactCollisionData.ImpactStrength);
            else
                InvokeMagicImpactModulesServerRpc(itemAction.CharacterItem.SlotID, itemAction.ID, moduleGroup.ID, invokedBitmask, context.ImpactCollisionData.SourceID,
                sourceCharacterLocomotionViewID, sourceGameObject.ID, sourceGameObjectSlotID, impactGameObject.ID, impactGameObjectSlotID, impactCollider.ID,
                context.ImpactCollisionData.ImpactPosition, context.ImpactCollisionData.ImpactDirection, context.ImpactCollisionData.ImpactStrength);
        }

        /// <summary>
        /// Invokes the Magic Action Impact modules on the network.
        /// </summary>
        /// <param name="itemAction">The Item Action that is invoking the modules.</param>
        /// <param name="moduleGroup">The group that the modules belong to.</param>
        /// <param name="invokedBitmask">The bitmask of the invoked modules.</param>
        /// <param name="context">The context being sent to the module.</param>
        /// <param name="sourceID">The ID of the impact.</param>
        /// <param name="sourceCharacterLocomotionViewID">The ID of the CharacterLocomotion component that caused the collision.</param>
        /// <param name="sourceGameObjectID">The ID of the GameObject that caused the collision.</param>
        /// <param name="sourceGameObjectSlotID">The slot ID if an item caused the collision.</param>
        /// <param name="impactGameObjectID">The ID of the GameObject that was impacted.</param>
        /// <param name="impactGameObjectSlotID">The slot ID of the item that was impacted.</param>
        /// <param name="impactColliderID">The ID of the Collider that was impacted.</param>
        /// <param name="impactPosition">The position of impact.</param>
        /// <param name="impactDirection">The direction of impact.</param>
        /// <param name="impactStrength">The strength of the impact.</param>
        public void InvokeMagicImpactModulesRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, uint sourceID, int sourceCharacterLocomotionViewID,
                                                ulong sourceGameObjectID, int sourceGameObjectSlotID, ulong impactGameObjectID, int impactGameObjectSlotID,
                                                ulong impactColliderID, Vector3 impactPosition, Vector3 impactDirection, float impactStrength)
        {
            var moduleGroup = GetModuleGroup(slotID, actionID, moduleGroupID) as ActionModuleGroup<MagicImpactModule>;
            if (moduleGroup == null || moduleGroup.ModuleCount == 0) return;
            var context = GenericObjectPool.Get<ImpactCallbackContext>();
            if (context.ImpactCollisionData == null)
            {
                context.ImpactCollisionData = new ImpactCollisionData();
                context.ImpactDamageData = new ImpactDamageData();
            }
            var collisionData = context.ImpactCollisionData;
            if (!InitializeImpactCollisionData(ref collisionData, sourceID, sourceCharacterLocomotionViewID, sourceGameObjectID, sourceGameObjectSlotID, impactGameObjectID, impactGameObjectSlotID,
                                                impactColliderID, impactPosition, impactDirection, impactStrength))
            {
                GenericObjectPool.Return(context);
                return;
            }
            collisionData.SourceComponent = GetItemAction(slotID, actionID);
            context.ImpactCollisionData = collisionData;
            for (int i = 0; i < moduleGroup.ModuleCount; ++i)
            {
                // Not all modules are invoked.
                if (((1 << moduleGroup.Modules[i].ID) & invokedBitmask) == 0)
                    continue;
                moduleGroup.Modules[i].OnImpact(context);
            }
            GenericObjectPool.Return(context);
        }
        [ServerRpc]
        private void InvokeMagicImpactModulesServerRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, uint sourceID, int sourceCharacterLocomotionViewID,
                                                       ulong sourceGameObjectID, int sourceGameObjectSlotID, ulong impactGameObjectID, int impactGameObjectSlotID,
                                                       ulong impactColliderID, Vector3 impactPosition, Vector3 impactDirection, float impactStrength)
        {
            if (!IsClient) InvokeMagicImpactModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, sourceID, sourceCharacterLocomotionViewID,
                                                       sourceGameObjectID, sourceGameObjectSlotID, impactGameObjectID, impactGameObjectSlotID,
                                                       impactColliderID, impactPosition, impactDirection, impactStrength);
            InvokeMagicImpactModulesClientRpc(slotID, actionID, moduleGroupID, invokedBitmask, sourceID, sourceCharacterLocomotionViewID,
                                              sourceGameObjectID, sourceGameObjectSlotID, impactGameObjectID, impactGameObjectSlotID,
                                              impactColliderID, impactPosition, impactDirection, impactStrength);
        }
        [ClientRpc]
        private void InvokeMagicImpactModulesClientRpc(int slotID, int actionID, int moduleGroupID, int invokedBitmask, uint sourceID, int sourceCharacterLocomotionViewID,
                                                       ulong sourceGameObjectID, int sourceGameObjectSlotID, ulong impactGameObjectID, int impactGameObjectSlotID,
                                                       ulong impactColliderID, Vector3 impactPosition, Vector3 impactDirection, float impactStrength)
        {
            if (!IsOwner) InvokeMagicImpactModulesRpc(slotID, actionID, moduleGroupID, invokedBitmask, sourceID, sourceCharacterLocomotionViewID,
                                                      sourceGameObjectID, sourceGameObjectSlotID, impactGameObjectID, impactGameObjectSlotID,
                                                      impactColliderID, impactPosition, impactDirection, impactStrength);
        }
        /// <summary>
        /// Invokes the Usable Action Geenric Effect module.
        /// </summary>
        /// <param name="module">The module that should be invoked.</param>
        public void InvokeGenericEffectModule(ActionModule module)
        {
            if (IsServer)
                InvokeGenericEffectModuleClientRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID);
            else
                InvokeGenericEffectModuleServerRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID);
        }
        /// <summary>
        /// Invokes the Usable Action Geenric Effect module on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being invoked.</param>
        /// <param name="actionID">The ID of the ItemAction being invoked.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being invoked.</param>
        /// <param name="moduleID">The ID of the module being invoked.</param>
        private void InvokeGenericEffectModuleRpc(int slotID, int actionID, int moduleGroupID, int moduleID)
        {
            var module = GetModule<Opsive.UltimateCharacterController.Items.Actions.Modules.GenericItemEffects>(slotID, actionID, moduleGroupID, moduleID);
            module?.EffectGroup.InvokeEffects();
        }
        [ServerRpc]
        private void InvokeGenericEffectModuleServerRpc(int slotID, int actionID, int moduleGroupID, int moduleID)
        {
            if (!IsClient) InvokeGenericEffectModuleRpc(slotID, actionID, moduleGroupID, moduleID);
            InvokeGenericEffectModuleClientRpc(slotID, actionID, moduleGroupID, moduleID);
        }
        [ClientRpc]
        private void InvokeGenericEffectModuleClientRpc(int slotID, int actionID, int moduleGroupID, int moduleID)
        {
            if (!IsOwner) InvokeGenericEffectModuleRpc(slotID, actionID, moduleGroupID, moduleID);
        }
        /// <summary>
        /// Invokes the Use Attribute Modifier Toggle module.
        /// </summary>
        /// <param name="module">The module that should be invoked.</param>
        /// <param name="on">Should the module be toggled on?</param>
        public void InvokeUseAttributeModifierToggleModule(ActionModule module, bool on)
        {
            if (IsServer)
                InvokeUseAttributeModifierToggleModuleClientRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID, on);
            else
                InvokeUseAttributeModifierToggleModuleServerRpc(module.CharacterItem.SlotID, module.CharacterItemAction.ID, module.ModuleGroup.ID, module.ID, on);
        }
        /// <summary>
        /// Invokes the Usable Action Geenric Effect module on the network.
        /// </summary>
        /// <param name="slotID">The SlotID of the module that is being invoked.</param>
        /// <param name="actionID">The ID of the ItemAction being invoked.</param>
        /// <param name="moduleGroupID">The ID of the ModuleGroup being invoked.</param>
        /// <param name="moduleID">The ID of the module being invoked.</param>
        /// <param name="on">Should the module be toggled on?</param>
        private void InvokeUseAttributeModifierToggleModuleRpc(int slotID, int actionID, int moduleGroupID, int moduleID, bool on)
        {
            var module = GetModule<UseAttributeModifierToggle>(slotID, actionID, moduleGroupID, moduleID);
            module?.ToggleGameObjects(on);
        }
        [ServerRpc]
        private void InvokeUseAttributeModifierToggleModuleServerRpc(int slotID, int actionID, int moduleGroupID, int moduleID, bool on)
        {
            if (!IsClient) InvokeUseAttributeModifierToggleModuleRpc(slotID, actionID, moduleGroupID, moduleID, on);
            InvokeUseAttributeModifierToggleModuleClientRpc(slotID, actionID, moduleGroupID, moduleID, on);
        }
        [ClientRpc]
        private void InvokeUseAttributeModifierToggleModuleClientRpc(int slotID, int actionID, int moduleGroupID, int moduleID, bool on)
        {
            if (!IsOwner) InvokeUseAttributeModifierToggleModuleRpc(slotID, actionID, moduleGroupID, moduleID, on);
        }
        /// <summary>
        /// Pushes the target Rigidbody in the specified direction.
        /// </summary>
        /// <param name="targetRigidbody">The Rigidbody to push.</param>
        /// <param name="force">The amount of force to apply.</param>
        /// <param name="point">The point at which to apply the push force.</param>
        public void PushRigidbody(Rigidbody targetRigidbody, Vector3 force, Vector3 point)
        {
            var target = targetRigidbody.gameObject.GetCachedComponent<NetworkObject>();
            if (target == null)
            {
                Debug.LogError($"Error: The object {targetRigidbody.gameObject} must have a NetworkObject component added.");
            }
            else
            {
                if (IsOwner)
                    PushRigidbodyServerRpc(target.NetworkObjectId, force, point);
            }
        }
        /// <summary>
        /// Pushes the target Rigidbody in the specified direction on the network.
        /// </summary>
        /// <param name="targetRigidbody">The Rigidbody to push.</param>
        /// <param name="force">The amount of force to apply.</param>
        /// <param name="point">The point at which to apply the push force.</param>
        private void PushRigidbodyRpc(ulong rigidbodyNetworkObjectId, Vector3 force, Vector3 point)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue((ulong)rigidbodyNetworkObjectId, out var net))
            {
                var targetRigidbody = net.gameObject.GetComponent<Rigidbody>();
                targetRigidbody?.AddForceAtPosition(force, point, ForceMode.VelocityChange);
            }
        }
        [ServerRpc]
        private void PushRigidbodyServerRpc(ulong rigidbodyNetworkObjectId, Vector3 force, Vector3 point)
        {
            if (!IsClient) PushRigidbodyRpc(rigidbodyNetworkObjectId, force, point);
            PushRigidbodyClientRpc(rigidbodyNetworkObjectId, force, point);
        }
        [ClientRpc]
        private void PushRigidbodyClientRpc(ulong rigidbodyNetworkObjectId, Vector3 force, Vector3 point)
        {
            if (!IsOwner) PushRigidbodyRpc(rigidbodyNetworkObjectId, force, point);
        }
        /// <summary>
        /// Sets the rotation of the character.
        /// </summary>
        /// <param name="rotation">The rotation to set.</param>
        /// <param name="snapAnimator">Should the animator be snapped into position?</param>
        public void SetRotation(Quaternion rotation, bool snapAnimator)
        {
            if (IsServer)
                SetRotationClientRpc(rotation, snapAnimator);
            else
                SetRotationServerRpc(rotation, snapAnimator);
        }
        /// <summary>
        /// Sets the rotation of the character.
        /// </summary>
        /// <param name="rotation">The rotation to set.</param>
        /// <param name="snapAnimator">Should the animator be snapped into position?</param>
        public void SetRotationRpc(Quaternion rotation, bool snapAnimator) =>
        m_CharacterLocomotion.SetRotation(rotation, snapAnimator);
        [ServerRpc]
        public void SetRotationServerRpc(Quaternion rotation, bool snapAnimator)
        {
            if (!IsClient) SetRotationRpc(rotation, snapAnimator);
            SetRotationClientRpc(rotation, snapAnimator);
        }
        [ClientRpc]
        public void SetRotationClientRpc(Quaternion rotation, bool snapAnimator)
        {
            if (!IsOwner) SetRotationRpc(rotation, snapAnimator);
        }
        /// <summary>
        /// Sets the position of the character.
        /// </summary>
        /// <param name="position">The position to set.</param>
        /// <param name="snapAnimator">Should the animator be snapped into position?</param>
        public void SetPosition(Vector3 position, bool snapAnimator)
        {
            if (IsServer)
                SetPositionClientRpc(position, snapAnimator);
            else
                SetPositionServerRpc(position, snapAnimator);
        }
        /// <summary>
        /// Sets the position of the character.
        /// </summary>
        /// <param name="position">The position to set.</param>
        /// <param name="snapAnimator">Should the animator be snapped into position?</param>
        public void SetPositionRpc(Vector3 position, bool snapAnimator) =>
        m_CharacterLocomotion.SetPosition(position, snapAnimator);
        [ServerRpc]
        public void SetPositionServerRpc(Vector3 position, bool snapAnimator)
        {
            if (!IsClient) SetPositionRpc(position, snapAnimator);
            SetPositionClientRpc(position, snapAnimator);
        }
        [ClientRpc]
        public void SetPositionClientRpc(Vector3 position, bool snapAnimator)
        {
            if (!IsOwner) SetPositionRpc(position, snapAnimator);
        }
        /// <summary>
        /// Resets the rotation and position to their default values.
        /// </summary>
        public void ResetRotationPosition()
        {
            if (IsServer)
                ResetRotationPositionClientRpc();
            else
                ResetRotationPositionServerRpc();
        }
        /// <summary>
        /// Resets the rotation and position to their default values on the network.
        /// </summary>
        public void ResetRotationPositionRpc() =>
        m_CharacterLocomotion.ResetRotationPosition();
        [ServerRpc]
        public void ResetRotationPositionServerRpc()
        {
            if (!IsClient) ResetRotationPositionRpc();
            ResetRotationPositionClientRpc();
        }
        [ClientRpc]
        public void ResetRotationPositionClientRpc()
        {
            if (!IsOwner) ResetRotationPositionRpc();
        }
        /// <summary>
        /// Sets the position and rotation of the character on the network.
        /// </summary>
        /// <param name="position">The position to set.</param>
        /// <param name="rotation">The rotation to set.</param>
        /// <param name="snapAnimator">Should the animator be snapped into position?</param>
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation, bool snapAnimator, bool stopAllAbilities)
        {
            if (IsServer)
                SetPositionAndRotationClientRpc(position, rotation, snapAnimator, stopAllAbilities);
            else
                SetPositionAndRotationServerRpc(position, rotation, snapAnimator, stopAllAbilities);
        }
        /// <summary>
        /// Sets the position and rotation of the character.
        /// </summary>
        /// <param name="position">The position to set.</param>
        /// <param name="rotation">The rotation to set.</param>
        /// <param name="snapAnimator">Should the animator be snapped into position?</param>
        public void SetPositionAndRotationRpc(Vector3 position, Quaternion rotation, bool snapAnimator, bool stopAllAbilities) =>
        m_CharacterLocomotion.SetPositionAndRotation(position, rotation, snapAnimator, stopAllAbilities);
        [ServerRpc]
        public void SetPositionAndRotationServerRpc(Vector3 position, Quaternion rotation, bool snapAnimator, bool stopAllAbilities)
        {
            if (!IsClient) SetPositionAndRotationRpc(position, rotation, stopAllAbilities, snapAnimator);
            SetPositionAndRotationClientRpc(position, rotation, stopAllAbilities, snapAnimator);
        }
        [ClientRpc]
        public void SetPositionAndRotationClientRpc(Vector3 position, Quaternion rotation, bool snapAnimator, bool stopAllAbilities)
        {
            if (!IsOwner) SetPositionAndRotationRpc(position, rotation, stopAllAbilities, snapAnimator);
        }
        /// <summary>
        /// Changes the character model.
        /// </summary>
        /// <param name="modelIndex">The index of the model within the ModelManager.</param>
        public void ChangeModels(int modelIndex)
        {
            if (IsServer)
                ChangeModelsClientRpc(modelIndex);
            else
                ChangeModelsServerRpc(modelIndex);
        }
        /// <summary>
        /// Changes the character model on the network.
        /// </summary>
        /// <param name="modelIndex">The index of the model within the ModelManager.</param>
        private void ChangeModelsRpc(int modelIndex)
        {
            if (modelIndex < 0 || m_ModelManager.AvailableModels == null || modelIndex >= m_ModelManager.AvailableModels.Length)
                return;
            // ULTIMATE_CHARACTER_CONTROLLER_MULTIPLAYER will be defined, but it is required here to allow the add-on to be compiled for the first time.
#if ULTIMATE_CHARACTER_CONTROLLER_MULTIPLAYER
            m_ModelManager.ChangeModels(m_ModelManager.AvailableModels[modelIndex], true);
#endif
        }
        [ServerRpc]
        private void ChangeModelsServerRpc(int modelIndex)
        {
            if (!IsClient) ChangeModelsRpc(modelIndex);
            ChangeModelsClientRpc(modelIndex);
        }
        [ClientRpc]
        private void ChangeModelsClientRpc(int modelIndex)
        {
            if (!IsOwner) ChangeModelsRpc(modelIndex);
        }
        /// <summary>
        /// Activates or deactivates the character.
        /// </summary>
        /// <param name="active">Is the character active?</param>
        /// <param name="uiEvent">Should the OnShowUI event be executed?</param>
        public void SetActive(bool active, bool uiEvent)
        {
            if (IsServer)
                SetActiveClientRpc(active, uiEvent);
            else
                SetActiveServerRpc(active, uiEvent);
        }
        /// <summary>
        /// Activates or deactivates the character on the network.
        /// </summary>
        /// <param name="active">Is the character active?</param>
        /// <param name="uiEvent">Should the OnShowUI event be executed?</param>
        private void SetActiveRpc(bool active, bool uiEvent)
        {
            m_GameObject.SetActive(active);
            if (uiEvent)
                EventHandler.ExecuteEvent(m_GameObject, "OnShowUI", active);
        }
        [ServerRpc]
        private void SetActiveServerRpc(bool active, bool uiEvent)
        {
            if (!IsClient) SetActiveRpc(active, uiEvent);
            SetActiveClientRpc(active, uiEvent);
        }
        [ClientRpc]
        private void SetActiveClientRpc(bool active, bool uiEvent)
        {
            if (!IsOwner) SetActiveRpc(active, uiEvent);
        }
        /// <summary>
        /// Executes a bool event.
        /// </summary>
        /// <param name="eventName">The name of the event.</param>
        /// <param name="value">The bool value.</param>
        public void ExecuteBoolEvent(string eventName, bool value)
        {
            if (IsServer)
                ExecuteBoolEventClientRpc(eventName, value);
            else
                ExecuteBoolEventServerRpc(eventName, value);
        }
        /// <summary>
        /// Executes a bool event on the network.
        /// </summary>
        /// <param name="eventName">The name of the event.</param>
        /// <param name="value">The bool value.</param>
        private void ExecuteBoolEventRpc(string eventName, bool value) =>
        EventHandler.ExecuteEvent(m_GameObject, eventName, value);
        [ServerRpc]
        private void ExecuteBoolEventServerRpc(string eventName, bool value)
        {
            if (!IsClient) ExecuteBoolEventRpc(eventName, value);
            ExecuteBoolEventClientRpc(eventName, value);
        }
        [ClientRpc]
        private void ExecuteBoolEventClientRpc(string eventName, bool value)
        {
            if (!IsOwner) ExecuteBoolEventRpc(eventName, value);
        }
    }
}