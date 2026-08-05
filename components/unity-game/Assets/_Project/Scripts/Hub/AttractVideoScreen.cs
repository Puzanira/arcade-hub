using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The launcher's attract screen: the HubMenu shows a full-screen LOOPING video with sound — the
    /// cabinet's attract reel inviting players to poke the controls. The hold-to-launch stack (charge
    /// bar + themed sprite rain, plus the single approved "СКОРО" overlay) renders ON TOP of the video
    /// from its own higher-sorted canvas.
    ///
    /// attract-screen v2 (founder, 2026-08-05) swapped the reel for a new clip and made only its
    /// BOTTOM zone — the pixel hands and controls — visible: this component paints an opaque panel in
    /// the clip's own background tone over everything above <see cref="AttractZones.MaskBottom"/>,
    /// burying the clip's baked credits line and its baked «НАЖМИ ЧТО-НИБУДЬ» title. The launcher then
    /// redraws both, brighter and live, from <see cref="AttractOverlay"/>. The zone boundaries were
    /// measured off the clip frame by frame — see <see cref="AttractZones"/>.
    ///
    /// The clip lives in StreamingAssets (like launcher-config.json — swappable on the cabinet without a
    /// rebuild). It is decoded into a RenderTexture shown by a RawImage on a bottom-sorted canvas,
    /// letterboxed to the video's own aspect (FitInside math; the shipped 1920×1080 clip fills the 16:9
    /// reference exactly). Audio plays through the default output (direct mode) — the attract sound.
    ///
    /// The reel also YIELDS while somebody plays with it (founder, 2026-08-05: «при запуске можно ли
    /// ставить видео на паузу (и с фейдом его)… при отжатии все восстанавливается»): the moment a control
    /// starts charging a slot the reel fades slowly out behind a veil and, once it is gone, freezes —
    /// picture and sound both — so the screen belongs to the game's title, the charge bar and the themed
    /// rain. Releasing the control brings it back, sound and all. See <see cref="TickEngagement"/>.
    /// </summary>
    [AddComponentMenu("Arcade Hub/Attract Video Screen")]
    [DisallowMultipleComponent]
    public sealed class AttractVideoScreen : MonoBehaviour
    {
        /// <summary>The attract clip's file name inside StreamingAssets.</summary>
        public const string FileName = "Attract.mp4";

        private const float RefWidth = 1920f;
        private const float RefHeight = 1080f;

        private VideoPlayer _player;
        private RawImage _screen;
        private RectTransform _zoneMask;
        private Image _veil;
        private RenderTexture _target;
        private bool _built;
        private float _pausedSince = -1f;
        private float _stepAccum;
        private float _retryAccum;
        private float _fade01;      // 0 = reel at full brightness, 1 = fully faded out
        private bool _chargePaused; // the reel is deliberately frozen while a control is charged

        /// <summary>The video player (test seam: isPlaying / isLooping / frame).</summary>
        public VideoPlayer Player => _player;

        /// <summary>The full-screen RawImage the video is drawn into (test seam: on-screen rect).</summary>
        public RawImage Screen => _screen;

        /// <summary>
        /// The opaque panel hiding the clip's baked credits + title (test seam: it must cover them and
        /// stop short of the hands).
        /// </summary>
        public RectTransform ZoneMask => _zoneMask;

        /// <summary>
        /// The veil that fades the reel out while a control is being charged (test seam: its alpha, and
        /// the fact that it sits UNDER the launcher's own title/ticker/bar but OVER the reel).
        /// </summary>
        public Image Veil => _veil;

        /// <summary>
        /// How far the reel is currently faded out, 0 (playing, full brightness) … 1 (fully veiled).
        /// Ramps over <see cref="AttractZones.ReelFadeOutSeconds"/> /
        /// <see cref="AttractZones.ReelFadeInSeconds"/>; the drawn alpha is this smoothstepped and scaled
        /// by <see cref="AttractZones.ReelFadeMaxAlpha"/>.
        /// </summary>
        public float FadeAmount => _fade01;

        /// <summary>The veil's actual drawn alpha (test seam: what the eye sees, not the raw ramp).</summary>
        public float VeilAlpha => _veil != null ? _veil.color.a : 0f;

        /// <summary>
        /// True while the reel is deliberately frozen — picture AND sound — because a player is charging
        /// a slot (founder, 2026-08-05: «при запуске можно ли ставить видео на паузу (и с фейдом его)»).
        /// This is NOT the unfocused-editor stall: while it is set, the step fallback is disabled outright
        /// so nothing creeps the paused clip forward behind the player's back.
        /// </summary>
        public bool IsChargePaused => _chargePaused;

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
            EnsureBuilt();
            BuildPlayer();
        }

        /// <summary>
        /// Build the video canvas + screen + zone mask, once. Idempotent and safe to call before
        /// <see cref="Start"/>: <see cref="AttractOverlay"/> parents its widgets onto
        /// <see cref="Screen"/> and cannot rely on Unity's Start ordering.
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            BuildView();
        }

        private void Update()
        {
            if (_player == null || !_player.isPrepared) return;

            // Frozen on purpose while a control is charged: the whole watchdog below exists to rescue a
            // reel that WANTS to play and cannot, and it recognises that state as "not playing". Without
            // this gate it would read our own Pause() as the macOS stall and step the clip forward
            // silently — a "paused" video whose frames keep advancing.
            if (_chargePaused) return;

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

        // -------- the reel's fade + freeze while a control is charged --------

        /// <summary>
        /// Drive the reel's engagement state one frame. <paramref name="engaged"/> is the SAME signal the
        /// title and the charge bar run on (<see cref="IAttractSlotSource.ActiveSlot"/> ≥ 0), so the reel
        /// fading out, the title turning into the game's name and the bar appearing are one movement, and
        /// the reel comes back on exactly the signal the title returns to the cabinet name on.
        ///
        /// Called from <see cref="AttractOverlay.Tick"/> rather than from this component's own
        /// <see cref="Update"/>: one driver, one clock, and the PlayMode tests that already step the
        /// overlay with a fixed dt step the fade deterministically too.
        ///
        /// The clip is paused only once it has FULLY faded out — pausing on the first frame of the fade
        /// would cut the attract sound dead while the picture was still leaving.
        /// </summary>
        public void TickEngagement(bool engaged, float dt)
        {
            if (dt < 0f) dt = 0f;

            float perSecond = engaged
                ? 1f / Mathf.Max(0.01f, AttractZones.ReelFadeOutSeconds)
                : 1f / Mathf.Max(0.01f, AttractZones.ReelFadeInSeconds);
            _fade01 = Mathf.MoveTowards(_fade01, engaged ? 1f : 0f, perSecond * dt);
            ApplyFade();

            if (_player == null || !_player.isPrepared) return;

            if (engaged)
            {
                if (!_chargePaused && _fade01 >= 1f)
                {
                    _chargePaused = true;
                    // Leave the stall watchdog in a clean state: while paused it is gated off entirely,
                    // and on release it must start judging the clock from scratch.
                    FallbackStepping = false;
                    _pausedSince = -1f;
                    _retryAccum = 0f;
                    _stepAccum = 0f;
                    _player.Pause();
                }
                return;
            }

            if (_chargePaused)
            {
                _chargePaused = false;
                _pausedSince = -1f;
                _retryAccum = 0f;
                _stepAccum = 0f;
                _player.Play(); // sound and picture return together as the veil lifts
            }
        }

        private void ApplyFade()
        {
            if (_veil == null) return;
            Color c = AttractZones.BackgroundColor;
            // Smoothstep the drawn alpha: a linear alpha ramp reads as a hard start and a hard stop, and
            // the founder asked for the reel to leave SLOWLY.
            c.a = AttractZones.ReelFadeMaxAlpha * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_fade01));
            _veil.color = c;
            // An idle attract screen draws no veil at all, not a transparent full-screen quad.
            if (_veil.enabled != (c.a > 0.001f))
                _veil.enabled = c.a > 0.001f;
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

            BuildZoneMask();
            BuildVeil();
        }

        // The fade-out layer for "somebody is charging a slot". It is a PANEL over the reel rather than a
        // tint on the RawImage for one measured reason: multiplying the video's own colour drives it to
        // BLACK, while the zone mask above it stays #262626 — which tears a hard seam across the screen at
        // MaskBottom exactly when the player is looking hardest. A veil in the clip's own background tone
        // covers reel and mask alike, so a fully faded screen is one flat #262626 field with the title,
        // the bar and the rain on it. Built here, before AttractOverlay adds its widgets, so it sits UNDER
        // the launcher's own title/ticker/charge bar — those must stay bright while the reel leaves.
        private void BuildVeil()
        {
            var go = new GameObject("ReelVeil", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_screen.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _veil = go.GetComponent<Image>();
            _veil.raycastTarget = false;
            _veil.enabled = false;
            ApplyFade();
        }

        // The founder's "only the red zone stays visible" rule (2026-08-05). One opaque panel in the
        // clip's own flat background tone (#262626) covering clip rows 0…MaskBottom — i.e. the baked
        // credits AND the baked title — parented to the video rect with NORMALISED anchors, so it keeps
        // registering with the video however the FitInside pass letterboxes it. It is a child of the
        // RawImage, hence drawn after it; AttractOverlay's own text is added later still and lands on
        // top of the panel.
        private void BuildZoneMask()
        {
            var go = new GameObject("ZoneMask", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_screen.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, AttractZones.BottomFraction(AttractZones.MaskBottom));
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = AttractZones.BackgroundColor;
            img.raycastTarget = false;
            _zoneMask = rt;
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
