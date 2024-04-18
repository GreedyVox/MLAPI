#if ULTIMATE_CHARACTER_CONTROLLER_MULTIPLAYER_BD_AI
using BehaviorDesigner.Runtime;
#endif
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ai Behavior Designer for running on server only.
/// </summary>
namespace GreedyVox.NetCode.Ai
{
    [DisallowMultipleComponent]
    public class NetCodeAiBD : NetworkBehaviour
    {
#if ULTIMATE_CHARACTER_CONTROLLER_MULTIPLAYER_BD_AI
        private BehaviorTree m_BehaviorTree;
        private void Awake() => m_BehaviorTree = GetComponent<BehaviorTree>();
        public override void OnNetworkSpawn()
        { if (m_BehaviorTree != null) m_BehaviorTree.enabled = IsServer; }
#endif
    }
}