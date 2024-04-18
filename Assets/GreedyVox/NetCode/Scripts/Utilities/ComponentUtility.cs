using UnityEngine;

namespace GreedyVox.NetCode.Utilities
{
    public sealed class ComponentUtility
    {
        public static bool HasComponent<T>(GameObject go)
        where T : Component
        {
            return go?.GetComponent<T>() != null;
        }
        public static void CopyValues<T>(T from, T to)
        where T : Component
        {
            var json = JsonUtility.ToJson(from);
            JsonUtility.FromJsonOverwrite(json, to);
        }
        public static void CopyValues<F, T>(F from, T to)
        where F : Component where T : Component
        {
            var json = JsonUtility.ToJson(from);
            JsonUtility.FromJsonOverwrite(json, to);
        }
        public static void RemoveCopyValues<T>(T from, T to)
        where T : Component
        {
            var json = JsonUtility.ToJson(from);
            JsonUtility.FromJsonOverwrite(json, to);
            GameObject.DestroyImmediate(from, true);
        }
        public static void RemoveCopyValues<F, T>(F from, T to)
        where F : Component where T : Component
        {
            var json = JsonUtility.ToJson(from);
            JsonUtility.FromJsonOverwrite(json, to);
            GameObject.DestroyImmediate(from, true);
        }
        public static bool TryAddComponent<T>(GameObject go)
        where T : Component
        {
            var component = go?.GetComponent<T>();
            if (component == null)
            {
                go?.AddComponent<T>();
                return true;
            }
            return false;
        }
        public static bool TryAddComponent<T>(GameObject go, out T com)
        where T : Component
        {
            com = go?.GetComponent<T>();
            if (com == null)
            {
                com = go?.AddComponent<T>();
                return true;
            }
            return false;
        }
        public static T TryAddGetComponent<T>(GameObject go)
        where T : Component
        {
            var component = go?.GetComponent<T>();
            if (component == null)
            {
                component = go?.AddComponent<T>();
            }
            return component;
        }
        public static bool TryAddGetComponent<T>(GameObject go, out T com)
        where T : Component
        {
            com = go?.GetComponent<T>();
            if (com == null)
            {
                com = go?.AddComponent<T>();
            }
            return com != null;
        }
        public static bool TryRemoveComponent<T>(GameObject go)
        where T : Component
        {
            var component = go?.GetComponent<T>();
            if (component != null)
            {
                GameObject.DestroyImmediate(component, true);
                return true;
            }
            return false;
        }
        public static bool TryRemoveComponentInChildren<T>(GameObject go)
        where T : Component
        {
            var component = go?.GetComponentInChildren<T>();
            if (component != null)
            {
                GameObject.DestroyImmediate(component, true);
                return true;
            }
            return false;
        }
        public static bool TryGetComponentInChildren<T>(GameObject go, out T com)
        where T : Component
        {
            com = default;
            if (go != null)
                com = go.GetComponentInChildren<T>();
            return com != null;
        }
        public static bool TryGetComponentsInChildren<T>(GameObject go, out T[] com)
        where T : Component
        {
            com = default;
            if (go != null)
                com = go.GetComponentsInChildren<T>();
            return com != null;
        }
        public static bool TryReplaceComponent<T, V>(GameObject go)
        where T : Component where V : Component
        {
            if (TryRemoveComponent<T>(go) && TryAddComponent<V>(go))
                return true;
            return false;
        }
        public static bool TryReplaceComponentInChildren<T, V>(GameObject go)
        where T : Component where V : Component
        {
            if (TryGetComponentInChildren<T>(go, out var com)
            && TryReplaceComponent<T, V>(com.gameObject))
                return true;
            return false;
        }
        public static bool TryReplaceComponentsInChildren<T, V>(GameObject go)
        where T : Component where V : Component
        {
            if (TryGetComponentsInChildren<T>(go, out var com))
            {
                for (var i = 0; i < com.Length; i++)
                    if (!TryReplaceComponent<T, V>(com[i].gameObject))
                        return false;
                return true;
            }
            return false;
        }
    }
}