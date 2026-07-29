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
    public sealed class HoldToLaunchController : MonoBehaviour
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

        /// <summary>When false, <see cref="Update"/> does not advance — tests drive <see cref="Tick"/> with a fixed dt.</summary>
        public bool AutoTick = true;

        private LauncherConfig _config;
        private LaunchInputSampler _sampler;
        private LaunchChargeMachine _machine;
        private bool[] _engaged;

        private Canvas _canvas;
        private RectTransform _barFill;
        private RectTransform _barBg;
        private Text _comingSoon;
        private RectTransform _flowerRoot;
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

        private const float BarWidth = 1200f;
        private const float BarHeight = 44f;
        private const float RefWidth = 1920f;
        private const float RefHeight = 1080f;

        // -------- public read surface (for tests) --------

        public float Charge => _machine?.Charge ?? 0f;
        public int ActiveSlot => _machine?.ActiveSlot ?? -1;

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
                _comingSoon.color = ColorForSlot(slotIndex);
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

            SetBarFill(charge);
            _barFill.GetComponent<Image>().color = ColorForSlot(slot);

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

        // Bring cell fillOrder[slotInFill] into view: skin it for the active slot and start its fall.
        private void ActivateCell(int slotInFill, int slot, bool animated)
        {
            int k = _fillOrder[slotInFill];
            RectTransform rt = _cells[k];
            SkinCell(_cellImages[k], slot);
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
            float w = Mathf.Clamp01(charge01) * BarWidth;
            _barFill.sizeDelta = new Vector2(w, BarHeight);

            // Idle keeps the attract video CLEAN: the whole bar (bg + fill) only exists while charging.
            bool visible = charge01 > 0f;
            if (_barBg != null && _barBg.gameObject.activeSelf != visible)
                _barBg.gameObject.SetActive(visible);
            if (_barFill.gameObject.activeSelf != visible)
                _barFill.gameObject.SetActive(visible);
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

            // Progress bar, bottom-centre.
            _barBg = MakeImage(_canvas.transform, "ChargeBarBg",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 140f), new Vector2(BarWidth, BarHeight),
                new Color(0f, 0f, 0f, 0.55f));

            // Fill grows from the left edge of the bar.
            _barFill = MakeImage(_canvas.transform, "ChargeBarFill",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                new Vector2(-BarWidth / 2f, 140f), new Vector2(0f, BarHeight),
                Color.white);

            // No hint label: the attract screen carries NO text (founder's rule) — the themed rain and
            // the growing bar are the feedback. The only allowed text is the "СКОРО" overlay below.
            _comingSoon = MakeLabel(_canvas.transform, "ComingSoon", "",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(1600f, 120f),
                84, FontStyle.Bold, new Color(1f, 0.85f, 0.2f));
            _comingSoon.gameObject.SetActive(false);

            SetBarFill(0f);
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
            float spriteSize = cell * sizeShrink;

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
                    rt.sizeDelta = new Vector2(spriteSize, spriteSize);
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

        private RectTransform MakeImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            go.GetComponent<Image>().color = color;
            return rt;
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

            SetBarFill(charge);
            _barFill.GetComponent<Image>().color = ColorForSlot(slotIndex);

            ClearFlowers();
            _viewSlot = slotIndex;
            int target = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(charge) * _cells.Length), 0, _cells.Length);
            while (_filledCount < target)
                ActivateCell(_filledCount, slotIndex, animated: false);

            if (showComingSoon && slotIndex >= 0 && slotIndex < config.slots.Count)
            {
                _comingSoon.text = $"СКОРО — {config.slots[slotIndex].displayName}";
                _comingSoon.color = ColorForSlot(slotIndex);
                _comingSoon.gameObject.SetActive(true);
            }
        }
    }
}
