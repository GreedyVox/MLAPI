using System.Collections.Generic;
using Opsive.Shared.Events;
using Opsive.Shared.Game;
using Opsive.Shared.Networking;
using Opsive.UltimateCharacterController.Character;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Synchronizes the Ultimate Character Controller animator across the network.
/// </summary>
namespace GreedyVox.NetCode.Character
{
    public class NetCodeCharacterAnimatorMonitor : NetworkBehaviour
    {
        /// <summary>
        /// Specifies which parameters are dirty.
        /// </summary>
        public enum ParameterDirtyFlags : short
        {
            HorizontalMovement = 1, // The Horizontal Movement parameter has changed.
            ForwardMovement = 2, // The Forward Movement parameter has changed.
            Pitch = 4, // The Pitch parameter has changed.
            Yaw = 8, // The Yaw parameter has changed.
            Speed = 16, // The Speed parameter has changed.
            Height = 32, // The Height parameter has changed.
            Moving = 64, // The Moving parameter has changed.
            Aiming = 128, // The Aiming parameter has changed.
            MovementSetID = 256, // The Movement Set ID parameter has changed.
            AbilityIndex = 512, // The Ability Index parameter has changed.
            AbilityIntData = 1024, // The Ability Int Data parameter has changed.
            AbilityFloatData = 2048 // The Ability Float Data parameter has changed.
        }
        private short m_Flag;
        private ulong m_ServerID;
        private int m_MaxBufferSize;
        private IReadOnlyList<ulong> m_Clients;
        private NetCodeManager m_NetworkManager;
        private FastBufferWriter m_FastBufferWriter;
        private string m_MsgServerPara, m_MsgServerItems;
        private string m_MsgNameClient, m_MsgNameServer;
        private CustomMessagingManager m_CustomMessagingManager;

        private GameObject m_GameObject;
        private AnimatorMonitor m_AnimatorMonitor;
        private int m_SnappedAbilityIndex;
        private short m_DirtyFlag;
        private byte m_ItemDirtySlot;

        private float m_NetworkHorizontalMovement;
        private float m_NetworkForwardMovement;
        private float m_NetworkPitch;
        private float m_NetworkYaw;
        private float m_NetworkSpeed;
        private float m_NetworkAbilityFloatData;

