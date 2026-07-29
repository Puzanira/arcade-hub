using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The launcher's attract screen (founder's decision 2026-07-29): the HubMenu shows a full-screen
    /// LOOPING video with sound — the cabinet's attract reel inviting players to poke the controls — and
    /// NOTHING else. No game list, no cursor, no text; the hold-to-launch stack (charge bar + themed
    /// sprite rain, plus the single approved "СКОРО" overlay) renders ON TOP of the video from its own
    /// higher-sorted canvas.
    ///
    /// The clip lives in StreamingAssets (like launcher-config.json — swappable on the cabinet without a
    /// rebuild). It is decoded into a RenderTexture shown by a RawImage on a bottom-sorted canvas,
    /// letterboxed to the video's own aspect (FitInside math; the shipped 1280×720 clip fills the 16:9
    /// reference exactly). Audio plays through the default output (direct mode) — the attract sound.
    /// </summary>
    [AddComponentMenu("Arcade Hub/Attract Video Screen")]
    [DisallowMultipleComponent]
    public sealed class AttractVideoScreen : MonoBehaviour
    {
        /// <summary>The attract clip's file name inside StreamingAssets.</summary>
        public const string FileName = "Explainer.mp4";

        private const float RefWidth = 1920f;
        private const float RefHeight = 1080f;

        private VideoPlayer _player;
        private RawImage _screen;
        private RenderTexture _target;
        private float _pausedSince = -1f;
        private float _stepAccum;
        private float _retryAccum;

        /// <summary>The video player (test seam: isPlaying / isLooping / frame).</summary>
        public VideoPlayer Player => _player;

        /// <summary>The full-screen RawImage the video is drawn into (test seam: on-screen rect).</summary>
        public RawImage Screen => _screen;

        /// <summary>True once the clip is prepared and actively playing.</summary>
        public bool IsPlaying => _player != null && _player.isPlaying;

        /// <summary>
        /// True when the watchdog fell back to manual frame stepping. macOS quirk: an UNFOCUSED editor
        /// (tests driven from a terminal, batch runs) prepares the clip fine but AVPlayer's playback clock
        /// refuses to start — <c>Play()</c> leaves the player paused, silently. Decoding still works
        /// frame-by-frame, so after ~2 s of refusal we drive <see cref="VideoPlayer.StepForward"/> off the
        /// real-time clock (no audio in this mode). The fallback is gated to that exact case
        /// (<see cref="Application.isEditor"/> / <see cref="Application.isBatchMode"/> / not
        /// <see cref="Application.isFocused"/>): a FOCUSED standalone cabinet can NEVER enter it, so a
        /// merely slow decoder there just waits for the clock instead of flipping into silent stepping.
        /// And the fallback is not a dead end — while stepping we keep retrying <c>Play()</c>, so the
        /// moment the clock does start (e.g. an unfocused editor regains focus) we drop back to normal
        /// clocked playback WITH sound.
        /// </summary>
        public bool FallbackStepping { get; private set; }

        /// <summary>The clip is prepared and frames are being presented (clock playback OR step fallback).</summary>
        public bool IsShowingFrames => _player != null && _player.isPrepared
            && (_player.isPlaying || FallbackStepping);

        private void Start()
        {
            BuildView();
            BuildPlayer();
        }

        private void Update()
        {
            if (_player == null || !_player.isPrepared) return;

            if (_player.isPlaying)
            {
                // Real clock is running (with audio). This is the normal path AND the exit from a
                // fallback that recovered (unfocused editor regained focus) — clear both states.
                _pausedSince = -1f;
                _retryAccum = 0f;
                FallbackStepping = false;
                return;
            }

            if (FallbackStepping)
            {
                // Keep the reel visible via manual stepping, but periodically retry the real clock so
                // that regaining focus restores clocked playback WITH sound (handled by the branch above
                // once isPlaying flips true).
                _retryAccum += Time.unscaledDeltaTime;
                if (_retryAccum >= 1f)
                {
                    _retryAccum = 0f;
                    _player.Play();
                    if (_player.isPlaying) return; // recovered — next frame takes the isPlaying branch
                }
                StepTick();
                return;
            }

            // Play() was issued in OnPrepared but the clock hasn't started (paused, no error, no frames).
            // Only the diagnosed unfocused-editor / batch case may fall back to silent stepping — a
            // FOCUSED standalone cabinet must NEVER do so (a slow decoder there just waits for the clock,
            // and the normal audible path is never muted).
            bool fallbackAllowed = Application.isEditor || Application.isBatchMode || !Application.isFocused;
            if (!fallbackAllowed)
            {
                _pausedSince = -1f;
                return;
            }

            if (_pausedSince < 0f)
                _pausedSince = Time.realtimeSinceStartup;
            else if (Time.realtimeSinceStartup - _pausedSince > 2f)
            {
                FallbackStepping = true;
                _retryAccum = 0f;
                Debug.LogWarning("[Hub] Attract video clock refused to start (unfocused editor / batch quirk) — " +
                                 "falling back to manual frame stepping; no audio in this mode. Will retry the clock " +
                                 "and restore audible playback if the editor regains focus.");
            }
        }

        // Advance the clip by real elapsed time using StepForward (decode works even while "paused").
        // Capped steps per frame so fast test frames can't run the decoder hot; manual wrap = the loop.
        private void StepTick()
        {
            float fps = _player.frameRate > 0f ? _player.frameRate : 24f;
            _stepAccum += Time.unscaledDeltaTime;
            float secondsPerFrame = 1f / fps;
            int steps = 0;
            while (_stepAccum >= secondsPerFrame && steps < 4)
            {
                _stepAccum -= secondsPerFrame;
                if (_player.frameCount > 0 && _player.frame >= (long)_player.frameCount - 1)
                    _player.frame = 0; // manual loop-around
                else
                    _player.StepForward();
                steps++;
            }
        }

        private void BuildView()
        {
            var canvasGO = new GameObject("AttractVideoCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0; // below the hold-to-launch overlay (sortingOrder 10)
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);

            var imgGO = new GameObject("VideoScreen", typeof(RectTransform), typeof(RawImage));
            imgGO.transform.SetParent(canvas.transform, false);
            var rt = imgGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(RefWidth, RefHeight); // resized to the true aspect on prepare
            _screen = imgGO.GetComponent<RawImage>();
            _screen.color = Color.black; // black until the first decoded frame arrives
        }

        private void BuildPlayer()
        {
            _player = gameObject.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.source = VideoSource.Url;
            _player.url = Path.Combine(Application.streamingAssetsPath, FileName);
            _player.isLooping = true;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.audioOutputMode = VideoAudioOutputMode.Direct; // the cabinet's attract sound
            _player.skipOnDrop = true;

            _player.prepareCompleted += OnPrepared;
            _player.errorReceived += (_, message) =>
                Debug.LogError($"[Hub] Attract video failed: {message}");
            _player.Prepare();
        }

        private void OnPrepared(VideoPlayer vp)
        {
            // Target texture at the clip's own resolution; letterbox the RawImage into the reference rect.
            int w = Mathf.Max(1, (int)vp.width);
            int h = Mathf.Max(1, (int)vp.height);
            _target = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            _target.Create();
            vp.targetTexture = _target;
            _screen.texture = _target;
            _screen.color = Color.white;

            float scale = Mathf.Min(RefWidth / w, RefHeight / h); // FitInside — bars, never crop/stretch
            ((RectTransform)_screen.transform).sizeDelta = new Vector2(w * scale, h * scale);

            vp.Play();
        }

        private void OnDestroy()
        {
            if (_target != null)
            {
                _target.Release();
                Destroy(_target);
            }
        }
    }
}
