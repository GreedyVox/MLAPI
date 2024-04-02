using Opsive.Shared.Networking;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Contains information about the object on the network.
/// </summary>
namespace GreedyVox.NetCode
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class NetCodeInfo : NetworkBehaviour, INetworkInfo
    {
        /// <summary>
        /// Is the networking implementation server or host? Cheat code for moving platforms
        /// </summary>
        /// <returns>True if the network transform is server or host.</returns>        
        public bool IsServerHost()
        {
            return IsServer || IsHost;
        }
        /// <summary>
        /// Is the networking implementation server authoritative?
        /// </summary>
        /// <returns>True if the network transform is server authoritative.</returns>        
        public bool IsServerAuthoritative()
        {
            return IsServer && !IsClient;
        }
        /// <summary>
        /// Is the game instance on the server?
        /// </summary>
        /// <returns>True if the game instance is on the server.</returns>
        bool INetworkInfo.IsServer()
        {
            return IsServer;
        }
        /// <summary>
        /// Is the game instance on the server?
        /// </summary>
        /// <returns>True if the game instance is on the client.</returns>
        public bool IsPlayer()
        {
            return IsClient;
        }
        /// <summary>
        /// Is the character the local player?
        /// </summary>
        /// <returns>True if the character is the local player.</returns>
        bool INetworkInfo.IsLocalPlayer()
        {
            return IsOwner;
        }
        /// <summary>
        /// Does the network instance have authority?
        /// </summary>
        /// <returns>True if the instance has authority.</returns>
        public bool HasAuthority()
        {
            return IsOwner;
        }
    }
}