        public short DirtyFlag { get => m_DirtyFlag; set => m_DirtyFlag = value; }
        public byte ItemDirtySlot { get => m_ItemDirtySlot; set => m_ItemDirtySlot = value; }
        private float HorizontalMovement { get => m_AnimatorMonitor.HorizontalMovement; }
        private float ForwardMovement { get => m_AnimatorMonitor.ForwardMovement; }
        private float Pitch { get => m_AnimatorMonitor.Pitch; }
        private float Yaw { get => m_AnimatorMonitor.Yaw; }
        private float Speed { get => m_AnimatorMonitor.Speed; }
        private float Height { get => m_AnimatorMonitor.Height; }
        private bool Moving { get => m_AnimatorMonitor.Moving; }
        private bool Aiming { get => m_AnimatorMonitor.Aiming; }
        private int MovementSetID { get => m_AnimatorMonitor.MovementSetID; }
        private int AbilityIndex { get => m_AnimatorMonitor.AbilityIndex; }
        private int AbilityIntData { get => m_AnimatorMonitor.AbilityIntData; }
        private float AbilityFloatData { get => m_AnimatorMonitor.AbilityFloatData; }
        private bool HasItemParameters { get => m_AnimatorMonitor.HasItemParameters; }
        private int ParameterSlotCount { get => m_AnimatorMonitor.ParameterSlotCount; }
        private int[] ItemSlotID { get => m_AnimatorMonitor.ItemSlotID; }
        private int[] ItemSlotStateIndex { get => m_AnimatorMonitor.ItemSlotStateIndex; }
        private int[] ItemSlotSubstateIndex { get => m_AnimatorMonitor.ItemSlotSubstateIndex; }
        /// <summary>
        /// The object has been destroyed.
        /// </summary>
        public override void OnDestroy()
        {
            base.OnDestroy();
            EventHandler.UnregisterEvent<GameObject>(m_GameObject, "OnCharacterSwitchModels", OnSwitchModels);
        }
        /// <summary>
        /// Initializes the default values.
        /// </summary>        
        private void Awake()
        {
            m_GameObject = gameObject;
            m_MaxBufferSize = MaxBufferSize();
            m_NetworkManager = NetCodeManager.Instance;
            var modelManager = m_GameObject.GetCachedComponent<ModelManager>();
            if (modelManager != null)
                m_AnimatorMonitor = modelManager.ActiveModel.GetCachedComponent<AnimatorMonitor>();
            else
                m_AnimatorMonitor = m_GameObject.GetComponentInChildren<AnimatorMonitor>();
            EventHandler.RegisterEvent<GameObject>(m_GameObject, "OnCharacterSwitchModels", OnSwitchModels);
        }
        /// <summary>
        /// Verify the update mode of the animator.
        /// </summary>
        private void Start()
        {
            if (!IsOwner)
            {
                // Remote players do not move within the FixedUpdate loop.
                var animators = GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < animators.Length; i++)
                    animators[i].updateMode = AnimatorUpdateMode.Normal;
            }
        }
        /// <summary>
        /// The character's model has switched.
        /// </summary>
        /// <param name="activeModel">The active character model.</param>
        private void OnSwitchModels(GameObject activeModel) =>
        m_AnimatorMonitor = activeModel.GetCachedComponent<AnimatorMonitor>();
        /// <summary>
        /// Gets called when message handlers are ready to be unregistered.
        /// </summary>
        public override void OnNetworkDespawn()
        {
            m_CustomMessagingManager?.UnregisterNamedMessageHandler(m_MsgServerPara);
            m_CustomMessagingManager?.UnregisterNamedMessageHandler(m_MsgServerItems);
            m_CustomMessagingManager?.UnregisterNamedMessageHandler(m_MsgNameClient);
            m_CustomMessagingManager?.UnregisterNamedMessageHandler(m_MsgNameServer);
            m_NetworkManager.NetworkSettings.NetworkSyncServerEvent -= OnNetworkSyncServerEvent;
            m_NetworkManager.NetworkSettings.NetworkSyncClientEvent -= OnNetworkSyncClientEvent;
            m_NetworkManager.NetworkSettings.NetworkSyncUpdateEvent -= OnNetworkSyncUpdateEvent;
        }
        /// <summary>
        /// Gets called when message handlers are ready to be registered and the networking is setup.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            m_ServerID = NetworkManager.ServerClientId;
            m_CustomMessagingManager = NetworkManager.Singleton.CustomMessagingManager;
            m_MsgServerPara = $"{NetworkObjectId}MsgServerPara{OwnerClientId}";
            m_MsgServerItems = $"{NetworkObjectId}MsgServerItems{OwnerClientId}";
            m_MsgNameClient = $"{NetworkObjectId}MsgClientAnima{OwnerClientId}";
            m_MsgNameServer = $"{NetworkObjectId}MsgServerAnima{OwnerClientId}";

            if (IsServer)
            {
                m_Clients = NetworkManager.Singleton.ConnectedClientsIds;
                m_NetworkManager.NetworkSettings.NetworkSyncServerEvent += OnNetworkSyncServerEvent;
            }
            else if (IsOwner)
            {
                m_NetworkManager.NetworkSettings.NetworkSyncClientEvent += OnNetworkSyncClientEvent;
            }

