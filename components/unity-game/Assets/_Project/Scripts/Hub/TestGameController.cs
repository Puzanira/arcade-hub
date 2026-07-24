using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using AiGameStudio.ArcadeControls;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The built-in test game — the minimal scene that proves the game-scene contract before real games
    /// arrive. It boots by scene load, reads only <see cref="ArcadeInput"/> (a joystick-driven marker to
    /// look alive), and dies cleanly when <see cref="ArcadeInput.MenuButton"/> is pressed, returning to
    /// the launcher. It holds NO persistent/DontDestroyOnLoad state, so every entry is a clean start.
    /// </summary>
    [AddComponentMenu("Arcade Hub/Test Game Controller")]
    [DisallowMultipleComponent]
    public sealed class TestGameController : MonoBehaviour
    {
        /// <summary>
        /// Live count of active instances. Proves the contract in tests: after menu -> game -> menu ->
        /// game it must be exactly 1, never 2 (no leak, no DontDestroyOnLoad survivor).
        /// </summary>
        public static int InstanceCount { get; private set; }

        private RectTransform _player;
        private Font _font;
        private bool _menuPrev;

        private void OnEnable() => InstanceCount++;
        private void OnDisable() => InstanceCount--;

        private void Start()
        {
            BuildView();
        }

        private void Update()
        {
            // Look alive: drive the marker with the joystick (any gameplay control is fair game).
            if (_player != null)
            {
                Vector2 v = ArcadeInput.Joystick.Vector;
                _player.anchoredPosition += v * (400f * Time.deltaTime);
            }

            // Die cleanly on the MENU button rising edge -> back to the launcher.
            bool menu = ArcadeInput.MenuButton.IsHeld;
            if (menu && !_menuPrev) SceneManager.LoadScene(HubScenes.HubMenu);
            _menuPrev = menu;
        }

        private void BuildView()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGO = new GameObject("TestGameCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Full-screen tinted backdrop so the "game" reads as its own screen, not the menu.
            var bgGO = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            bgGO.transform.SetParent(canvas.transform, false);
            var bgRt = bgGO.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            bgGO.GetComponent<Image>().color = new Color(0.06f, 0.12f, 0.2f, 1f);

            CreateLabel(canvas.transform, "TestGameTitle", "TEST GAME",
                new Vector2(0f, -120f), new Vector2(1200f, 90f), 72, FontStyle.Bold,
                new Color(0.4f, 0.9f, 1f));
            CreateLabel(canvas.transform, "TestGameHint",
                "Move the marker with the JOYSTICK  •  press MENU (Esc) to return",
                new Vector2(0f, -230f), new Vector2(1400f, 50f), 30, FontStyle.Normal,
                new Color(0.8f, 0.85f, 0.9f));

            // The joystick-driven marker.
            var playerGO = new GameObject("Player", typeof(RectTransform), typeof(Image));
            playerGO.transform.SetParent(canvas.transform, false);
            _player = playerGO.GetComponent<RectTransform>();
            _player.anchorMin = new Vector2(0.5f, 0.5f);
            _player.anchorMax = new Vector2(0.5f, 0.5f);
            _player.pivot = new Vector2(0.5f, 0.5f);
            _player.sizeDelta = new Vector2(90f, 90f);
            _player.anchoredPosition = Vector2.zero;
            playerGO.GetComponent<Image>().color = new Color(1f, 0.75f, 0.2f, 1f);
        }

        private void CreateLabel(Transform parent, string name, string text, Vector2 anchoredPos,
            Vector2 size, int fontSize, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
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
        }
    }
}
