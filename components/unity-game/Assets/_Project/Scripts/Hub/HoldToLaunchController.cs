using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using AiGameStudio.ArcadeControls;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// Hold-to-launch (cabinet spec §2.3), layered on top of the hub-v0 menu. Each slot is bound to a
    /// physical control (<c>controlName</c> in the config); holding / cranking / deflecting that control
    /// charges a bar 0→1 over ~5 s, releasing decays it to 0 over ~1 s, and only one control charges at a
    /// time (exclusivity). At full charge an installed slot loads its scene; a planned slot shows a
    /// "СКОРО" overlay and resets.
    ///
    /// The bar itself is NOT a bottom-of-screen HUD element any more (founder, 2026-08-05: «полосу
    /// прогрузки вставлять под название» + «единого цвета»). It hangs in the band the attract screen
    /// reclaims from the clip, directly under the game's title, framed in the reel's own pixel idiom, and
    /// in ONE colour for every slot — see <see cref="BuildChargeBar"/> and
    /// <see cref="AttachChargeBarTo"/>.
    ///
    /// The launch feedback is the arcade cabinet's SHARED start animation: falling sprites rain down and
    /// pile up a screen-filling grid from the bottom row up, covered in proportion to the charge and
    /// cleared from the top down as it decays. The fall/fill behaviour is adapted from the per-game start
    /// animation in the Lady Bug game by tony20202021 (external/lady_bug, IntroSequence.cs) — the founder's
    /// decision was to lift that flower animation out of the game and make it the launcher's universal
    /// launch animation for every slot. Each slot brings its own falling-sprite set via <c>rainSprites</c>
    /// in the config (Resources paths; single-sprite <c>rainSprite</c> also accepted) — each cell picks one
    /// at random, like the source intro mixed its ten flowers. The Lady Bug slot ships that game's own ten
    /// flower sprites; slots without sprites fall back to a solid slot-coloured tile (themed per-game
    /// sprites arrive later from manifests).
    ///
    /// Input is read ONLY through <see cref="ArcadeInput"/>. The charge/decay/exclusivity and the
    /// engagement thresholds live in the pure, EditMode-tested <see cref="LaunchChargeMachine"/> and
    /// <see cref="LaunchInputSampler"/>; this class is the Unity adapter + view.
    /// </summary>
    [AddComponentMenu("Arcade Hub/Hold To Launch Controller")]
    [DisallowMultipleComponent]
    public sealed class HoldToLaunchController : MonoBehaviour, IAttractSlotSource
    {
        // Slot-colour palette (7 slots); index wraps if a config has more.
        private static readonly Color[] SlotColors =
        {
            new Color(0.95f, 0.30f, 0.30f), // red
            new Color(0.35f, 0.80f, 0.45f), // green
            new Color(0.35f, 0.65f, 1.00f), // blue
            new Color(1.00f, 0.75f, 0.25f), // amber
            new Color(0.80f, 0.45f, 0.95f), // violet
            new Color(0.30f, 0.85f, 0.85f), // teal
            new Color(1.00f, 0.55f, 0.80f), // pink
        };

        // Grid geometry — the source animation's own numbers (lady_bug SceneSetup.CreateIntroScreen),
        // kept verbatim so the launcher's pile looks like the game's did: 140-unit cells tiled a little
        // past the reference screen's edges, each sprite drawn at 0.9 of the cell (a slight gap so the
        // pieces read as individual flowers, not one solid mass), jittered up to 0.45 of a cell off the
        // exact grid point so the fill order stays bottom-row-up but the result looks organic.
        [Header("Flower-fill grid (shared launch animation, geometry from lady_bug)")]
        [Tooltip("Grid cell size in reference-resolution units; columns/rows derive from it (source: 140).")]
        [SerializeField] private float cellSize = 140f;
        [Tooltip("Sprite size as a fraction of the cell (source: 0.9 — slight gaps between pieces).")]
        [SerializeField] private float sizeShrink = 0.9f;
        [Tooltip("Max random offset off the exact grid point, as a fraction of the cell (source: 0.45).")]
        [SerializeField] private float jitterFraction = 0.45f;
        [Tooltip("How far above its resting cell a sprite starts its fall (source: IntroSequence.fallDistance = 400).")]
        [SerializeField] private float fallOffset = 400f;
        [Tooltip("Seconds a single sprite takes to fall from its start into its resting cell (source: IntroSequence.fallDuration).")]
        [SerializeField] private float fallDuration = 0.4f;
        [Tooltip("Largest a falling object may be drawn, as a multiple of the base cell sprite size. Every " +
                 "spawn rolls its own size in [1, this]; the base size is therefore the SMALLEST an object " +
                 "can be (founder, 2026-08-05: «текущих брать как самый маленький размер»).")]
        [SerializeField] private float rainSizeMax = 1.75f;

        /// <summary>When false, <see cref="Update"/> does not advance — tests drive <see cref="Tick"/> with a fixed dt.</summary>
        public bool AutoTick = true;

        /// <summary>
        /// While true, NOTHING charges and nothing rains: the launcher is muted because a full-screen
        /// screen is up over the attract reel (the «История проекта» document — see
        /// <see cref="AboutScreenController"/>). The joystick that scrolls that document is also a slot's
        /// launch control, so this is not cosmetic: without it, reading the story would start a game.
        ///
        /// The charge is HELD AT ZERO rather than left to decay, so no partial charge can survive under
        /// the document and no launch can fire out from behind it.
        /// </summary>
        public bool Suspended { get; set; }

        private LauncherConfig _config;
        private LaunchInputSampler _sampler;
        private LaunchChargeMachine _machine;
        private bool[] _engaged;

        private Canvas _canvas;
        private RectTransform _barRoot;   // the framed bar as a whole; tracks the attract reel's own rect
        private RectTransform _barTrack;  // the empty slot inside the frame
        private RectTransform _barFill;   // the amber that grows across the track
        private Text _comingSoon;
        private RectTransform _flowerRoot;
        private AttractVideoScreen _attractVideo;
        private Font _font;

        // The full screen-filling grid, one cell per Image, all built inactive up-front; only the first
        // _filledCount entries of _fillOrder are shown at any moment (bottom row up).
        private RectTransform[] _cells;
        private Image[] _cellImages;
        private Vector2[] _cellTargets;   // resting anchoredPosition of each cell
        private Vector2[] _cellStarts;    // start-of-fall position of each cell's current drop
        private float[] _cellFallT;       // elapsed fall time for each cell's current drop
        private int[] _fillOrder;         // cell indices in bottom-row-first (shuffled within a row) order
        private int _filledCount;
        private int _viewSlot = -1;       // slot whose sprite/colour the currently-shown cells carry
        private readonly List<RectTransform> _flowerView = new List<RectTransform>();
        private readonly Dictionary<int, Sprite[]> _slotSpriteCache = new Dictionary<int, Sprite[]>();
        private int _gridColumns;
        private int _gridRows;
        private float _cellBaseSize;      // the SMALLEST a falling object is drawn (sizes roll upward from it)

        private const float RefWidth = 1920f;
        private const float RefHeight = 1080f;

        // -------- public read surface (for tests) --------

        public float Charge => _machine?.Charge ?? 0f;
        public int ActiveSlot => _machine?.ActiveSlot ?? -1;

        /// <summary>
        /// The config this controller was built from. Exposed so <see cref="AttractOverlay"/> can name
        /// the active slot from the SAME loaded slot list the charge machine indexes into — reloading
        /// the JSON separately would risk the title naming a different game than the bar is charging.
        /// </summary>
        public LauncherConfig Config => _config;

        /// <summary>Number of sprites currently piled on the grid (grows with charge, clears on decay).</summary>
        public int FlowerCount => _filledCount;

        /// <summary>Total grid capacity (columns × rows) — a full charge fills exactly this many cells.</summary>
        public int FlowerCapacity => _cells != null ? _cells.Length : 0;

        /// <summary>The bottom-most (first-filled) sprite, or null when the grid is empty.</summary>
        public RectTransform FirstFlower => _filledCount > 0 ? _cells[_fillOrder[0]] : null;

        /// <summary>All piled sprites, in fill order (test seam: assert at least one is visibly on-screen).</summary>
        public IReadOnlyList<RectTransform> Flowers
        {
            get
            {
                _flowerView.Clear();
                for (int i = 0; i < _filledCount; i++)
                    _flowerView.Add(_cells[_fillOrder[i]]);
                return _flowerView;
            }
        }

        public RectTransform BarFill => _barFill;

        /// <summary>
        /// The framed bar as a whole (test seam: it must sit in the band UNDER the attract title, not at
        /// the bottom of the screen).
        /// </summary>
        public RectTransform ChargeBar => _barRoot;

        /// <summary>The bar's empty slot — the fill's full-charge width (test seam).</summary>
        public RectTransform ChargeBarTrack => _barTrack;

        /// <summary>Smallest size a falling object is ever drawn at, in reference pixels (test seam).</summary>
        public float RainSizeMin => _cellBaseSize;

        /// <summary>Largest size a falling object may roll, in reference pixels (test seam).</summary>
        public float RainSizeMax => _cellBaseSize * Mathf.Max(1f, rainSizeMax);

        public Text ComingSoonLabel => _comingSoon;
        public bool ComingSoonVisible => _comingSoon != null && _comingSoon.gameObject.activeSelf;

        /// <summary>The overlay canvas (for the screenshot tool to retarget it to an offscreen camera).</summary>
        public Canvas Canvas => _canvas;

        /// <summary>Test/tooling seam: build from an explicit config instead of StreamingAssets.</summary>
        public void InitializeWith(LauncherConfig config)
        {
            Build(config);
        }

        private void Start()
        {
            if (_machine == null)
                Build(LauncherConfigLoader.LoadFromStreamingAssets());
        }

        private void Update()
        {
            if (AutoTick) Tick(Time.deltaTime);
        }

        // -------- core loop --------

        /// <summary>Advance one frame: read input, charge, redraw, and launch/reset on full.</summary>
        public void Tick(float dt)
        {
            if (_machine == null) return;

            if (Suspended)
            {
                // Muted by a full-screen screen: input is not even read, the machine is pinned at idle,
                // and the bar and the rain are cleared. Nothing can be launched from under the document.
                _machine.Reset();
                ClearFlowers();
                SetBarFill(0f);
                return;
            }

            ControlReadings r = ReadInput();
            _sampler.Sample(r, dt, _engaged);
            int launch = _machine.Tick(_engaged, dt);

            RedrawCharge(dt);

            if (launch >= 0)
                Fire(launch);
        }

        private void Fire(int slotIndex)
        {
            LauncherSlot slot = _config.slots[slotIndex];
            if (slot.IsInstalled)
            {
                // External games reset in place on MenuButton but don't know the hub's menu scene, so the
                // launcher arms a return watchdog. The built-in TestGame returns to the menu itself.
                if (slot.entryScene != HubScenes.TestGame)
                    LauncherReturn.ArmFor(HubScenes.HubMenu, slot.nativeInput);
                SceneManager.LoadScene(slot.entryScene);
                return;
            }

            // Planned slot: surface "СКОРО — <game>" and reset the charge (spec §2.3).
            if (_comingSoon != null)
            {
                _comingSoon.text = $"СКОРО — {slot.displayName}";
                // Same amber as the bar that just filled to summon it — the planned-slot ending has to
                // look like the charge it came out of, not like a seventh slot colour.
                _comingSoon.color = AttractZones.ChargeColor;
                _comingSoon.gameObject.SetActive(true);
            }
            _machine.Reset();
            ClearFlowers();
            SetBarFill(0f);
        }

        private ControlReadings ReadInput()
        {
            Vector2 j = ArcadeInput.Joystick.Vector;
            return new ControlReadings
            {
                CrankDeltaDegrees = ArcadeInput.Crank.DeltaDegrees,
                RedHeld = ArcadeInput.RedButton.IsHeld,
                GreenHeld = ArcadeInput.GreenButton.IsHeld,
                BangHeld = ArcadeInput.BangButton.IsHeld,
                HeightA = ArcadeInput.HeightA.Value,
                HeightB = ArcadeInput.HeightB.Value,
                JoystickX = j.x,
                JoystickY = j.y,
            };
        }

        // -------- view --------

        private void RedrawCharge(float dt)
        {
            float charge = _machine.Charge;
            int slot = _machine.ActiveSlot;

            // ONE colour for every slot (founder, 2026-08-05) — the bar no longer repaints itself per
            // slot. Which game is being charged is said by the title above the bar and by the themed rain;
            // a bar that also changed colour made the screen read as three competing signals.
            SetBarFill(charge);

            if (charge <= 0f || slot < 0)
            {
                ClearFlowers();
                return;
            }

            // Re-skin the grid the instant a new slot takes over (exclusivity means the previous slot's
            // pile has already decayed to nothing before this happens, so nothing visible is re-coloured
            // mid-fall — this only sets what the next fills will look like).
            if (slot != _viewSlot)
                _viewSlot = slot;

            // Cover the grid in proportion to charge: the first round(charge·capacity) cells (bottom row up)
            // are piled, the rest empty. This is the launcher's shared launch animation — see class summary.
            int target = Mathf.Clamp(Mathf.RoundToInt(charge * _cells.Length), 0, _cells.Length);
            while (_filledCount < target)
                ActivateCell(_filledCount, slot, animated: true);
            while (_filledCount > target)
                DeactivateTopCell();

            AdvanceFalls(dt);
        }

        // Bring cell fillOrder[slotInFill] into view: skin it for the active slot, roll its size and start
        // its fall. Every spawn gets its OWN size (founder, 2026-08-05: «рандомизация для всех игр
        // размеров ассетов — причем текущих брать как самый маленький размер — чтобы было интереснее»), so
        // the pile reads as a heap of different objects instead of a tiled texture. The old fixed size is
        // the bottom of the range, never the average: nothing ever gets SMALLER than what she already
        // approved, the rain only gains bigger pieces. Rolled per activation, so a cell that clears and
        // re-fills comes back a different size — the pile is never the same twice.
        private void ActivateCell(int slotInFill, int slot, bool animated)
        {
            int k = _fillOrder[slotInFill];
            RectTransform rt = _cells[k];
            SkinCell(_cellImages[k], slot);
            float size = _cellBaseSize * Random.Range(1f, Mathf.Max(1f, rainSizeMax));
            rt.sizeDelta = new Vector2(size, size);
            _cellStarts[k] = _cellTargets[k] + new Vector2(0f, fallOffset);
            _cellFallT[k] = animated ? 0f : fallDuration;
            rt.anchoredPosition = animated ? _cellStarts[k] : _cellTargets[k];
            rt.gameObject.SetActive(true);
            _filledCount++;
        }

        private void DeactivateTopCell()
        {
            _filledCount--;
            _cells[_fillOrder[_filledCount]].gameObject.SetActive(false);
        }

        private void AdvanceFalls(float dt)
        {
            for (int i = 0; i < _filledCount; i++)
            {
                int k = _fillOrder[i];
                if (_cellFallT[k] >= fallDuration)
                    continue;
                _cellFallT[k] += dt;
                float p = Mathf.Clamp01(_cellFallT[k] / fallDuration);
                _cells[k].anchoredPosition = Vector2.Lerp(_cellStarts[k], _cellTargets[k], p);
            }
        }

        // A sprite-backed slot shows one of its sprites picked at random per cell (in the sprites' own
        // colours, white tint) — the same per-cell random mix the source intro used across its ten
        // flowers; a slot with no sprites falls back to a solid slot-coloured tile.
        private void SkinCell(Image img, int slot)
        {
            Sprite[] set = SpritesForSlot(slot);
            Sprite sprite = (set != null && set.Length > 0) ? set[Random.Range(0, set.Length)] : null;
            img.sprite = sprite;
            img.preserveAspect = sprite != null;
            img.color = sprite != null ? Color.white : ColorForSlot(slot);
        }

        // The slot's loaded sprite set: rainSprites if non-empty, else the single rainSprite, else none.
        // Cached per slot (misses and bad paths included) so config lookups don't hit Resources per cell.
        private Sprite[] SpritesForSlot(int slot)
        {
            if (_config == null || slot < 0 || slot >= _config.slots.Count)
                return null;
            if (_slotSpriteCache.TryGetValue(slot, out Sprite[] cached))
                return cached;

            LauncherSlot s = _config.slots[slot];
            var loaded = new List<Sprite>();
            if (s.rainSprites != null && s.rainSprites.Length > 0)
            {
                foreach (string path in s.rainSprites)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    Sprite sprite = Resources.Load<Sprite>(path);
                    if (sprite != null) loaded.Add(sprite);
                }
            }
            else if (!string.IsNullOrEmpty(s.rainSprite))
            {
                Sprite sprite = Resources.Load<Sprite>(s.rainSprite);
                if (sprite != null) loaded.Add(sprite);
            }

            Sprite[] set = loaded.Count > 0 ? loaded.ToArray() : null;
            _slotSpriteCache[slot] = set;
            return set;
        }

        private void SetBarFill(float charge01)
        {
            if (_barFill == null) return;
            float charge = Mathf.Clamp01(charge01);

            // Idle keeps the attract video CLEAN: the whole bar (frame, track and fill) only exists while
            // charging. Toggle BEFORE measuring — an inactive rect has no dependable width to scale against.
            bool visible = charge > 0f;
            if (_barRoot != null && _barRoot.gameObject.activeSelf != visible)
                _barRoot.gameObject.SetActive(visible);
            if (_barFill.gameObject.activeSelf != visible)
                _barFill.gameObject.SetActive(visible);

            // The track's width is read live rather than hardcoded: the bar is anchored to the attract
            // reel's rect, which is letterboxed to the clip's aspect, so its true width depends on the
            // screen it lands on.
            float track = _barTrack != null ? _barTrack.rect.width : 0f;
            _barFill.sizeDelta = new Vector2(charge * track, 0f);
        }

        private void ClearFlowers()
        {
            for (int i = 0; i < _filledCount; i++)
                _cells[_fillOrder[i]].gameObject.SetActive(false);
            _filledCount = 0;
        }

        private Color ColorForSlot(int slot)
        {
            if (slot < 0) return Color.white;
            return SlotColors[slot % SlotColors.Length];
        }

        private void Build(LauncherConfig config)
        {
            _config = config;
            LaunchTuning tuning = config.TuningOrDefault();
            _sampler = new LaunchInputSampler(config.slots, tuning);
            _machine = new LaunchChargeMachine(tuning);
            _engaged = new bool[config.slots.Count];
            BuildView();
        }

        private void BuildView()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGO = new GameObject("HoldToLaunchCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10; // above the menu
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);

            // Flower-pile layer sits behind the bar/overlay.
            var flowerGO = new GameObject("FlowerRoot", typeof(RectTransform));
            flowerGO.transform.SetParent(_canvas.transform, false);
            _flowerRoot = flowerGO.GetComponent<RectTransform>();
            _flowerRoot.anchorMin = Vector2.zero;
            _flowerRoot.anchorMax = Vector2.one;
            _flowerRoot.offsetMin = Vector2.zero;
            _flowerRoot.offsetMax = Vector2.zero;

            BuildGrid();
            BuildChargeBar();

            // No hint label: the attract screen carries NO text (founder's rule) — the themed rain and
            // the growing bar are the feedback. The only allowed text is the "СКОРО" overlay below.
            _comingSoon = MakeLabel(_canvas.transform, "ComingSoon", "",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(1600f, 120f),
                84, FontStyle.Bold, AttractZones.ChargeColor);
            _comingSoon.gameObject.SetActive(false);

            SetBarFill(0f);
        }

        /// <summary>
        /// Hang the charge bar on the attract reel's own rect. Idempotent, and safe to call before OR
        /// after this controller has built its view (<see cref="HubMenuController"/> creates the reel in
        /// its own Start, and Unity does not order Starts).
        ///
        /// The bar has to live on the REEL's rect, not on this controller's full-screen canvas, because it
        /// is now positioned in clip-row space directly under the launcher's title — and the reel is
        /// letterboxed to the clip's aspect. Anchored to the screen instead, the bar would drift away from
        /// the title the moment the surface is not exactly 16:9.
        /// </summary>
        public void AttachChargeBarTo(AttractVideoScreen video)
        {
            _attractVideo = video;
            ApplyChargeBarParent();
        }

        private void ApplyChargeBarParent()
        {
            if (_attractVideo == null || _barRoot == null) return;
            _attractVideo.EnsureBuilt();
            if (_attractVideo.Screen == null) return;
            // Added last, so it draws over the reel, over the veil that fades the reel out while charging,
            // and over the zone mask. The anchors are normalised, so re-parenting keeps the geometry.
            _barRoot.SetParent(_attractVideo.Screen.transform, false);
            _barRoot.SetAsLastSibling();
        }

        // The charge bar (founder, 2026-08-05: «полосу прогрузки хочется дизайн сделать получше и единого
        // цвета» + «полосу прогрузки вставлять под название»). It moved out of the bottom of the screen
        // into the band the attract screen reclaims from the clip, right under the title — so the two read
        // as one block: the game's name, and how far it is from starting.
        //
        // The shape is drawn in the reel's own idiom — hard pixel edges, no rounding, no gradient: a thin
        // amber frame, a near-black slot inside it, and the amber charge cut into notches by gaps in the
        // slot's own colour. An empty bar therefore still reads as the same amber object rather than as a
        // black hole on the reel, and a filling one ticks up in discrete steps like a cabinet's own meter.
        private void BuildChargeBar()
        {
            float halfWidth = AttractZones.ChargeBarWidth / 2f / AttractZones.ClipWidth;

            var rootGO = new GameObject("ChargeBar", typeof(RectTransform));
            rootGO.transform.SetParent(_canvas.transform, false); // re-parented onto the reel when attached
            _barRoot = rootGO.GetComponent<RectTransform>();
            _barRoot.anchorMin = new Vector2(0.5f - halfWidth,
                AttractZones.BottomFraction(AttractZones.ChargeBandBottom));
            _barRoot.anchorMax = new Vector2(0.5f + halfWidth,
                AttractZones.TopFraction(AttractZones.ChargeBandTop));
            _barRoot.offsetMin = Vector2.zero;
            _barRoot.offsetMax = Vector2.zero;

            StretchedImage(_barRoot, "ChargeBarFrame", 0f, AttractZones.ChargeFrameColor);
            _barTrack = StretchedImage(_barRoot, "ChargeBarTrack",
                AttractZones.ChargeBarBorder, AttractZones.ChargeTrackColor);

            var fillGO = new GameObject("ChargeBarFill", typeof(RectTransform), typeof(Image));
            fillGO.transform.SetParent(_barTrack, false);
            _barFill = fillGO.GetComponent<RectTransform>();
            _barFill.anchorMin = new Vector2(0f, 0f);
            _barFill.anchorMax = new Vector2(0f, 1f); // full track height, width driven by the charge
            _barFill.pivot = new Vector2(0f, 0.5f);   // grows from the left edge
            _barFill.anchoredPosition = Vector2.zero;
            _barFill.sizeDelta = Vector2.zero;
            var fillImg = fillGO.GetComponent<Image>();
            fillImg.color = AttractZones.ChargeColor;
            fillImg.raycastTarget = false;

            // Notches: fixed gaps in the TRACK's colour, drawn over the fill. Invisible on an empty bar
            // (slot colour on slot colour) and cutting the amber into steps as it grows.
            var notch = AttractZones.ChargeTrackColor;
            notch.a = 1f;
            for (int i = 1; i < AttractZones.ChargeBarSegments; i++)
            {
                float f = (float)i / AttractZones.ChargeBarSegments;
                var go = new GameObject("Notch", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_barTrack, false);
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

            ApplyChargeBarParent(); // no-op unless the reel was wired in before this build ran
        }

        // A full-rect Image inside <paramref name="parent"/>, inset by <paramref name="inset"/> on all sides.
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

        // Build the full screen-covering grid once, all cells inactive, and precompute the bottom-row-first
        // (shuffled within a row) fill order — the same fill order the source animation uses so the pile
        // grows from the ground up rather than sprinkling in randomly. Geometry is the source's verbatim:
        // the grid is tiled a little past the reference screen's edges (one extra column/row) so full
        // coverage doesn't depend on exact rounding, and every cell sits a random jitter off its exact
        // grid point (fixed at build time; the fall targets it, so re-fills land identically).
        private void BuildGrid()
        {
            float cell = Mathf.Max(1f, cellSize);
            _gridColumns = Mathf.CeilToInt(RefWidth / cell) + 1;
            _gridRows = Mathf.CeilToInt(RefHeight / cell) + 1;
            int cols = _gridColumns;
            int rows = _gridRows;
            int count = cols * rows;

            _cells = new RectTransform[count];
            _cellImages = new Image[count];
            _cellTargets = new Vector2[count];
            _cellStarts = new Vector2[count];
            _cellFallT = new float[count];

            float gridWidth = cols * cell;
            float gridHeight = rows * cell;
            float startX = -gridWidth / 2f + cell / 2f;
            float startY = -gridHeight / 2f + cell / 2f;
            float jitter = cell * jitterFraction;
            // The source animation's fixed sprite size is now the FLOOR of the per-spawn size roll (see
            // ActivateCell); cells are built at it and re-sized as they are activated.
            _cellBaseSize = cell * sizeShrink;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int index = r * cols + c;
                    var go = new GameObject("Flower", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(_flowerRoot, false);
                    var rt = go.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(_cellBaseSize, _cellBaseSize);
                    // r = 0 is the bottom row.
                    Vector2 jitterOffset = new Vector2(
                        Random.Range(-jitter, jitter),
                        Random.Range(-jitter, jitter));
                    _cellTargets[index] = new Vector2(startX + c * cell, startY + r * cell) + jitterOffset;
                    rt.anchoredPosition = _cellTargets[index];
                    go.SetActive(false);
                    _cells[index] = rt;
                    _cellImages[index] = go.GetComponent<Image>();
                }
            }

            // Fill order: whole bottom row first, then the next row up, shuffled within each row.
            _fillOrder = new int[count];
            int w = 0;
            var rowIndices = new List<int>(cols);
            for (int r = 0; r < rows; r++)
            {
                rowIndices.Clear();
                for (int c = 0; c < cols; c++)
                    rowIndices.Add(r * cols + c);
                for (int i = rowIndices.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (rowIndices[i], rowIndices[j]) = (rowIndices[j], rowIndices[i]);
                }
                for (int i = 0; i < rowIndices.Count; i++)
                    _fillOrder[w++] = rowIndices[i];
            }
        }

        private Text MakeLabel(Transform parent, string name, string text, Vector2 anchor,
            Vector2 anchoredPos, Vector2 size, int fontSize, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var label = go.GetComponent<Text>();
            label.font = _font;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = text;
            return label;
        }

        // -------- editor/screenshot posing (renders the real widgets in a chosen state) --------

        /// <summary>
        /// Build (if needed) and pose the view at a fixed charge for a screenshot: fills the bar and piles
        /// the grid with round(charge · capacity) slot-skinned sprites, resting in place (post-fall), and
        /// optionally shows the "СКОРО" overlay. Uses the same widgets the live loop drives.
        /// </summary>
        public void EditorPose(LauncherConfig config, int slotIndex, float charge, bool showComingSoon)
        {
            if (_machine == null) Build(config);

            // Canvases size their children on the next layout pass; a pose renders immediately, so the
            // track has to be measurable before the fill is scaled against it.
            Canvas.ForceUpdateCanvases();
            SetBarFill(charge);

            ClearFlowers();
            _viewSlot = slotIndex;
            int target = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(charge) * _cells.Length), 0, _cells.Length);
            while (_filledCount < target)
                ActivateCell(_filledCount, slotIndex, animated: false);

            if (showComingSoon && slotIndex >= 0 && slotIndex < config.slots.Count)
            {
                _comingSoon.text = $"СКОРО — {config.slots[slotIndex].displayName}";
                _comingSoon.color = AttractZones.ChargeColor;
                _comingSoon.gameObject.SetActive(true);
            }
        }
    }
}