            if (!IsOwner)
            {
                m_NetworkManager.NetworkSettings.NetworkSyncUpdateEvent += OnNetworkSyncUpdateEvent;
                if (IsServer)
                {
                    m_CustomMessagingManager?.RegisterNamedMessageHandler(m_MsgNameServer, (sender, reader) =>
                    {
                        SynchronizeParameters(ref reader);
                    });
                    m_CustomMessagingManager?.RegisterNamedMessageHandler(m_MsgServerPara, (sender, reader) =>
                    {
                        InitializeParameters(ref reader);
                    });
                    m_CustomMessagingManager?.RegisterNamedMessageHandler(m_MsgServerItems, (sender, reader) =>
                    {
                        InitializeItemParameters(ref reader);
                    });
                }
                else
                {
                    m_CustomMessagingManager?.RegisterNamedMessageHandler(m_MsgNameClient, (sender, reader) =>
                    {
                        SynchronizeParameters(ref reader);
                    });
                }
            }
            else if (IsLocalPlayer)
            {
                using (m_FastBufferWriter = new FastBufferWriter(1, Allocator.Temp, m_MaxBufferSize))
                {
                    InitializeParameters();
                    m_CustomMessagingManager?.SendNamedMessage(m_MsgServerPara, m_ServerID, m_FastBufferWriter, NetworkDelivery.ReliableSequenced);
                }
                if (HasItemParameters)
                {
                    for (int i = 0; i < ParameterSlotCount; i++)
                        using (m_FastBufferWriter = new FastBufferWriter(1, Allocator.Temp, m_MaxBufferSize))
                        {
                            InitializeItemParameters(i);
                            if (m_FastBufferWriter.Capacity > 0)
                                m_CustomMessagingManager?.SendNamedMessage(m_MsgServerItems, m_ServerID, m_FastBufferWriter, NetworkDelivery.ReliableSequenced);
                        }
                }
            }
        }
        /// <summary>
        /// Returns the maximus size for the fast buffer writer
        /// </summary>
        private int MaxBufferSize()
        {
            return sizeof(bool) * 2 + sizeof(short) * 2 + sizeof(int) * 4 +
                sizeof(float) * 6 + sizeof(int) * (ItemSlotID == null ? 0 : ParameterSlotCount) * 3;
        }
        /// <summary>
        /// Network sync event called from the NetworkInfo component
        /// </summary>
        private void OnNetworkSyncClientEvent()
        {
            // Error handling if this function still executing after despawning event
            if (NetworkManager.Singleton.IsClient)
                using (m_FastBufferWriter = new FastBufferWriter(FastBufferWriter.GetWriteSize(m_Flag), Allocator.Temp, m_MaxBufferSize))
                    if (SynchronizeParameters())
                        m_CustomMessagingManager?.SendNamedMessage(m_MsgNameServer, m_ServerID, m_FastBufferWriter, NetworkDelivery.ReliableSequenced);
        }
        /// <summary>
        /// Network broadcast event called from the NetworkInfo component
        /// </summary>
        private void OnNetworkSyncServerEvent()
        {
            // Error handling if this function still executing after despawning event
            if (NetworkManager.Singleton.IsServer)
                using (m_FastBufferWriter = new FastBufferWriter(FastBufferWriter.GetWriteSize(m_Flag), Allocator.Temp, m_MaxBufferSize))
                    if (SynchronizeParameters())
                        m_CustomMessagingManager?.SendNamedMessage(m_MsgNameClient, m_Clients, m_FastBufferWriter, NetworkDelivery.ReliableSequenced);
        }
        /// <summary>
        /// Snaps the animator to the default values.
        /// </summary>
        private void SnapAnimator() =>
        EventHandler.ExecuteEvent(m_GameObject, "OnCharacterSnapAnimator", true);
        /// <summary>
        /// The animator has snapped into position.
        /// </summary>
        public void AnimatorSnapped() =>
        m_SnappedAbilityIndex = m_AnimatorMonitor.AbilityIndex;
        /// <summary>
        /// Reads/writes the continuous animator parameters.
        /// </summary>
        private void OnNetworkSyncUpdateEvent()
        {
            m_AnimatorMonitor.SetHorizontalMovementParameter(m_NetworkHorizontalMovement, 1);
            m_AnimatorMonitor.SetForwardMovementParameter(m_NetworkForwardMovement, 1);
            m_AnimatorMonitor.SetPitchParameter(m_NetworkPitch, 1);
            m_AnimatorMonitor.SetYawParameter(m_NetworkYaw, 1);
            m_AnimatorMonitor.SetSpeedParameter(m_NetworkSpeed, 1);
            m_AnimatorMonitor.SetAbilityFloatDataParameter(m_NetworkAbilityFloatData, 1);
        }
        /// <summary>
        /// Sets the initial item parameter values.
        /// </summary>
        private void InitializeItemParameters(int idx)
        {
            BytePacker.WriteValuePacked(m_FastBufferWriter, idx);
            BytePacker.WriteValuePacked(m_FastBufferWriter, ItemSlotID[idx]);
            BytePacker.WriteValuePacked(m_FastBufferWriter, ItemSlotStateIndex[idx]);
            BytePacker.WriteValuePacked(m_FastBufferWriter, ItemSlotSubstateIndex[idx]);
        }
        /// <summary>
        /// Gets the initial item parameter values.
        /// </summary>
        private void InitializeItemParameters(ref FastBufferReader reader)
        {
            ByteUnpacker.ReadValuePacked(reader, out int idx);
            ByteUnpacker.ReadValuePacked(reader, out int id);
            ByteUnpacker.ReadValuePacked(reader, out int state);
            ByteUnpacker.ReadValuePacked(reader, out int index);
            m_AnimatorMonitor.SetItemIDParameter(idx, id);
            m_AnimatorMonitor.SetItemStateIndexParameter(idx, state, true);
            m_AnimatorMonitor.SetItemSubstateIndexParameter(idx, index, true);
            SnapAnimator();
        }
        /// <summary>
        /// Sets the initial parameter values.
        /// </summary>
        private void InitializeParameters()
        {
            BytePacker.WriteValuePacked(m_FastBufferWriter, HorizontalMovement);
            BytePacker.WriteValuePacked(m_FastBufferWriter, ForwardMovement);
            BytePacker.WriteValuePacked(m_FastBufferWriter, Pitch);
            BytePacker.WriteValuePacked(m_FastBufferWriter, Yaw);
            BytePacker.WriteValuePacked(m_FastBufferWriter, Speed);
            BytePacker.WriteValuePacked(m_FastBufferWriter, Height);
            BytePacker.WriteValuePacked(m_FastBufferWriter, Moving);
            BytePacker.WriteValuePacked(m_FastBufferWriter, Aiming);
            BytePacker.WriteValuePacked(m_FastBufferWriter, MovementSetID);
            BytePacker.WriteValuePacked(m_FastBufferWriter, AbilityIndex);
            BytePacker.WriteValuePacked(m_FastBufferWriter, AbilityIntData);
            BytePacker.WriteValuePacked(m_FastBufferWriter, AbilityFloatData);
        }
        /// <summary>
        /// Gets the initial parameter values.
        /// </summary>
        private void InitializeParameters(ref FastBufferReader reader)
        {
            ByteUnpacker.ReadValuePacked(reader, out float horizontalMovement);
            ByteUnpacker.ReadValuePacked(reader, out float forwardMovement);
            ByteUnpacker.ReadValuePacked(reader, out float pitch);
            ByteUnpacker.ReadValuePacked(reader, out float yaw);
            ByteUnpacker.ReadValuePacked(reader, out float speed);
            ByteUnpacker.ReadValuePacked(reader, out int height);
            ByteUnpacker.ReadValuePacked(reader, out bool moving);
            ByteUnpacker.ReadValuePacked(reader, out bool aiming);
            ByteUnpacker.ReadValuePacked(reader, out int movementSetID);
            ByteUnpacker.ReadValuePacked(reader, out int abilityIndex);
            ByteUnpacker.ReadValuePacked(reader, out int abilityIntData);
            ByteUnpacker.ReadValuePacked(reader, out float abilityFloatData);
            m_AnimatorMonitor.SetHorizontalMovementParameter(horizontalMovement, 1);
            m_AnimatorMonitor.SetForwardMovementParameter(forwardMovement, 1);
            m_AnimatorMonitor.SetPitchParameter(pitch, 1);
            m_AnimatorMonitor.SetYawParameter(yaw, 1);
            m_AnimatorMonitor.SetSpeedParameter(speed, 1);
            m_AnimatorMonitor.SetHeightParameter(height);
            m_AnimatorMonitor.SetMovingParameter(moving);
            m_AnimatorMonitor.SetAimingParameter(aiming);
            m_AnimatorMonitor.SetMovementSetIDParameter(movementSetID);
            m_AnimatorMonitor.SetAbilityIndexParameter(abilityIndex);
            m_AnimatorMonitor.SetAbilityIntDataParameter(abilityIntData);
            m_AnimatorMonitor.SetAbilityFloatDataParameter(abilityFloatData, 1);
            SnapAnimator();
        }
        /// <summary>
        /// Called several times per second, so that your script can read synchronization data.
        /// </summary>
        /// <param name="stream">The stream that is being read from.</param>
        private void SynchronizeParameters(ref FastBufferReader reader)
        {
            ByteUnpacker.ReadValuePacked(reader, out short flag);
            if ((flag & (short)ParameterDirtyFlags.HorizontalMovement) != 0)
                ByteUnpacker.ReadValuePacked(reader, out m_NetworkHorizontalMovement);
            if ((flag & (short)ParameterDirtyFlags.ForwardMovement) != 0)
                ByteUnpacker.ReadValuePacked(reader, out m_NetworkForwardMovement);
            if ((flag & (short)ParameterDirtyFlags.Pitch) != 0)
                ByteUnpacker.ReadValuePacked(reader, out m_NetworkPitch);
            if ((flag & (short)ParameterDirtyFlags.Yaw) != 0)
                ByteUnpacker.ReadValuePacked(reader, out m_NetworkYaw);
            if ((flag & (short)ParameterDirtyFlags.Speed) != 0)
                ByteUnpacker.ReadValuePacked(reader, out m_NetworkSpeed);
            if ((flag & (short)ParameterDirtyFlags.Height) != 0)
            {
                ByteUnpacker.ReadValuePacked(reader, out int value);
                m_AnimatorMonitor.SetHeightParameter(value);
            }
            if ((flag & (short)ParameterDirtyFlags.Moving) != 0)
            {
                ByteUnpacker.ReadValuePacked(reader, out bool value);
                m_AnimatorMonitor.SetMovingParameter(value);
            }
            if ((flag & (short)ParameterDirtyFlags.Aiming) != 0)
            {
                ByteUnpacker.ReadValuePacked(reader, out bool value);
                m_AnimatorMonitor.SetAimingParameter(value);
            }
            if ((flag & (short)ParameterDirtyFlags.MovementSetID) != 0)
            {
                ByteUnpacker.ReadValuePacked(reader, out int value);
                m_AnimatorMonitor.SetMovementSetIDParameter(value);
            }
            if ((flag & (short)ParameterDirtyFlags.AbilityIndex) != 0)
            {
                ByteUnpacker.ReadValuePacked(reader, out int abilityIndex);
                // When the animator is snapped the ability index will be reset. 
                // It may take some time for that value to propagate across the network.
                // Wait to set the ability index until it is the correct reset value.
                if (m_SnappedAbilityIndex == -1 || abilityIndex == m_SnappedAbilityIndex)
                {
                    m_AnimatorMonitor.SetAbilityIndexParameter(abilityIndex);
                    m_SnappedAbilityIndex = -1;
                }
            }
            if ((flag & (short)ParameterDirtyFlags.AbilityIntData) != 0)
            {
                ByteUnpacker.ReadValuePacked(reader, out int value);
                m_AnimatorMonitor.SetAbilityIntDataParameter(value);
            }
            if ((flag & (short)ParameterDirtyFlags.AbilityFloatData) != 0)
                ByteUnpacker.ReadValuePacked(reader, out m_NetworkAbilityFloatData);
            if (HasItemParameters)
            {
                int id, state, index;
                ByteUnpacker.ReadValuePacked(reader, out byte slot);
                for (int i = 0; i < ParameterSlotCount; i++)
                {
                    if ((slot & (i + 1)) != 0)
                    {
                        ByteUnpacker.ReadValuePacked(reader, out id);
                        m_AnimatorMonitor.SetItemIDParameter(i, id);
                        ByteUnpacker.ReadValuePacked(reader, out state);
                        m_AnimatorMonitor.SetItemStateIndexParameter(i, state, true);
                        ByteUnpacker.ReadValuePacked(reader, out index);
                        m_AnimatorMonitor.SetItemSubstateIndexParameter(i, index, true);
                    }
                }
            }
        }
        /// <summary>
        /// Called several times per second, so that your script can write synchronization data.
        /// </summary>
        /// <param name="stream">The stream that is being written.</param>
        private bool SynchronizeParameters()
        {
            bool results = m_Flag > 0;
            BytePacker.WriteValuePacked(m_FastBufferWriter, m_Flag);
            if ((m_Flag & (short)ParameterDirtyFlags.HorizontalMovement) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, HorizontalMovement);
            if ((m_Flag & (short)ParameterDirtyFlags.ForwardMovement) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, ForwardMovement);
            if ((m_Flag & (short)ParameterDirtyFlags.Pitch) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, Pitch);
            if ((m_Flag & (short)ParameterDirtyFlags.Yaw) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, Yaw);
            if ((m_Flag & (short)ParameterDirtyFlags.Speed) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, Speed);
            if ((m_Flag & (short)ParameterDirtyFlags.Height) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, Height);
            if ((m_Flag & (short)ParameterDirtyFlags.Moving) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, Moving);
            if ((m_Flag & (short)ParameterDirtyFlags.Aiming) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, Aiming);
            if ((m_Flag & (short)ParameterDirtyFlags.MovementSetID) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, MovementSetID);
            if ((m_Flag & (short)ParameterDirtyFlags.AbilityIndex) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, AbilityIndex);
            if ((m_Flag & (short)ParameterDirtyFlags.AbilityIntData) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, AbilityIntData);
            if ((m_Flag & (short)ParameterDirtyFlags.AbilityFloatData) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, AbilityFloatData);
            if (HasItemParameters)
            {
                BytePacker.WriteValuePacked(m_FastBufferWriter, m_ItemDirtySlot);
                for (int i = 0; i < ParameterSlotCount; i++)
                {
                    if ((m_ItemDirtySlot & (i + 1)) != 0)
                    {
                        BytePacker.WriteValuePacked(m_FastBufferWriter, ItemSlotID[i]);
                        BytePacker.WriteValuePacked(m_FastBufferWriter, ItemSlotStateIndex[i]);
                        BytePacker.WriteValuePacked(m_FastBufferWriter, ItemSlotSubstateIndex[i]);
                    }
                }
            }
            m_Flag = 0;
            m_ItemDirtySlot = 0;
            return results;
        }
    }
}