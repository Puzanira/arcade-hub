using UnityEditor;
using UnityEditor.SceneManagement;

namespace AiGameStudio.ArcadeHub.Editor
{
    /// <summary>
    /// Play always starts the CABINET, whatever scene happens to be open.
    ///
    /// Why this exists: the editor keeps reopening this project on Unity's default
    /// `SampleScene` instead of `HubMenu`, and Play on that scene shows an empty
    /// grey frame — nothing launches, nothing is wrong, there is simply nothing
    /// there. That cost the founder three separate "ничего не запускается" rounds
    /// on 2026-09-22 before anyone noticed which scene was loaded.
    ///
    /// `playModeStartScene` is Unity's own answer: it overrides which scene Play
    /// enters, leaving the open scene untouched in the editor. So the cabinet can
    /// be played from anywhere, and a session editing a game scene can still Play
    /// into the launcher to see it in context.
    ///
    /// To work on a scene in isolation instead, clear it: Tools → Arcade Hub →
    /// Play starts at the cabinet (toggle).
    /// </summary>
    [InitializeOnLoad]
    internal static class AlwaysStartAtHubMenu
    {
        private const string ScenePath = "Assets/_Project/Scenes/HubMenu.unity";
        private const string MenuPath = "Tools/Arcade Hub/Play starts at the cabinet";
        private const string PrefKey = "AiGameStudio.ArcadeHub.PlayStartsAtCabinet";

        static AlwaysStartAtHubMenu()
        {
            // Deferred: on domain reload the asset database may not be ready yet, and
            // LoadAssetAtPath would hand back null and silently disable the override.
            EditorApplication.delayCall += Apply;
        }

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        private static void Apply()
        {
            if (!Enabled)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            // Missing scene is not an error worth shouting about — a session may be
            // working in a stripped checkout. Leave Play alone instead.
            if (scene != null)
                EditorSceneManager.playModeStartScene = scene;
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            Apply();
        }

        [MenuItem(MenuPath, isValidateFunction: true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
    }
}
