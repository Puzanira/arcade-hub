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
        private AttractOverlay _overlay;

        /// <summary>The attract video screen built by this controller (test seam).</summary>
        public AttractVideoScreen Video => _video;

        /// <summary>The credits ticker + fading title drawn into the reclaimed band (test seam).</summary>
        public AttractOverlay Overlay => _overlay;

        private void Start()
        {
            // Sweep leaked external DDOL objects on EVERY menu entry: after returning from a game
            // (e.g. Factory's GameManager) and on the very first menu load (Factory's ArduinoInputBridge
            // spawns via a global RuntimeInitializeOnLoadMethod in whatever scene starts play — ours too;
            // RuntimeInit runs before Start, so it is already in DDOL by now). External code is untouched;
            // games recreate their singletons on entry (Factory's Boot calls GameManager.Ensure()).
            HubDdolJanitor.CleanForeigners();

            // AudioListener.volume/pause are PROCESS-GLOBAL: a game that mutes itself (Sisyphus has a
            // mute toggle) and is exited through the MenuButton watchdog never gets to restore them —
            // and would leave every following game silent while the attract reel (Direct audio, bypasses
            // the Unity mixer) keeps sounding. The menu is the one place every path funnels through,
            // so it unconditionally hands the next game a live mixer.
            AudioListener.volume = 1f;
            AudioListener.pause = false;

            // The attract reel: full-screen looping video with sound, under the hold-to-launch overlay.
            var videoGO = new GameObject("AttractVideo");
            videoGO.transform.SetParent(transform, false);
            _video = videoGO.AddComponent<AttractVideoScreen>();

            // attract-screen v2: everything above the reel's hands zone is masked out and redrawn by us —
            // our own brighter credits ticker and the title that names whichever game the player's control
            // launches. It reads the active slot straight off the hold-to-launch controller, so the title,
            // the charge bar and the themed rain can never name different games.
            var holdToLaunch = FindAnyObjectByType<HoldToLaunchController>();

            var overlayGO = new GameObject("AttractOverlay");
            overlayGO.transform.SetParent(transform, false);
            _overlay = overlayGO.AddComponent<AttractOverlay>();
            _overlay.Bind(_video, holdToLaunch);

            // The charge bar now lives in that same reclaimed band, under the title, so it has to hang off
            // the reel's letterboxed rect rather than off the hold-to-launch canvas. Wired here because
            // only this component knows when the reel exists; the call is order-independent.
            if (holdToLaunch != null)
                holdToLaunch.AttachChargeBarTo(_video);
        }
    }
}
