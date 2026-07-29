using UnityEngine;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The HubMenu scene's controller — now the ATTRACT SCREEN host (founder's decision 2026-07-29:
    /// the start screen is a looping video with sound, «поверх — прогресс-бар и падающие объекты»).
    /// The old game list and joystick navigation are REMOVED COMPLETELY: no rows, no cursor, no text.
    /// What the player sees is the <see cref="AttractVideoScreen"/> full-screen video with the
    /// <see cref="HoldToLaunchController"/> stack (charge bar + themed sprite rain + the single approved
    /// "СКОРО" overlay) rendering above it; the themed rain itself is the hint for which game a control
    /// charges. Launching stays exclusively hold-to-launch — every control only charges its own slot.
    ///
    /// This component also keeps the scene's housekeeping duties: the DDOL janitor sweep on every menu
    /// entry (external games leak DontDestroyOnLoad singletons — see <see cref="HubDdolJanitor"/>).
    /// </summary>
    [AddComponentMenu("Arcade Hub/Hub Menu Controller")]
    [DisallowMultipleComponent]
    public sealed class HubMenuController : MonoBehaviour
    {
        private AttractVideoScreen _video;

        /// <summary>The attract video screen built by this controller (test seam).</summary>
        public AttractVideoScreen Video => _video;

        private void Start()
        {
            // Sweep leaked external DDOL objects on EVERY menu entry: after returning from a game
            // (e.g. Factory's GameManager) and on the very first menu load (Factory's ArduinoInputBridge
            // spawns via a global RuntimeInitializeOnLoadMethod in whatever scene starts play — ours too;
            // RuntimeInit runs before Start, so it is already in DDOL by now). External code is untouched;
            // games recreate their singletons on entry (Factory's Boot calls GameManager.Ensure()).
            HubDdolJanitor.CleanForeigners();

            // The attract reel: full-screen looping video with sound, under the hold-to-launch overlay.
            var videoGO = new GameObject("AttractVideo");
            videoGO.transform.SetParent(transform, false);
            _video = videoGO.AddComponent<AttractVideoScreen>();
        }
    }
}
