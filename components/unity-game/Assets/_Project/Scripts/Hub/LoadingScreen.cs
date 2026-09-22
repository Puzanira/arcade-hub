using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The cabinet's LOADING SCREEN (founder at the live cabinet, 2026-09-22: «показать что идет
    /// загрузка»). Both of the launcher's transitions — starting a game and coming back to the menu —
    /// are blocking <see cref="SceneManager.LoadScene"/> calls, so the picture simply STOPS for a second
    /// or two and the cabinet reads as hung. This screen is what stands in that gap.
    ///
    /// <b>The whole problem is getting it onto the glass BEFORE the freeze.</b> A blocking load does not
    /// give the renderer a turn: building a canvas and calling LoadScene on the next line shows the
    /// player nothing at all — the first frame that could have carried it is the frame that never
    /// finishes. So the load is DEFERRED by this object's own coroutine:
    ///
    /// <code>
    ///   frame N    Show() builds the canvas   → Unity renders the frame → the screen is on the glass
    ///   frame N+1  coroutine resumes, waits one more frame (a dropped/skipped frame must not be able
    ///              to swallow the only chance the screen had to be drawn)
    ///   frame N+2  SceneManager.LoadScene(...) — the freeze happens with the screen already visible,
    ///              and it STAYS visible because this object is DontDestroyOnLoad
    /// </code>
    ///
    /// Frame COUNTING, not <c>WaitForEndOfFrame</c>: the latter never resumes under
    /// <c>-batchmode -nographics</c>, which is exactly how the EditMode leg of the suite runs — a pending
    /// load would hang there forever. Counting frames behaves identically in every mode, and it is also
    /// immune to <see cref="Time.timeScale"/>, which matters because a game being exited has usually
    /// frozen itself to zero by the time the MENU button is read.
    ///
    /// <b>Over ANY game.</b> The return watchdog (<see cref="LauncherReturn"/>) fires inside a game whose
    /// UI the launcher knows nothing about, so this screen may not depend on that game's canvases: it
    /// brings its own <see cref="RenderMode.ScreenSpaceOverlay"/> canvas at
    /// <see cref="CanvasSortingOrder"/> (the top of the short range Unity sorts overlays in). A
    /// screen-space-overlay canvas is composited after everything any camera drew, so 3D, sprites,
    /// camera-space UI and world-space UI are all covered regardless of the game's own sorting.
    ///
    /// <b>It never touches input.</b> No <see cref="GraphicRaycaster"/>, no EventSystem, every graphic
    /// with <c>raycastTarget = false</c>: the cabinet's controls are polled through
    /// <see cref="AiGameStudio.ArcadeControls.ArcadeInput"/> and cannot be intercepted by a canvas — and
    /// without a raycaster this one cannot even swallow a pointer. The MENU button and the hold-to-launch
    /// gesture behave exactly as they did before it existed.
    ///
    /// <b>It survives the menu's janitor</b> by being hub code: <see cref="HubDdolJanitor"/> whitelists
    /// the "AiGameStudio" namespaces, so the sweep in <see cref="HubMenuController"/>.Start walks past it.
    ///
    /// The LOOK is deliberately the attract screen's own (see <see cref="AttractZones"/>): the same flat
    /// #262626 field, the title in the same band and the same Russo One face the attract title uses, and
    /// the same notched amber bar geometry the charge bar is drawn in — full, with the notches MARCHING
    /// rather than an amber edge creeping along, because a blocking load has no progress to report and a
    /// part-filled bar frozen mid-load is precisely the "it is stuck" reading this screen exists to
    /// remove (see <see cref="MarchSeconds"/>). That gives both transitions continuity instead of a
    /// system dialog: a launch cuts from "title + full charge bar" to the same title and the same full
    /// bar, now working, and a return already shows «6 режимов суеты» exactly where the attract screen
    /// is about to put it, so the reel fades up UNDER a title that never moved.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class LoadingScreen : MonoBehaviour
    {
        /// <summary>The founder's own word for what the screen is saying (2026-09-22).</summary>
        public const string Caption = "ЗАГРУЗКА";

        /// <summary>
        /// Top of the range Unity sorts screen-space-overlay canvases in (the field is stored as a
        /// short). Nothing a game ships can legally sort above it.
        /// </summary>
        public const int CanvasSortingOrder = 32767;

        /// <summary>
        /// Frames the screen is given to reach the glass before the blocking load is issued. One would
        /// do on a healthy frame; two means a single skipped or dropped frame still cannot swallow the
        /// show. At 60 Hz this costs ~33 ms against a load that costs seconds.
        /// </summary>
        public const int FramesBeforeLoad = 2;

        /// <summary>
        /// Frames the screen stays up after the new scene has loaded, so the cut lands on a frame the
        /// arriving scene has actually drawn (its Awake/Start have run and rendered at least once)
        /// rather than on its first, half-built one.
        /// </summary>
        public const int FramesAfterLoad = 2;

        /// <summary>
        /// Hard cap on the whole hold, in unscaled seconds. The menu hold waits for the attract clip's
        /// first frame (see <see cref="WaitsForAttractReel"/>), and a missing or broken clip must not be
        /// able to trap the cabinet behind a loading screen — after this it drops regardless.
        /// </summary>
        public const float MaxHoldSeconds = 5f;

        /// <summary>
        /// Seconds one notch takes to march the width of a segment — the bar's whole animation.
        ///
        /// It is a MARCH and not a block sweeping a track on purpose. A blocking load has no progress to
        /// report, and whatever the bar is drawing when the freeze starts is what the player stares at
        /// for the next second or two: a part-filled bar frozen a fifth of the way across reads as a
        /// machine STUCK at 20%, which is the exact impression this whole screen exists to remove. A
        /// full bar cut into marching notches has no "how far" to misread — standing still it is a ticked
        /// amber rule under the title, and it is also, to the pixel, what the charge bar looks like at
        /// the instant the player finished charging it.
        /// </summary>
        public const float MarchSeconds = 0.42f;

        /// <summary>The caption's band, in the clip pixel space <see cref="AttractZones"/> is measured in.</summary>
        public const float CaptionBandTop = 352f;
        public const float CaptionBandBottom = 404f;

        /// <summary>Caption size in clip pixels — a quiet line under the bar, never a second title.</summary>
        public const int CaptionFontSize = 40;

        private static LoadingScreen _instance;

        /// <summary>The live loading screen, if one is up (test/inspection seam).</summary>
        public static LoadingScreen Instance => _instance;

        /// <summary>
        /// Test/shot seam: while false, a shown screen holds its pending scene load instead of issuing
        /// it, so a test can prove the screen is ALREADY visible while the old scene is still running —
        /// and shoot that exact moment. Production leaves it true; tests must put it back.
        /// </summary>
        public static bool AutoLoad = true;

        private Canvas _canvas;
        private Image _backdrop;
        private Text _title;
        private Text _caption;
        private RectTransform _barRoot;
        private RectTransform _barTrack;
        private RectTransform _barFill;
        private RectTransform _notches;
        private float _marchTime;

        private string _targetScene;
        private int _loadsSeen;

        // -------- read surface (tests / tooling) --------

        /// <summary>The overlay canvas (test seam: sorting order, render mode, no raycaster).</summary>
        public Canvas Canvas => _canvas;

        /// <summary>The opaque field the whole screen is painted on (test seam: it covers everything).</summary>
        public Image Backdrop => _backdrop;

        /// <summary>What is being loaded — the game's name, or the cabinet's on the way back.</summary>
        public Text TitleLabel => _title;

        /// <summary>The «ЗАГРУЗКА» line (test seam).</summary>
        public Text CaptionLabel => _caption;

        /// <summary>The bar's slot — the amber fills it whole (test seam).</summary>
        public RectTransform BarTrack => _barTrack;

        /// <summary>The amber inside the bar; a full track, never a measurement (test seam).</summary>
        public RectTransform BarFill => _barFill;

        /// <summary>The marching row of notches cut into the amber (test seam: it moves).</summary>
        public RectTransform Notches => _notches;

        /// <summary>The scene this screen is covering the load of, or null for a bare (posed) screen.</summary>
        public string TargetScene => _targetScene;

        /// <summary>True once the blocking load has actually been issued.</summary>
        public bool LoadIssued { get; private set; }

        /// <summary>
        /// True when this screen holds until the attract reel has a frame to show. Coming back to the
        /// menu, <see cref="HubMenuController"/> rebuilds the whole attract stack and the clip is only
        /// PREPARED asynchronously afterwards, so the first few hundred milliseconds of the menu are an
        /// empty screen. Dropping the loading screen on the scene load would therefore trade one freeze
        /// for a blank flash; it holds until <see cref="AttractVideoScreen.IsShowingFrames"/> instead, so
        /// the player goes straight from «ЗАГРУЗКА» to a running reel. Launching a GAME has no such
        /// signal — the launcher knows nothing about what a game considers "ready" — so that direction
        /// cuts after <see cref="FramesAfterLoad"/>.
        /// </summary>
        public bool WaitsForAttractReel => _targetScene == HubScenes.HubMenu;

        // -------- the two ways in --------

        /// <summary>
        /// Put the loading screen up and load <paramref name="sceneName"/> once it has been DRAWN — the
        /// launcher's only scene-loading call. <paramref name="title"/> names what is coming (the game's
        /// display name on the way in, the cabinet's name on the way back).
        /// </summary>
        public static void LoadSceneWhenShown(string sceneName, string title)
        {
            LoadingScreen screen = Show(title);
            screen._targetScene = sceneName;
            screen.StartCoroutine(screen.ShowThenLoad());
        }

        /// <summary>
        /// Build and show a bare loading screen (no pending load). The production entry point is
        /// <see cref="LoadSceneWhenShown"/>; this is what the screenshot tooling poses.
        /// </summary>
        public static LoadingScreen Show(string title)
        {
            if (_instance != null)
                Destroy(_instance.gameObject);

            var go = new GameObject("~LoadingScreen");
            DontDestroyOnLoad(go);
            var screen = go.AddComponent<LoadingScreen>();
            screen.BuildView(title);
            _instance = screen;
            return screen;
        }

        // -------- lifetime --------

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _loadsSeen++;
            // The first load is the one this screen is covering. A SECOND one means somebody else is
            // driving the cabinet now (a test loading a scene directly, a game loading its own): the
            // screen's job is over and it must never linger over a scene it knows nothing about.
            if (_loadsSeen > 1)
                Destroy(gameObject);
        }

        private IEnumerator ShowThenLoad()
        {
            // Give the engine its turn: the frame this screen was built in renders at its end, so after
            // FramesBeforeLoad yields the player has demonstrably seen it — see the class summary for
            // why this is a frame count and not WaitForEndOfFrame.
            for (int i = 0; i < FramesBeforeLoad; i++)
                yield return null;

            while (!AutoLoad)
                yield return null;

            LoadIssued = true;
            try
            {
                SceneManager.LoadScene(_targetScene);
            }
            catch (System.Exception e)
            {
                // A scene missing from Build Settings must not leave the cabinet behind a screen that
                // covers nothing.
                Debug.LogError($"[Hub] Loading screen could not load scene '{_targetScene}': {e.Message}");
                Destroy(gameObject);
                yield break;
            }

            yield return null; // the new scene's Awake/Start have run by now

            float held = 0f;
            int frames = 0;
            while (true)
            {
                if (held >= MaxHoldSeconds) break;

                if (WaitsForAttractReel)
                {
                    var video = FindAnyObjectByType<AttractVideoScreen>();
                    if (video != null && video.IsShowingFrames && frames >= FramesAfterLoad) break;
                }
                else if (frames >= FramesAfterLoad)
                {
                    break;
                }

                held += Time.unscaledDeltaTime;
                frames++;
                yield return null;
            }

            Destroy(gameObject);
        }

        private void Update()
        {
            // Unscaled: a game frozen at timeScale 0 is the normal state on the way out of one.
            _marchTime += Time.unscaledDeltaTime;
            LayoutMarch();
        }

        // -------- view --------

        // March the notch row one segment to the left per MarchSeconds and wrap. The row carries one
        // spare notch at each end, so the wrap re-uses a notch that was already on screen and the
        // pattern never blinks; the track's mask keeps the spares from drawing over the frame. Unscaled
        // time, so the bar is genuinely moving on BOTH sides of the freeze — before the load is issued,
        // and again while the menu waits for the reel's first frame.
        private void LayoutMarch()
        {
            if (_notches == null || _barTrack == null) return;
            float track = _barTrack.rect.width;
            if (track <= 0f) return;

            float segment = track / AttractZones.ChargeBarSegments;
            float phase = Mathf.Repeat(_marchTime / MarchSeconds, 1f);
            _notches.anchoredPosition = new Vector2(-phase * segment, 0f);
        }

        private void BuildView(string title)
        {
            var canvasGO = new GameObject("LoadingCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // The very top of the cabinet's sorting: the last sorting layer defined in the project (a
            // game shipped as a package cannot add one), and the top of the order range inside it. A
            // game's own full-screen HUD cannot legally end up above this.
            SortingLayer[] layers = SortingLayer.layers;
            if (layers != null && layers.Length > 0)
                _canvas.sortingLayerID = layers[layers.Length - 1].id;
            _canvas.sortingOrder = CanvasSortingOrder;
            // NO GraphicRaycaster on purpose (see the class summary): the screen must be incapable of
            // taking input, not merely uninterested in it.
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(AttractZones.ClipWidth, AttractZones.ClipHeight);
            // Expand = the canvas is never smaller than the reference in either axis, so the fixed-size
            // block below is uniformly scaled and fully visible on any surface (FitInside), exactly like
            // the letterboxed attract reel it stands in for.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            // The field: the clip's own flat #262626, fully opaque, edge to edge — whatever game was on
            // the screen a frame ago is gone, and the menu arriving underneath is the same tone.
            var bgGO = new GameObject("Field", typeof(RectTransform), typeof(Image));
            bgGO.transform.SetParent(_canvas.transform, false);
            var bgRt = bgGO.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            _backdrop = bgGO.GetComponent<Image>();
            _backdrop.color = AttractZones.BackgroundColor;
            _backdrop.raycastTarget = false;

            // The attract screen's own 1920×1080 frame, centred: everything below is anchored in the
            // SAME clip-pixel space AttractZones measures, so the title and the bar land exactly where
            // the attract screen puts them.
            var blockGO = new GameObject("Block", typeof(RectTransform));
            blockGO.transform.SetParent(_canvas.transform, false);
            var block = blockGO.GetComponent<RectTransform>();
            block.anchorMin = new Vector2(0.5f, 0.5f);
            block.anchorMax = new Vector2(0.5f, 0.5f);
            block.pivot = new Vector2(0.5f, 0.5f);
            block.sizeDelta = new Vector2(AttractZones.ClipWidth, AttractZones.ClipHeight);
            block.anchoredPosition = Vector2.zero;

            BuildTitle(block, title);
            BuildBar(block);
            BuildCaption(block);
            LayoutMarch();
        }

        private void BuildTitle(RectTransform block, string title)
        {
            Font display = Resources.Load<Font>("Fonts/RussoOne")
                           ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var go = new GameObject("LoadingTitle", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(block, false);
            var rt = go.GetComponent<RectTransform>();
            float sideMargin = AttractZones.TitleSideMargin / AttractZones.ClipWidth;
            rt.anchorMin = new Vector2(sideMargin, AttractZones.BottomFraction(AttractZones.TitleBandBottom));
            rt.anchorMax = new Vector2(1f - sideMargin, AttractZones.TopFraction(AttractZones.TitleBandTop));
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _title = go.GetComponent<Text>();
            _title.font = display;
            _title.alignment = TextAnchor.MiddleCenter;
            _title.raycastTarget = false;
            // Best-fit, same as the attract title: «6 режимов суеты» gets to be huge, a long game name
            // shrinks instead of running off the side.
            _title.resizeTextForBestFit = true;
            _title.resizeTextMinSize = 44;
            _title.resizeTextMaxSize = 132;
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.verticalOverflow = VerticalWrapMode.Truncate;
            _title.color = Color.white;
            _title.text = title ?? string.Empty;
        }

        // The charge bar's exact idiom — hard pixel edges, a thin amber frame, a near-black slot and the
        // amber cut into notches — built from the SAME AttractZones constants the charge bar reads, so
        // retuning that look retunes this one. What differs is the amber: there it grows from the left
        // with the charge, here a block sweeps, because there is nothing to measure.
        private void BuildBar(RectTransform block)
        {
            float halfWidth = AttractZones.ChargeBarWidth / 2f / AttractZones.ClipWidth;

            var rootGO = new GameObject("LoadingBar", typeof(RectTransform));
            rootGO.transform.SetParent(block, false);
            _barRoot = rootGO.GetComponent<RectTransform>();
            _barRoot.anchorMin = new Vector2(0.5f - halfWidth,
                AttractZones.BottomFraction(AttractZones.ChargeBandBottom));
            _barRoot.anchorMax = new Vector2(0.5f + halfWidth,
                AttractZones.TopFraction(AttractZones.ChargeBandTop));
            _barRoot.offsetMin = Vector2.zero;
            _barRoot.offsetMax = Vector2.zero;

            StretchedImage(_barRoot, "LoadingBarFrame", 0f, AttractZones.ChargeFrameColor);
            _barTrack = StretchedImage(_barRoot, "LoadingBarTrack",
                AttractZones.ChargeBarBorder, AttractZones.ChargeTrackColor);
            // The notch row marches, and its spare notches live just outside the track; the mask is what
            // keeps them from drawing over the amber frame.
            _barTrack.gameObject.AddComponent<RectMask2D>();

            // A FULL track of amber — the charge bar at the instant it finished charging.
            _barFill = StretchedImage(_barTrack, "LoadingBarFill", 0f, AttractZones.ChargeColor);

            // The notches: gaps punched in the track's own colour, the same steps the charge bar ticks
            // up in — here they march instead of standing still. Held in one row so the whole pattern
            // moves as a unit, with a spare notch at each end for a seamless wrap.
            var rowGO = new GameObject("LoadingBarNotches", typeof(RectTransform));
            rowGO.transform.SetParent(_barTrack, false);
            _notches = rowGO.GetComponent<RectTransform>();
            _notches.anchorMin = Vector2.zero;
            _notches.anchorMax = Vector2.one;
            _notches.offsetMin = Vector2.zero;
            _notches.offsetMax = Vector2.zero;

            Color notch = AttractZones.ChargeTrackColor;
            notch.a = 1f;
            for (int i = 0; i <= AttractZones.ChargeBarSegments + 1; i++)
            {
                float f = (float)i / AttractZones.ChargeBarSegments;
                var go = new GameObject("Notch", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_notches, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(f, 0f);
                rt.anchorMax = new Vector2(f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(AttractZones.ChargeBarSegmentGap, 0f);
                var img = go.GetComponent<Image>();
                img.color = notch;
                img.raycastTarget = false;
            }
        }

        private void BuildCaption(RectTransform block)
        {
            // Arimo Regular — the launcher's light face, the one the ticker is set in. The word is the
            // only text on the screen that is not a name, so it stays quiet: small, amber, under the bar.
            Font body = Resources.Load<Font>("Fonts/Arimo-Regular")
                        ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var go = new GameObject("LoadingCaption", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(block, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, AttractZones.BottomFraction(CaptionBandBottom));
            rt.anchorMax = new Vector2(1f, AttractZones.TopFraction(CaptionBandTop));
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _caption = go.GetComponent<Text>();
            _caption.font = body;
            _caption.fontSize = CaptionFontSize;
            _caption.alignment = TextAnchor.MiddleCenter;
            _caption.raycastTarget = false;
            _caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            _caption.verticalOverflow = VerticalWrapMode.Overflow;
            Color c = AttractZones.ChargeColor;
            c.a = 0.85f;
            _caption.color = c;
            _caption.text = Caption;
        }

        private static RectTransform StretchedImage(RectTransform parent, string name, float inset, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }
    }
}
