using Opsive.Shared.Events;
using Opsive.Shared.Game;
using Opsive.UltimateCharacterController.Character;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Synchronizes the Ultimate Character Controller animator across the network.
/// </summary>
namespace GreedyVox.NetCode.Character
{
    [DisallowMultipleComponent]
    public class NetCodeCharacterAnimatorMonitor : NetworkBehaviour
    {
        // Local
        private GameObject m_GameObject;
        private AnimatorMonitor m_AnimatorMonitor;
        private int m_SnappedAbilityIndex;
        private byte m_ItemDirtySlot;
        // Syncing
        private float m_NetworkHorizontalMovement;
        private float m_NetworkForwardMovement;
        private float m_NetworkPitch;
        private float m_NetworkYaw;
        private float m_NetworkSpeed;
        private float m_NetworkAbilityFloatData;
        // Networking
        private ulong m_ServerID;
        private int m_MaxBufferSize;
        private NetCodeManager m_NetworkManager;
        private FastBufferWriter m_FastBufferWriter;
        private string m_MsgServerPara, m_MsgServerItems;
        private string m_MsgNameClient, m_MsgNameServer;
        private CustomMessagingManager m_CustomMessagingManager;
        // Properties
        private short _DirtyFlag;
        public short DirtyFlag { get => _DirtyFlag; set => _DirtyFlag = value; }
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
            m_MaxBufferSize = MaxBufferSize();
            m_ServerID = NetworkManager.ServerClientId;
            m_CustomMessagingManager = NetworkManager.CustomMessagingManager;
            m_MsgServerPara = $"{OwnerClientId}MsgServerPara{NetworkObjectId}";
            m_MsgServerItems = $"{OwnerClientId}MsgServerItems{NetworkObjectId}";
            m_MsgNameClient = $"{OwnerClientId}MsgClientAnima{NetworkObjectId}";
            m_MsgNameServer = $"{OwnerClientId}MsgServerAnima{NetworkObjectId}";

            if (IsServer)
                m_NetworkManager.NetworkSettings.NetworkSyncServerEvent += OnNetworkSyncServerEvent;
            else if (IsOwner)
                m_NetworkManager.NetworkSettings.NetworkSyncClientEvent += OnNetworkSyncClientEvent;

