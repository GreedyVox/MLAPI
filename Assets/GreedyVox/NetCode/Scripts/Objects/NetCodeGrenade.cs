using System;
using GreedyVox.NetCode.Data;
using Opsive.Shared.Game;
using Opsive.UltimateCharacterController.Items.Actions.Impact;
using Opsive.UltimateCharacterController.Objects;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Initializes the grenade over the network.
/// </summary>
namespace GreedyVox.NetCode.Objects
{
    [DisallowMultipleComponent]
    public class NetCodeGrenade : Grenade, IPayload
    {
        private PayloadGrenado m_Data;
        private ImpactDamageData m_DamageData;
        /// <summary>
        /// Returns the maximus size for the fast buffer writer
        /// </summary>
        public int MaxBufferSize()
        {
            return FastBufferWriter.GetWriteSize(m_Data.ImpactStateName) +
                FastBufferWriter.GetWriteSize(m_Data.Velocity) +
                FastBufferWriter.GetWriteSize(m_Data.Torque) +
                FastBufferWriter.GetWriteSize(m_Data.ImpactFrames) +
                FastBufferWriter.GetWriteSize(m_Data.ImpactLayers) +
                FastBufferWriter.GetWriteSize(m_Data.ImpactForce) +
                FastBufferWriter.GetWriteSize(m_Data.DamageAmount) +
                FastBufferWriter.GetWriteSize(m_Data.ImpactStateDisableTimer) +
                FastBufferWriter.GetWriteSize(m_Data.ScheduledDeactivation);
        }
        /// <summary>
        /// Initialize the default data values.
        /// </summary>
        public void OnNetworkSpawn()
        {
            base.OnEnable();
            m_Data = new PayloadGrenado()
            {
                OwnerID = m_ID,
                ImpactStateName = m_ImpactDamageData.ImpactStateName,
                Velocity = m_Velocity,
                Torque = m_Torque,
                ImpactFrames = m_ImpactDamageData.ImpactForceFrames,
                ImpactLayers = m_ImpactLayers.value,
                ImpactForce = m_ImpactDamageData.ImpactForce,
                DamageAmount = m_ImpactDamageData.DamageAmount,
                ImpactStateDisableTimer = m_ImpactDamageData.ImpactStateDisableTimer,
                ScheduledDeactivation = m_ScheduledDeactivation != null ?
                (m_ScheduledDeactivation.EndTime - Time.time) : -1,
                NetCodeObject = m_Owner.GetCachedComponent<NetworkObject>()
            };
        }
        /// <summary>
        /// The object has been spawned, write the payload data.
        /// </summary>
        public bool Load(out FastBufferWriter writer)
        {
            try
            {
                using (writer = new FastBufferWriter(MaxBufferSize(), Allocator.Temp))
                    writer.WriteValueSafe(m_Data);
                return true;
            }
            catch (Exception e)
            {
                NetworkLog.LogErrorServer($"{e.Message} [Length={writer.Length}/{writer.MaxCapacity}]");
                return false;
            }
        }
        /// <summary>
        /// The object has been spawned, read the payload data.
        /// </summary>
        public void Unload(ref FastBufferReader reader, GameObject go)
        {
            reader.ReadValueSafe(out m_Data);
            GameObject owner = null;
            if (m_Data.NetCodeObject.TryGet(out var net))
                owner = net.gameObject;
            m_DamageData ??= new ImpactDamageData();
            m_DamageData.DamageAmount = m_Data.DamageAmount;
            m_DamageData.ImpactForce = m_Data.ImpactForce;
            m_DamageData.ImpactForceFrames = m_Data.ImpactFrames;
            m_ImpactLayers = m_Data.ImpactLayers;
            m_DamageData.ImpactStateName = m_Data.ImpactStateName;
            m_DamageData.ImpactStateDisableTimer = m_Data.ImpactStateDisableTimer;
            Initialize(m_Data.OwnerID, m_Data.Velocity, m_Data.Torque, owner, m_DamageData);
            // The grenade should start cooking.
            var deactivationTime = m_Data.ScheduledDeactivation;
            if (deactivationTime > 0)
                m_ScheduledDeactivation = Scheduler.Schedule(deactivationTime, Deactivate);
        }
    }
}