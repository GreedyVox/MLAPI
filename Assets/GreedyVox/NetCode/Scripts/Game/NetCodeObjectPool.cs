using System.Collections.Generic;
using GreedyVox.NetCode.Interfaces;
using GreedyVox.NetCode.Utilities;
using Opsive.Shared.Game;
using Opsive.UltimateCharacterController.Networking.Game;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Provides a way to synchronize pooled objects over the network.
/// </summary>
namespace GreedyVox.NetCode.Game
{
    [DisallowMultipleComponent]
    public class NetCodeObjectPool : NetworkObjectPool
    {
        private HashSet<GameObject> m_SpawnableGameObjects = new();
        private HashSet<GameObject> m_SpawnedGameObjects = new();
        private HashSet<GameObject> m_ActiveGameObjects = new();
        private NetworkObject m_NetworkObject;
        /// Initialize the default values.
        /// </summary>
        private void Start()
        {
            var pool = FindObjectOfType<ObjectPool>()?.PreloadedPrefabs;
            for (int n = 0; n < pool?.Length; n++)
                SetupSpawnManager(pool[n].Prefab);
        }
        private void SetupSpawnManager(GameObject go, bool pool = true)
        {
            if (ComponentUtility.HasComponent<NetworkObject>(go))
            {
                m_SpawnableGameObjects.Add(go);
                NetworkManager.Singleton.PrefabHandler.AddHandler(go,
                    new NetCodeSpawnManager(go, transform, pool));
            }
        }
        /// <summary>
        /// Internal method which spawns the object over the network. This does not instantiate a new object on the local client.
        /// </summary>
        /// <param name="original">The object that the object was instantiated from.</param>
        /// <param name="instanceObject">The object that was instantiated from the original object.</param>
        /// <param name="sceneObject">Is the object owned by the scene? If fales it will be owned by the character.</param>
        protected override void NetworkSpawnInternal(GameObject original, GameObject instanceObject, bool sceneObject)
        {
            if (m_SpawnableGameObjects.Contains(original))
            {
                if (!m_SpawnedGameObjects.Contains(instanceObject))
                    m_SpawnedGameObjects.Add(instanceObject);
                if (!m_ActiveGameObjects.Contains(instanceObject))
                    m_ActiveGameObjects.Add(instanceObject);
                if (NetworkManager.Singleton.IsServer)
                    instanceObject.GetCachedComponent<NetworkObject>()?.Spawn(sceneObject);
                else if (ComponentUtility.TryGet<IPayload>(instanceObject, out var dat))
                    NetCodeMessenger.Instance.ClientSpawnObject(original, dat);
                return;
            }
            Debug.LogError($"Error: Unable to spawn {original.name} on the network. Ensure the object has been added to the NetworkObjectPool.");
        }
        /// <summary>
        /// Internal method which destroys the object instance on the network.
        /// </summary>
        /// <param name="obj">The object to destroy.</param>
        protected override void DestroyInternal(GameObject obj)
        {
            if (ObjectPool.InstantiatedWithPool(obj))
                DestroyInternalExtended(obj);
            else
                GameObject.Destroy(obj);
        }
        /// <summary>
        /// Destroys the object.
        /// </summary>
        /// <param name="obj">The object that should be destroyed.</param>
        protected virtual void DestroyInternalExtended(GameObject obj)
        {
            if ((m_NetworkObject = obj.GetComponent<NetworkObject>()) != null && m_NetworkObject.IsSpawned)
            {
                if (NetworkManager.Singleton.IsServer)
                    m_NetworkObject.Despawn();
                else if (NetworkManager.Singleton.IsClient)
                    NetCodeMessenger.Instance.ClientDespawnObject(m_NetworkObject.NetworkObjectId);
            }
            else { ObjectPool.Destroy(obj); }
            if (m_NetworkObject == null)
                m_ActiveGameObjects.Remove(obj);
        }
        /// Internal method which returns if the specified object was spawned with the network object pool.
        /// </summary>
        /// <param name="obj">The object instance to determine if was spawned with the object pool.</param>
        /// <returns>True if the object was spawned with the network object pool.</returns>
        protected override bool SpawnedWithPoolInternal(GameObject obj) => m_SpawnedGameObjects.Contains(obj);
    }
}