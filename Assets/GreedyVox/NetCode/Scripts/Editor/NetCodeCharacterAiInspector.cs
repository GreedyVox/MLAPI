// using GreedyVox.NetCode;
// using GreedyVox.NetCode.Ai;
// using GreedyVox.NetCode.Utilities;
// using Opsive.UltimateCharacterController.Character;
// using Opsive.UltimateCharacterController.Traits;
// using Unity.Netcode;
// using UnityEditor;
// using UnityEngine;

// public class NetCodeCharacterAiInspector : EditorWindow {
//     private Object m_NetworkCharacter;
//     [MenuItem ("Tools/GreedyVox/NetCode/Character Ai Inspector")]
//     private static NetCodeCharacterAiInspector Init () {
//         return EditorWindow.GetWindowWithRect<NetCodeCharacterAiInspector> (
//             new Rect (Screen.width - 300 / 2, Screen.height - 200 / 2, 300, 200), true, "Network Character Ai");
//     }
//     private void OnGUI () {
//         EditorGUILayout.BeginHorizontal ();
//         m_NetworkCharacter = EditorGUILayout.ObjectField (m_NetworkCharacter, typeof (Object), true);
//         EditorGUILayout.EndHorizontal ();
//         if (GUILayout.Button ("Update Character")) {
//             if (m_NetworkCharacter == null) {
//                 ShowNotification (new GUIContent ("No object selected for updating"), 9);
//             } else {
//                 SetupCharacter ((GameObject) m_NetworkCharacter);
//                 ShowNotification (new GUIContent ("Finished updating character"), 9);
//             }
//         }
//     }
//     /// <summary>
//     /// Sets up the character to be able to work with networking.
//     /// </summary>
//     private void SetupCharacter (GameObject obj) {
//         // Remove the single player variants of the necessary components.
//         ComponentUtility.TryRemoveComponent<AnimatorMonitor> (obj);
//         if (ComponentUtility.TryAddComponent<NetworkObject> (obj, out var net)) {
//             net.AutoObjectParentSync = false;
//         }
//         ComponentUtility.TryAddComponent<NetCodeInfo> (obj);
//         ComponentUtility.TryAddComponent<NetCodeEvent> (obj);
//         ComponentUtility.TryAddComponent<NetCodeCharacter> (obj);
//         // Certain components may be necessary if their single player components is added to the character.
//         if (ComponentUtility.HasComponent<AttributeManager> (obj)) {
//             ComponentUtility.TryAddComponent<NetCodeAttributeMonitor> (obj);
//         }
//         if (ComponentUtility.HasComponent<Health> (obj)) {
//             ComponentUtility.TryAddComponent<NetCodeHealthMonitor> (obj);
//         }
//         if (ComponentUtility.HasComponent<Respawner> (obj)) {
//             ComponentUtility.TryAddComponent<NetCodeRespawnerMonitor> (obj);
//         }
//         ComponentUtility.TryAddComponent<NetCodeAiBD> (obj);
//         ComponentUtility.TryAddComponent<NetCodeCharacterTransformAiMonitor> (obj);
//         ComponentUtility.TryAddComponent<NetCodeLookSourceAi> (obj);
//         ComponentUtility.TryAddComponent<NetCodeAnimatorAiMonitor> (obj);
//         ComponentUtility.TryAddComponent<NetCodeSyncRate> (obj);
//     }
// }