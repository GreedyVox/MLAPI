using System;
using GreedyVox.NetCode.Data;
using Opsive.Shared.Game;
using Opsive.UltimateCharacterController.Items.Actions.Impact;
using Opsive.UltimateCharacterController.Objects;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GreedyVox.NetCode.Objects
{
    public class NetCodeProjectile : Projectile, IPayload
    {
        private PayloadProjectile m_Data;
        private ImpactDamageData m_DamageData;
        /// <summary>
        /// Returns the maximus size for the fast buffer writer
        /// </summary>
        public int MaxBufferSize()
        {
            return FastBufferWriter.GetWriteSize(m_Data.OwnerID) +
                   FastBufferWriter.GetWriteSize(m_Data.ProjectileID) +
                   FastBufferWriter.GetWriteSize(m_Data.Velocity) +
                   FastBufferWriter.GetWriteSize(m_Data.Torque) +
                   FastBufferWriter.GetWriteSize(m_Data.DamageAmount) +
                   FastBufferWriter.GetWriteSize(m_Data.ImpactForce) +
                   FastBufferWriter.GetWriteSize(m_Data.ImpactFrames) +
                   FastBufferWriter.GetWriteSize(m_Data.ImpactLayers) +
                   FastBufferWriter.GetWriteSize(m_Data.ImpactStateDisableTimer) +
                   FastBufferWriter.GetWriteSize(m_Data.ImpactStateName);
        }
        /// <summary>
        /// Returns the initialization data that is required when the object spawns. This allows the remote players to initialize the object correctly.
        /// </summary>
        /// <returns>The initialization data that is required when the object spawns.</returns>
        public void OnNetworkSpawn()
        {
            var net = m_Owner.GetCachedComponent<NetworkObject>();
            m_Data = new PayloadProjectile()
            {
                OwnerID = net == null ? -1L : (long)net.OwnerClientId,
                ProjectileID = m_ID,
                Velocity = m_Velocity,
                Torque = m_Torque,
                DamageAmount = m_ImpactDamageData.DamageAmount,
                ImpactForce = m_ImpactDamageData.ImpactForce,
                ImpactFrames = m_ImpactDamageData.ImpactForceFrames,
                ImpactLayers = m_ImpactLayers.value,
                ImpactStateDisableTimer = m_ImpactDamageData.ImpactStateDisableTimer,
                ImpactStateName = m_ImpactDamageData.ImpactStateName
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
                NetworkLog.LogErrorServer(e.Message);
                return false;
            }
        }
        /// <summary>
        /// The object has been spawned. Initialize the projectile.
        /// </summary>
        public void Unload(ref FastBufferReader reader, GameObject go)
        {
            if (go == null) return;
            reader.ReadValueSafe(out m_Data);
            m_DamageData ??= new ImpactDamageData();
            m_DamageData.DamageAmount = m_Data.DamageAmount;
            m_DamageData.ImpactForce = m_Data.ImpactForce;
            m_DamageData.ImpactForceFrames = m_Data.ImpactFrames;
            m_ImpactLayers = m_Data.ImpactLayers;
            m_DamageData.ImpactStateName = m_Data.ImpactStateName;
            m_DamageData.ImpactStateDisableTimer = m_Data.ImpactStateDisableTimer;
            Initialize(m_Data.ProjectileID, m_Data.Velocity, m_Data.Torque, go, m_DamageData);
        }
    }
}