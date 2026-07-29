using UnityEngine;
using UnityEngine.SceneManagement;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The launcher's DontDestroyOnLoad janitor. External games are code we do NOT touch, and some of
    /// them park persistent singletons in the DDOL scene by their own design — Factory's GameManager
    /// (Boot → Ensure) and its ArduinoInputBridge (a global RuntimeInitializeOnLoadMethod that spawns in
    /// whatever scene starts play, INCLUDING the hub menu). Left alone they leak across the launcher and
    /// into every other game on the cabinet.
    ///
    /// On every hub-menu entry (<see cref="HubMenuController"/>.Start) this sweeps the DDOL scene and
    /// destroys FOREIGN root objects — anything carrying a MonoBehaviour outside the whitelisted
    /// namespaces (the hub/controls own "AiGameStudio.*", Unity's own machinery incl. the PlayMode test
    /// runner). Games recreate what they need on entry (Factory's Boot calls GameManager.Ensure()), so
    /// sweeping between runs is safe; the games' own files are never modified.
    /// </summary>
    public static class HubDdolJanitor
    {
        // Namespace prefixes that may live in the DDOL scene. Everything else is a leaked game object.
        private static readonly string[] WhitelistPrefixes =
        {
            "AiGameStudio", // the hub itself (LauncherReturn + its runner) and arcade-controls
            "Unity",        // UnityEngine.*, UnityEditor.*, Unity.* — incl. the PlayMode test runner
            "TMPro",
        };

        /// <summary>
        /// Destroy every foreign root object in the DontDestroyOnLoad scene. Returns how many roots were
        /// swept (test/inspection seam). Play mode only — a no-op in the editor outside play.
        /// </summary>
        public static int CleanForeigners()
        {
            if (!Application.isPlaying) return 0;

            int removed = 0;
            var probe = new GameObject("~HubDdolJanitorProbe");
            Object.DontDestroyOnLoad(probe);
            Scene ddol = probe.scene;

            foreach (GameObject root in ddol.GetRootGameObjects())
            {
                if (root == probe) continue;
                if (!IsForeign(root)) continue;
                Debug.Log($"[Hub] DDOL janitor: sweeping leaked external object '{root.name}'.");
                Object.Destroy(root);
                removed++;
            }

            Object.Destroy(probe);
            return removed;
        }

        // A root is foreign when ANY behaviour on it (children included) lives outside the whitelist.
        // Global-namespace scripts are foreign too: all hub/controls code is namespaced, so an
        // un-namespaced behaviour in DDOL can only be a leaked game script.
        private static bool IsForeign(GameObject root)
        {
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue; // missing script: unidentifiable, leave the object alone
                string ns = mb.GetType().Namespace;
                if (ns == null) return true;
                bool whitelisted = false;
                foreach (string prefix in WhitelistPrefixes)
                {
                    if (ns == prefix || ns.StartsWith(prefix + ".") || ns.StartsWith(prefix))
                    {
                        whitelisted = true;
                        break;
                    }
                }
                if (!whitelisted) return true;
            }
            return false;
        }
    }
}
