using UnityEngine;
using UnityEngine.SceneManagement;
using AiGameStudio.ArcadeControls;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The launcher's return-to-menu watchdog for EXTERNAL games. A packaged game (Home Alone,
    /// Life Choices) ends its run cleanly on <see cref="ArcadeInput.MenuButton"/> — freezing itself,
    /// holding NO DontDestroyOnLoad/static state — but it does NOT know the hub's menu scene, so on its
    /// own it only resets in place (its standalone contract). When the hub launches such a game it arms
    /// this watchdog (a launcher-owned, <see cref="Object.DontDestroyOnLoad"/> object) which watches the
    /// SAME MenuButton and loads <see cref="HubScenes.HubMenu"/> on the exit gesture, handing the cabinet
    /// back to the launcher.
    ///
    /// Exit gesture = the universal cabinet rule (matches Home Alone's own exit): a MenuButton still held
    /// while this game LOADS (the player's finger from exiting the previous game) must NOT exit — a
    /// release must be SEEN first, then a fresh press fires. Input is read ONLY through <see cref="ArcadeInput"/>.
    ///
    /// The built-in <see cref="TestGameController"/> returns to the menu itself, so the launcher does NOT
    /// arm this for the TestGame slot (no double return, and hub-v0's PlayMode loop is untouched).
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class LauncherReturn : MonoBehaviour
    {
        private static LauncherReturn _instance;

        private string _returnScene;
        private int _spawnFrame;
        private bool _releaseSeen;
        private bool _fired;

        /// <summary>The live watchdog, if one is armed (test/inspection seam).</summary>
        public static LauncherReturn Instance => _instance;

        /// <summary>
        /// Arm a fresh watchdog that returns to <paramref name="returnScene"/> on the MenuButton exit
        /// gesture. Replaces any existing instance so every launch gets a clean release-guard. Call this
        /// immediately BEFORE loading the game scene.
        /// </summary>
        public static void ArmFor(string returnScene)
        {
            if (_instance != null)
                Destroy(_instance.gameObject);

            var go = new GameObject("~LauncherReturn");
            DontDestroyOnLoad(go);
            var lr = go.AddComponent<LauncherReturn>();
            lr._returnScene = returnScene;
            lr._spawnFrame = Time.frameCount;
            _instance = lr;
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // Self-clean: once the menu is up (by our own return or any other path), the watchdog's job is
        // done — never linger into the launcher scene.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_fired && scene.name == _returnScene)
                Destroy(gameObject);
        }

        private void Update()
        {
            if (_fired) return;

            // Warmup: the spawn frame itself may run before the game scene's ArcadeInput has been pumped;
            // start reading the button on the next frame so a held-through-load press reads as held.
            if (Time.frameCount <= _spawnFrame) return;

            bool held = ArcadeInput.MenuButton.IsHeld;

            if (!_releaseSeen)
            {
                if (!held) _releaseSeen = true;
                return;
            }

            // Fresh press after a seen release → hand control back to the launcher.
            if (held)
            {
                _fired = true;
                SceneManager.LoadScene(_returnScene);
                Destroy(gameObject);
            }
        }
    }
}
