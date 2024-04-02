using System.Collections.Generic;
using Opsive.Shared.Game;
using Opsive.UltimateCharacterController.Character;
using Opsive.UltimateCharacterController.Character.Abilities;
using Opsive.UltimateCharacterController.Networking.Traits;
using Opsive.UltimateCharacterController.Traits;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Synchronizes the Interactable component over the network.
/// </summary>
namespace GreedyVox.NetCode
{
    public class NetCodeInteractableMonitor : NetworkBehaviour, INetworkInteractableMonitor
    {
        private GameObject m_GameObject;
        private Interactable m_Interactable;
        private NetCodeSettingsAbstract m_Settings;
        private Dictionary<ulong, NetworkObject> m_NetworkObjects;
        /// <summary>
        /// Initializes the default values.
        /// </summary>
        private void Awake()
        {
            m_GameObject = gameObject;
            m_NetworkObjects = NetworkManager.Singleton.SpawnManager.SpawnedObjects;
            m_Settings = NetCodeManager.Instance.NetworkSettings;
            m_Interactable = m_GameObject.GetCachedComponent<Interactable>();
        }
        /// <summary>
        /// Performs the interaction.
        /// </summary>
        /// <param name="character">The character that wants to interactact with the target.</param>
        /// <param name="interactAbility">The Interact ability that performed the interaction.</param>
        public void Interact(GameObject character, Interact interactAbility)
        {
            var net = character.GetCachedComponent<NetworkObject>();
            if (net == null)
                Debug.LogError("Error: The character " + character.name + " must have a NetworkObject component.");
            else
                if (IsServer)
                InteractClientRpc(net, interactAbility.Index);
            else
                InteractServerRpc(net, interactAbility.Index);
        }
        /// <summary>
        /// Performs the interaction on the network.
        /// </summary>
        /// <param name="character">The character that performed the interaction.</param>
        /// <param name="abilityIndex">The index of the Interact ability that performed the interaction.</param>
        private void InteractRpc(NetworkObjectReference character, int abilityIndex)
        {
            if (character.TryGet(out var net))
            {
                var go = net.gameObject;
                var characterLocomotion = net.gameObject.GetCachedComponent<UltimateCharacterLocomotion>();
                if (characterLocomotion != null)
                {
                    var interact = characterLocomotion.GetAbility<Interact>(abilityIndex);
                    m_Interactable.Interact(go, interact);
                }
            }
        }
        [ServerRpc]
        private void InteractServerRpc(NetworkObjectReference character, int abilityIndex)
        {
            if (!IsClient) { InteractRpc(character, abilityIndex); }
            InteractClientRpc(character, abilityIndex);
        }
        [ClientRpc]
        private void InteractClientRpc(NetworkObjectReference character, int abilityIndex)
        {
            if (!IsOwner) { InteractRpc(character, abilityIndex); }
        }
    }
}