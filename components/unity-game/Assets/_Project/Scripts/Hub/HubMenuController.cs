using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using AiGameStudio.ArcadeControls;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The launcher menu (scene 0). Loads the slot list from StreamingAssets, builds a procedural UGUI
    /// list, and drives selection ENTIRELY through <see cref="ArcadeInput"/> — joystick up/down moves the
    /// cursor (with wrap-around), the red button chooses. Choosing an installed slot loads its scene;
    /// choosing a planned slot shows a "coming soon" placeholder. No raw device input lives here.
    /// </summary>
    [AddComponentMenu("Arcade Hub/Hub Menu Controller")]
    [DisallowMultipleComponent]
    public sealed class HubMenuController : MonoBehaviour
    {
        [Tooltip("Joystick vertical magnitude that counts as a navigation push.")]
        [SerializeField] private float navPushThreshold = 0.5f;

        [Tooltip("Joystick vertical magnitude below which a push is considered released (re-arms nav).")]
        [SerializeField] private float navReleaseThreshold = 0.25f;

        private HubMenuModel _model;
        private readonly List<Text> _rows = new List<Text>();
        private Text _placeholder;
        private Font _font;

        // Input edge-detection state (polled, so it survives ArcadeInput re-initialization in tests).
        private bool _navArmed = true;
        private bool _redPrev;

        /// <summary>The row labels, one per slot, in slot order (for tests to assert visibility/content).</summary>
        public IReadOnlyList<Text> Rows => _rows;

        /// <summary>Currently highlighted slot index.</summary>
        public int SelectedIndex => _model?.SelectedIndex ?? 0;

        /// <summary>Number of rows built from the config.</summary>
        public int RowCount => _rows.Count;

        /// <summary>True while the "coming soon" placeholder is on screen (after choosing a planned slot).</summary>
        public bool PlaceholderVisible => _placeholder != null && _placeholder.gameObject.activeSelf;

        /// <summary>Injection hook for tests: build the menu from an explicit config instead of StreamingAssets.</summary>
        public void InitializeWith(LauncherConfig config)
        {
            BuildFrom(config);
        }

        private void Start()
        {
            if (_model == null)
                BuildFrom(LauncherConfigLoader.LoadFromStreamingAssets());
        }

        private void Update()
        {
            if (_model == null) return;

            // Navigation: joystick vertical, one move per push (armed/disarmed by a release threshold).
            float y = ArcadeInput.Joystick.Vector.y;
            if (_navArmed)
            {
                if (y > navPushThreshold) { _model.MoveUp(); _navArmed = false; RefreshHighlight(); }
                else if (y < -navPushThreshold) { _model.MoveDown(); _navArmed = false; RefreshHighlight(); }
            }
            else if (Mathf.Abs(y) < navReleaseThreshold)
            {
                _navArmed = true;
            }

            // Selection: red button rising edge.
            bool red = ArcadeInput.RedButton.IsHeld;
            if (red && !_redPrev) Choose();
            _redPrev = red;
        }

        private void Choose()
        {
            LauncherSlot slot = _model.Selected;
            if (slot == null) return;

            if (slot.IsInstalled)
            {
                SceneManager.LoadScene(slot.entryScene);
                return;
            }

            // Planned slot: surface a clear "coming soon" line instead of loading anything.
            if (_placeholder != null)
            {
                _placeholder.text = $"COMING SOON — {slot.displayName}";
                _placeholder.gameObject.SetActive(true);
            }
        }

        private void BuildFrom(LauncherConfig config)
        {
            _model = new HubMenuModel(config.slots);
            BuildView(config);
            RefreshHighlight();
        }

        private void BuildView(LauncherConfig config)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGO = new GameObject("HubMenuCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            const float rowHeight = 92f;
            const float rowWidth = 1200f;
            const float leftMargin = 120f;
            const float topMargin = 240f;

            CreateLabel(canvas.transform, "HubTitle", "ARCADE CABINET",
                new Vector2(leftMargin, -90f), new Vector2(rowWidth, 70f), 54, FontStyle.Bold,
                Color.white, TextAnchor.MiddleLeft);
            CreateLabel(canvas.transform, "HubSubtitle",
                "Joystick up/down to move  •  RED (1) to select  •  MENU (Esc) exits a game",
                new Vector2(leftMargin, -160f), new Vector2(rowWidth, 40f), 26, FontStyle.Normal,
                new Color(0.7f, 0.7f, 0.75f), TextAnchor.MiddleLeft);

            _rows.Clear();
            for (int i = 0; i < config.slots.Count; i++)
            {
                var row = CreateLabel(canvas.transform, "Slot_" + i, RowText(config.slots[i], false),
                    new Vector2(leftMargin, -(topMargin + i * rowHeight)),
                    new Vector2(rowWidth, rowHeight - 10f), 38, FontStyle.Normal,
                    Color.white, TextAnchor.MiddleLeft);
                _rows.Add(row);
            }

            _placeholder = CreateLabel(canvas.transform, "Placeholder", "",
                new Vector2(leftMargin, -(topMargin + config.slots.Count * rowHeight + 40f)),
                new Vector2(rowWidth, 60f), 36, FontStyle.Bold,
                new Color(1f, 0.85f, 0.2f), TextAnchor.MiddleLeft);
            _placeholder.gameObject.SetActive(false);
        }

        private void RefreshHighlight()
        {
            if (_model == null) return;
            for (int i = 0; i < _rows.Count; i++)
            {
                bool selected = i == _model.SelectedIndex;
                LauncherSlot slot = _model.SlotAt(i);
                _rows[i].text = RowText(slot, selected);
                _rows[i].color = selected
                    ? new Color(1f, 0.95f, 0.4f)
                    : (slot.IsInstalled ? Color.white : new Color(0.55f, 0.55f, 0.6f));
                _rows[i].fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        // Each row's text is one-hot: it contains its own unique display name plus a status word,
        // so a test can pin every row to its slot and a crossed wiring cannot pass silently.
        private static string RowText(LauncherSlot slot, bool selected)
        {
            string cursor = selected ? "> " : "   ";
            string status = slot.IsInstalled ? "[ PLAY ]" : "[ soon ]";
            return $"{cursor}{slot.displayName}   {status}";
        }

        private Text CreateLabel(Transform parent, string name, string text, Vector2 anchoredPos,
            Vector2 size, int fontSize, FontStyle style, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var label = go.GetComponent<Text>();
            label.font = _font;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = text;
            return label;
        }
    }
}
