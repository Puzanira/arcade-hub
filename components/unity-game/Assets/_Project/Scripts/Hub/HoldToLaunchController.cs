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
    /// "СКОРО" overlay and resets. A rain of slot-coloured objects fills the screen in proportion to the
    /// charge and clears as it decays.
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

        [Tooltip("Rain objects spawned per second at full charge.")]
        [SerializeField] private float rainSpawnRateAtFull = 45f;
        [Tooltip("Rain fall speed, canvas units per second.")]
        [SerializeField] private float rainFallSpeed = 900f;
        [Tooltip("Hard cap on live rain objects (keeps the still readable and cheap).")]
        [SerializeField] private int rainMax = 90;

        /// <summary>When false, <see cref="Update"/> does not advance — tests drive <see cref="Tick"/> with a fixed dt.</summary>
        public bool AutoTick = true;

        private LauncherConfig _config;
        private LaunchInputSampler _sampler;
        private LaunchChargeMachine _machine;
        private bool[] _engaged;

        private Canvas _canvas;
        private RectTransform _barFill;
        private RectTransform _barBg;
        private Text _hint;
        private Text _comingSoon;
        private RectTransform _rainRoot;
        private readonly List<RectTransform> _rain = new List<RectTransform>();
        private float _spawnAccum;
        private Font _font;

        private const float BarWidth = 1200f;
        private const float BarHeight = 44f;

        // -------- public read surface (for tests) --------

        public float Charge => _machine?.Charge ?? 0f;
        public int ActiveSlot => _machine?.ActiveSlot ?? -1;
        public int RainCount => _rain.Count;
        public RectTransform FirstRainDrop => _rain.Count > 0 ? _rain[0] : null;
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
                    LauncherReturn.ArmFor(HubScenes.HubMenu);
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
            ClearRain();
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
            Color color = ColorForSlot(slot);

            SetBarFill(charge);
            _barFill.GetComponent<Image>().color = color;

            if (charge <= 0f)
            {
                ClearRain();
                return;
            }

            // Spawn rain in proportion to charge; move + cull existing.
            _spawnAccum += rainSpawnRateAtFull * charge * dt;
            while (_spawnAccum >= 1f && _rain.Count < rainMax)
            {
                _spawnAccum -= 1f;
                SpawnRainDrop(color);
            }
            // If charge fell, let the population thin toward the target as drops fall off-screen.
            MoveRain(dt);
        }

        private void SetBarFill(float charge01)
        {
            if (_barFill == null) return;
            float w = Mathf.Clamp01(charge01) * BarWidth;
            _barFill.sizeDelta = new Vector2(w, BarHeight);
        }

        private void SpawnRainDrop(Color color)
        {
            var go = new GameObject("RainDrop", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_rainRoot, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float size = Random.Range(18f, 34f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(Random.Range(-940f, 940f), Random.Range(-10f, 60f));
            var img = go.GetComponent<Image>();
            img.color = new Color(color.r, color.g, color.b, Random.Range(0.55f, 0.95f));
            _rain.Add(rt);
        }

        private void MoveRain(float dt)
        {
            for (int i = _rain.Count - 1; i >= 0; i--)
            {
                RectTransform rt = _rain[i];
                if (rt == null) { _rain.RemoveAt(i); continue; }
                Vector2 p = rt.anchoredPosition;
                p.y -= rainFallSpeed * dt;
                rt.anchoredPosition = p;
                if (p.y < -1130f)
                {
                    Destroy(rt.gameObject);
                    _rain.RemoveAt(i);
                }
            }
        }

        private void ClearRain()
        {
            for (int i = 0; i < _rain.Count; i++)
                if (_rain[i] != null) Destroy(_rain[i].gameObject);
            _rain.Clear();
            _spawnAccum = 0f;
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
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Rain layer sits behind the bar/overlay.
            var rainGO = new GameObject("RainRoot", typeof(RectTransform));
            rainGO.transform.SetParent(_canvas.transform, false);
            _rainRoot = rainGO.GetComponent<RectTransform>();
            _rainRoot.anchorMin = Vector2.zero;
            _rainRoot.anchorMax = Vector2.one;
            _rainRoot.offsetMin = Vector2.zero;
            _rainRoot.offsetMax = Vector2.zero;

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

            _hint = MakeLabel(_canvas.transform, "ChargeHint",
                "Удержи / крути контрол слота, чтобы запустить",
                new Vector2(0.5f, 0f), new Vector2(0f, 200f), new Vector2(1400f, 40f),
                28, FontStyle.Normal, new Color(0.85f, 0.85f, 0.9f));

            _comingSoon = MakeLabel(_canvas.transform, "ComingSoon", "",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(1600f, 120f),
                84, FontStyle.Bold, new Color(1f, 0.85f, 0.2f));
            _comingSoon.gameObject.SetActive(false);

            SetBarFill(0f);
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
        /// Build (if needed) and pose the view at a fixed charge for a screenshot: fills the bar, seeds a
        /// full screen of slot-coloured rain proportional to <paramref name="charge"/>, and optionally
        /// shows the "СКОРО" overlay. Uses the same widgets the live loop drives.
        /// </summary>
        public void EditorPose(LauncherConfig config, int slotIndex, float charge, bool showComingSoon)
        {
            if (_machine == null) Build(config);
            Color color = ColorForSlot(slotIndex);

            SetBarFill(charge);
            _barFill.GetComponent<Image>().color = color;

            ClearRain();
            int drops = Mathf.RoundToInt(Mathf.Clamp01(charge) * rainMax);
            for (int i = 0; i < drops; i++)
            {
                SpawnRainDrop(color);
                // Spread the seeded drops across the whole height so the still reads as ongoing rain.
                RectTransform rt = _rain[_rain.Count - 1];
                rt.anchoredPosition = new Vector2(Random.Range(-940f, 940f), Random.Range(-1080f, 40f));
            }

            if (showComingSoon && slotIndex >= 0 && slotIndex < config.slots.Count)
            {
                _comingSoon.text = $"СКОРО — {config.slots[slotIndex].displayName}";
                _comingSoon.color = color;
                _comingSoon.gameObject.SetActive(true);
            }
        }
    }
}