            if (!IsOwner)
            {
                m_NetworkManager.NetworkSettings.NetworkSyncUpdateEvent += OnNetworkSyncUpdateEvent;
                if (IsServer)
                {
                    m_CustomMessagingManager?.RegisterNamedMessageHandler(m_MsgNameServer, (sender, reader) =>
                    { SynchronizeParameters(ref reader); });
                    m_CustomMessagingManager?.RegisterNamedMessageHandler(m_MsgServerPara, (sender, reader) =>
                    { InitializeParameters(ref reader); });
                    m_CustomMessagingManager?.RegisterNamedMessageHandler(m_MsgServerItems, (sender, reader) =>
                    { InitializeItemParameters(ref reader); });
                }
                else
                {
                    m_CustomMessagingManager?.RegisterNamedMessageHandler(m_MsgNameClient, (sender, reader) =>
                    { SynchronizeParameters(ref reader); });
                }
            }
            else if (IsLocalPlayer)
            {
                using (m_FastBufferWriter = new FastBufferWriter(1, Allocator.Temp, m_MaxBufferSize))
                {
                    InitializeParameters();
                    m_CustomMessagingManager?.SendNamedMessage(m_MsgServerPara, m_ServerID, m_FastBufferWriter, NetworkDelivery.Reliable);
                }
                if (HasItemParameters)
                {
                    for (int i = 0; i < ParameterSlotCount; i++)
                        using (m_FastBufferWriter = new FastBufferWriter(1, Allocator.Temp, m_MaxBufferSize))
                        {
                            InitializeItemParameters(i);
                            if (m_FastBufferWriter.Capacity > 0)
                                m_CustomMessagingManager?.SendNamedMessage(m_MsgServerItems, m_ServerID, m_FastBufferWriter, NetworkDelivery.Reliable);
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
            if (IsClient)
            {
                using (m_FastBufferWriter = new FastBufferWriter(FastBufferWriter.GetWriteSize(_DirtyFlag), Allocator.Temp, m_MaxBufferSize))
                {
                    if (SynchronizeParameters(ref _DirtyFlag))
                        m_CustomMessagingManager?.SendNamedMessage(m_MsgNameServer, m_ServerID, m_FastBufferWriter, NetworkDelivery.Reliable);
                }
            }
        }
        /// <summary>
        /// Network broadcast event called from the NetworkInfo component
        /// </summary>
        private void OnNetworkSyncServerEvent()
        {
            // Error handling if this function still executing after despawning event
            if (IsServer)
            {
                using (m_FastBufferWriter = new FastBufferWriter(FastBufferWriter.GetWriteSize(_DirtyFlag), Allocator.Temp, m_MaxBufferSize))
                {
                    if (SynchronizeParameters(ref _DirtyFlag))
                        m_CustomMessagingManager?.SendNamedMessageToAll(m_MsgNameClient, m_FastBufferWriter, NetworkDelivery.Reliable);
                }
            }
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
        /// <param name="reader">The stream that is being read from.</param>
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
        /// <param name="reader">The stream that is being read from.</param>
        private void SynchronizeParameters(ref FastBufferReader reader)
        {
            ByteUnpacker.ReadValuePacked(reader, out _DirtyFlag);
            if ((_DirtyFlag & (short)ParameterDirtyFlags.HorizontalMovement) != 0)
                ByteUnpacker.ReadValuePacked(reader, out m_NetworkHorizontalMovement);
            if ((_DirtyFlag & (short)ParameterDirtyFlags.ForwardMovement) != 0)
                ByteUnpacker.ReadValuePacked(reader, out m_NetworkForwardMovement);
            if ((_DirtyFlag & (short)ParameterDirtyFlags.Pitch) != 0)
                ByteUnpacker.ReadValuePacked(reader, out m_NetworkPitch);
            if ((_DirtyFlag & (short)ParameterDirtyFlags.Yaw) != 0)
                ByteUnpacker.ReadValuePacked(reader, out m_NetworkYaw);
            if ((_DirtyFlag & (short)ParameterDirtyFlags.Speed) != 0)
                ByteUnpacker.ReadValuePacked(reader, out m_NetworkSpeed);
            if ((_DirtyFlag & (short)ParameterDirtyFlags.Height) != 0)
            {
                ByteUnpacker.ReadValuePacked(reader, out int value);
                m_AnimatorMonitor.SetHeightParameter(value);
            }
            if ((_DirtyFlag & (short)ParameterDirtyFlags.Moving) != 0)
            {
                ByteUnpacker.ReadValuePacked(reader, out bool value);
                m_AnimatorMonitor.SetMovingParameter(value);
            }
            if ((_DirtyFlag & (short)ParameterDirtyFlags.Aiming) != 0)
            {
                ByteUnpacker.ReadValuePacked(reader, out bool value);
                m_AnimatorMonitor.SetAimingParameter(value);
            }
            if ((_DirtyFlag & (short)ParameterDirtyFlags.MovementSetID) != 0)
            {
                ByteUnpacker.ReadValuePacked(reader, out int value);
                m_AnimatorMonitor.SetMovementSetIDParameter(value);
            }
            if ((_DirtyFlag & (short)ParameterDirtyFlags.AbilityIndex) != 0)
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
            if ((_DirtyFlag & (short)ParameterDirtyFlags.AbilityIntData) != 0)
            {
                ByteUnpacker.ReadValuePacked(reader, out int value);
                m_AnimatorMonitor.SetAbilityIntDataParameter(value);
            }
            if ((_DirtyFlag & (short)ParameterDirtyFlags.AbilityFloatData) != 0)
                ByteUnpacker.ReadValuePacked(reader, out m_NetworkAbilityFloatData);
            if (HasItemParameters)
            {
                ByteUnpacker.ReadValuePacked(reader, out byte slot);
                for (int i = 0; i < ParameterSlotCount; i++)
                {
                    if ((slot & (i + 1)) != 0)
                    {
                        ByteUnpacker.ReadValuePacked(reader, out int id);
                        m_AnimatorMonitor.SetItemIDParameter(i, id);
                        ByteUnpacker.ReadValuePacked(reader, out int state);
                        m_AnimatorMonitor.SetItemStateIndexParameter(i, state, true);
                        ByteUnpacker.ReadValuePacked(reader, out int index);
                        m_AnimatorMonitor.SetItemSubstateIndexParameter(i, index, true);
                    }
                }
            }
        }
        /// <summary>
        /// Called several times per second, so that your script can write synchronization data.
        /// </summary>
        /// <param name="stream">The stream that is being written.</param>
        private bool SynchronizeParameters(ref short flag)
        {
            bool results = flag > 0;
            BytePacker.WriteValuePacked(m_FastBufferWriter, flag);
            if ((flag & (short)ParameterDirtyFlags.HorizontalMovement) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, HorizontalMovement);
            if ((flag & (short)ParameterDirtyFlags.ForwardMovement) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, ForwardMovement);
            if ((flag & (short)ParameterDirtyFlags.Pitch) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, Pitch);
            if ((flag & (short)ParameterDirtyFlags.Yaw) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, Yaw);
            if ((flag & (short)ParameterDirtyFlags.Speed) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, Speed);
            if ((flag & (short)ParameterDirtyFlags.Height) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, Height);
            if ((flag & (short)ParameterDirtyFlags.Moving) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, Moving);
            if ((flag & (short)ParameterDirtyFlags.Aiming) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, Aiming);
            if ((flag & (short)ParameterDirtyFlags.MovementSetID) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, MovementSetID);
            if ((flag & (short)ParameterDirtyFlags.AbilityIndex) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, AbilityIndex);
            if ((flag & (short)ParameterDirtyFlags.AbilityIntData) != 0)
                BytePacker.WriteValuePacked(m_FastBufferWriter, AbilityIntData);
            if ((flag & (short)ParameterDirtyFlags.AbilityFloatData) != 0)
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
            flag = 0;
            m_ItemDirtySlot = 0;
            return results;
        }
    }
}