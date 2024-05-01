using GreedyVox.NetCode.Objects;
using GreedyVox.NetCode.Traits;
using GreedyVox.NetCode.Utilities;
using Opsive.UltimateCharacterController.Objects;
using Opsive.UltimateCharacterController.Traits;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace GreedyVox.NetCode.Editors
{
    public class NetCodeItemGrenadeInspector : EditorWindow
    {
        [MenuItem("Tools/GreedyVox/NetCode/Items/Grenade Inspector")]
        private static NetCodeItemGrenadeInspector Init() =>
        EditorWindow.GetWindowWithRect<NetCodeItemGrenadeInspector>(
        new Rect(Screen.width - 300 / 2, Screen.height - 100 / 2, 300, 100), true, "Network Grenade");
        private Object m_NetworkItem;
        private const string IconErrorPath = "d_console.erroricon.sml";
        private const string IconIfoPath = "d_console.infoicon.sml";
        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            m_NetworkItem = EditorGUILayout.ObjectField(m_NetworkItem, typeof(Object), true);
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Update Grenade"))
            {
                if (m_NetworkItem == null)
                {
                    ShowNotification(new GUIContent("No object selected for updating",
                                         EditorGUIUtility.IconContent(IconErrorPath).image), 15);
                }
                else
                {
                    SetupItem((GameObject)m_NetworkItem);
                    ShowNotification(new GUIContent("Finished updating grenade item",
                                         EditorGUIUtility.IconContent(IconIfoPath).image), 15);
                }
            }
        }
        /// <summary>
        /// Sets up the grenade to be able to work with networking.
        /// </summary>
        private void SetupItem(GameObject go)
        {
            if (go == null) return;
            // Remove the single player variants of the necessary components.
            if (ComponentUtility.TryAddComponent<NetworkObject>(go, out var net))
            {
                net.SpawnWithObservers = true;
                net.AlwaysReplicateAsRoot = false;
                net.SynchronizeTransform = false;
                net.ActiveSceneSynchronization = false;
                net.SceneMigrationSynchronization = false;
                net.DontDestroyWithOwner = false;
                net.AutoObjectParentSync = false;
            }
            ComponentUtility.TryAddComponent<NetworkRigidbody>(go);
            ComponentUtility.TryAddComponent<NetworkTransform>(go);
            ComponentUtility.TryAddComponent<NetCodeInfo>(go);
            ComponentUtility.TryAddComponent<NetCodeDestructibleMonitor>(go);
            if (!ComponentUtility.TryReplaceCopy<Grenade, NetCodeGrenade>(go))
                ShowNotification(new GUIContent($"Error while replacing the component {typeof(Grenade)} with {typeof(NetCodeGrenade)}",
                                     EditorGUIUtility.IconContent(IconErrorPath).image), 15);
            if (ComponentUtility.HasComponent<AttributeManager>(go))
                ComponentUtility.TryAddComponent<NetCodeAttributeMonitor>(go);
            if (ComponentUtility.HasComponent<Health>(go))
                ComponentUtility.TryAddComponent<NetCodeHealthMonitor>(go);
        }
    }
}