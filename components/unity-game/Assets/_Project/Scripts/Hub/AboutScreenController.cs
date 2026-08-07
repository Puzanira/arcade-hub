using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using AiGameStudio.ArcadeControls;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The «История проекта» screen (founder, 2026-08-07: «на кнопку меню… отдельный проект… история о
    /// проекте, как мы его собирали… скроллящийся джойстиком текст с картинками. Шрифт такой же, как на
    /// лаунчере»).
    ///
    /// From the attract screen, a press of the MENU button lifts a full-screen document over the reel:
    /// the video fades out and freezes, the launcher's own title/ticker/charge bar step aside, nothing
    /// can be launched from under it, and the story of the cabinet scrolls under the joystick. A second
    /// MENU press — or a minute of nobody touching anything — puts the reel back, sound and all.
    ///
    /// The two screens never overlap while they are changing places (founder, 2026-08-08: «плохо
    /// наслаивается текст поверх — пока происходит фейд видео, уже виден текст, грязно. Нужно: СНАЧАЛА
    /// выцветает видео, ПОТОМ на него вцветает текст»). The hand-over is strictly sequential in BOTH
    /// directions:
    ///
    /// <code>
    ///   opening:  MENU ─► reel fades out (1.2 s) ─► veil at max ─► document fades in (0.45 s)
    ///   closing:  MENU ─► document fades out (0.3 s) ─► fully gone ─► reel fades back in (0.8 s)
    /// </code>
    ///
    /// The hand-over points are read off the ACTUAL states — <see cref="AttractVideoScreen.FadeAmount"/>
    /// on the way in, this screen's own alpha on the way out — never off copies of the two durations.
    /// Retuning <see cref="AttractZones.ReelFadeOutSeconds"/> or <see cref="AboutLayout.FadeInSeconds"/>
    /// therefore cannot desynchronise them into the smeared cross-fade the founder rejected.
    ///
    /// Three things are deliberately NOT owned here:
    /// • the reel's pause-with-a-fade is the one <see cref="AttractVideoScreen.TickEngagement"/> already
    ///   performs for a charging control — this screen only raises the same flag
    ///   (<see cref="IReelSuspender"/>), so there is exactly one fade-and-freeze path in the launcher;
    /// • the CONTENT is data (<c>StreamingAssets/About/content.json</c> + its photos), loaded on every
    ///   entry, so the founder's real text and process photos replace the placeholders by editing a
    ///   folder;
    /// • the open/close and scroll rules are pure machines (<see cref="AboutScreenMachine"/>,
    ///   <see cref="AboutScrollMachine"/>) — this class is the Unity adapter and the view.
    ///
    /// The layout here is a SCAFFOLD, sized to be readable on the cabinet (a ~1200 px column in the
    /// reel's own #262626, Russo One for display and Arimo for body — the launcher's two faces) and
    /// meant to be replaced by the founder's mock-up. Every number it uses lives in
    /// <see cref="AboutLayout"/>.
    /// </summary>
    [AddComponentMenu("Arcade Hub/About Screen")]
    [DisallowMultipleComponent]
    public sealed class AboutScreenController : MonoBehaviour, IReelSuspender
    {
        /// <summary>Above the hold-to-launch canvas (10), which is above the reel's (0).</summary>
        public const int CanvasSortingOrder = 100;

        /// <summary>When false, <see cref="Update"/> does not advance — tests drive <see cref="Tick"/> with a fixed dt.</summary>
        public bool AutoTick = true;

        // One built block of the document, remembered so the stack can be re-measured after the dynamic
        // font atlas fills in (the same warm-up the attract ticker has to survive).
        private sealed class BlockView
        {
            public RectTransform Rect;
            public Text Label;        // null for photos
            public float FixedHeight; // photos only
            public float SpaceBefore;
            public AboutBlockType Kind;
        }

        private readonly AboutScreenMachine _machine = new AboutScreenMachine();
        private readonly AboutScrollMachine _scroll = new AboutScrollMachine();
        private readonly List<BlockView> _blocks = new List<BlockView>();
        private readonly List<Texture2D> _photoTextures = new List<Texture2D>();

        private AttractOverlay _overlay;
        private AttractVideoScreen _video;
        private HoldToLaunchController _holdToLaunch;

        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _viewport;
        private RectTransform _document;
        private RectTransform _scrollTrack;
        private RectTransform _scrollThumb;
        private Text _hint;
        private Font _displayFont;
        private Font _bodyFont;

        private AboutContent _content;
        private float _alpha;
        private float _documentHeight;
        private int _warmupFrames;

        // -------- read surface (tests / tooling) --------

        /// <summary>True while the document is up (from the MENU press, before it has faded in).</summary>
        public bool IsOpen => _machine.IsOpen;

        /// <summary>
        /// The reel stays suspended for as long as the document is up OR still leaving the screen. That
        /// tail is what makes the CLOSE sequential: the reel may not start coming back underneath a
        /// document that is still visible, however faintly.
        /// </summary>
        public bool SuspendsReel => _machine.IsOpen || _alpha > 0f;

        /// <summary>
        /// True once the reel has FINISHED leaving — the veil is at its maximum and the picture is
        /// frozen. The document waits for this before it starts arriving, so the two never overlap.
        ///
        /// Read off the reel's real fade rather than off a copy of its duration; a scene with no reel at
        /// all (or with nobody driving it — the overlay is the reel's clock) has nothing to wait for and
        /// reports true, so a bare document still opens.
        /// </summary>
        public bool ReelHasLeft => _video == null || _overlay == null || _video.FadeAmount >= 1f;

        /// <summary>
        /// True in the gap between the MENU press and the document starting to arrive — the reel is
        /// still fading out and the screen must show NO text (test seam for the sequence).
        /// </summary>
        public bool IsWaitingForReel => _machine.IsOpen && !ReelHasLeft;

        /// <summary>The document's fade, 0 (gone) … 1 (fully opaque).</summary>
        public float DocumentAlpha => _alpha;

        /// <summary>The screen's canvas (test seam: sorting order, visibility).</summary>
        public Canvas Canvas => _canvas;

        /// <summary>The clipping window the document scrolls inside (test seam: the reading column).</summary>
        public RectTransform Viewport => _viewport;

        /// <summary>The scrolling document root — its height is the whole story's length.</summary>
        public RectTransform Document => _document;

        /// <summary>The loaded content (null until the screen is first opened).</summary>
        public AboutContent Content => _content;

        /// <summary>How far down the document we are, in reference px.</summary>
        public float ScrollPosition => _scroll.Position;

        /// <summary>The furthest the document can scroll (0 when it fits on one screen).</summary>
        public float MaxScroll => _scroll.MaxPosition;

        /// <summary>Total document height including its lead-in/lead-out air, in reference px.</summary>
        public float DocumentHeight => _documentHeight;

        /// <summary>Seconds of untouched cabinet so far (drives the automatic return).</summary>
        public float IdleSeconds => _machine.IdleSeconds;

        /// <summary>
        /// [tune] Seconds of no input before the document takes itself back to the attract reel
        /// (default <see cref="AboutLayout.IdleReturnSeconds"/>).
        /// </summary>
        public float IdleReturnSeconds
        {
            get => _machine.IdleTimeout;
            set => _machine.IdleTimeout = value;
        }

        /// <summary>The number of blocks actually drawn (unknown types are skipped).</summary>
        public int BlockCount => _blocks.Count;

        /// <summary>The controls hint at the bottom edge (test seam).</summary>
        public Text Hint => _hint;

        /// <summary>The scrollbar's thumb — hidden when the document fits on one screen (test seam).</summary>
        public RectTransform ScrollThumb => _scrollThumb;

        /// <summary>Every drawn text block, in document order (test seam: the placeholders are really on screen).</summary>
        public IReadOnlyList<Text> TextBlocks
        {
            get
            {
                var list = new List<Text>();
                foreach (BlockView b in _blocks)
                    if (b.Label != null) list.Add(b.Label);
                return list;
            }
        }

        /// <summary>Every drawn photo's rect, in document order (test seam: width and aspect).</summary>
        public IReadOnlyList<RectTransform> PhotoBlocks
        {
            get
            {
                var list = new List<RectTransform>();
                foreach (BlockView b in _blocks)
                    if (b.Kind == AboutBlockType.Photo) list.Add(b.Rect);
                return list;
            }
        }

        // -------- wiring --------

        /// <summary>
        /// Bind the screen to the reel it hands the screen over with, to the attract chrome it has to
        /// push aside, and to the launcher it has to mute while it is up. All three may be null (a bare
        /// document still opens and closes — it simply has nothing to wait for).
        /// </summary>
        public void Bind(AttractVideoScreen video, AttractOverlay overlay, HoldToLaunchController holdToLaunch)
        {
            _video = video;
            _overlay = overlay;
            _holdToLaunch = holdToLaunch;
            if (_overlay != null) _overlay.BindSuspender(this);
            EnsureBuilt();
        }

        private void Start()
        {
            EnsureBuilt();
            if (_video == null) _video = FindAnyObjectByType<AttractVideoScreen>();
            if (_overlay == null) _overlay = FindAnyObjectByType<AttractOverlay>();
            if (_holdToLaunch == null) _holdToLaunch = FindAnyObjectByType<HoldToLaunchController>();
            if (_overlay != null) _overlay.BindSuspender(this);
        }

        private void Update()
        {
            if (AutoTick) Tick(Time.deltaTime);
        }

        // -------- core loop --------

        /// <summary>Advance one frame: read MENU/joystick, open or close, scroll, fade.</summary>
        public void Tick(float dt)
        {
            if (dt < 0f) dt = 0f;
            EnsureBuilt();

            bool menuHeld = ArcadeInput.MenuButton.IsHeld;
            Vector2 stick = ArcadeInput.Joystick.Vector;

            AboutScreenEvent change = _machine.Tick(menuHeld, HasOtherInput(stick), dt);
            if (change == AboutScreenEvent.Opened) OpenDocument();
            else if (change == AboutScreenEvent.Closed || change == AboutScreenEvent.ClosedByTimeout) CloseDocument();

            // "The screen belongs to the document" — true from the MENU press until the document has
            // completely left again, which is a little longer than IsOpen. Nothing may charge from under
            // it, and the launcher's own title/ticker may not reappear underneath a document that is
            // still fading out. Raised EVERY frame (not only on the edge) so a controller wired in late,
            // or a scene re-entered, can never be left armed.
            bool occupied = SuspendsReel;
            if (_holdToLaunch != null) _holdToLaunch.Suspended = occupied;
            if (_overlay != null) _overlay.SetChromeHidden(occupied);

            TickFade(dt);

            if (_machine.IsOpen)
            {
                if (_warmupFrames > 0)
                {
                    // The dynamic font atlas fills in over the first frames, so a height measured at
                    // build time can still shift under us — re-stack until it has settled.
                    _warmupFrames--;
                    Relayout();
                }
                _scroll.Tick(stick.y, dt);
                ApplyScroll();
            }
        }

        // "Somebody is touching the cabinet" — anything but the MENU button, which the machine reads
        // separately. Height sensors and the crank use the same thresholds the launcher engages on, so
        // a hand resting nowhere near the cabinet cannot hold the document open forever.
        private bool HasOtherInput(Vector2 stick)
        {
            if (!Mathf.Approximately(_scroll.Deflection(stick.y), 0f)) return true;
            if (!Mathf.Approximately(_scroll.Deflection(stick.x), 0f)) return true;
            if (ArcadeInput.RedButton.IsHeld || ArcadeInput.GreenButton.IsHeld || ArcadeInput.BangButton.IsHeld)
                return true;
            if (Mathf.Abs(ArcadeInput.Crank.DeltaDegrees) > 0.5f) return true;
            if (ArcadeInput.HeightA.Value > 0.5f || ArcadeInput.HeightB.Value > 0.5f) return true;
            return false;
        }

        private void TickFade(float dt)
        {
            // The document arrives ONLY once the reel has finished leaving. While the veil is still on
            // its way the target stays 0, so the screen shows the reel dimming and nothing else — no
            // text creeping up through a half-faded picture (founder, 2026-08-08: «грязно»).
            bool arriving = _machine.IsOpen && ReelHasLeft;
            float target = arriving ? 1f : 0f;
            float seconds = arriving ? AboutLayout.FadeInSeconds : AboutLayout.FadeOutSeconds;
            _alpha = Mathf.MoveTowards(_alpha, target, dt / Mathf.Max(0.01f, seconds));
            if (_group != null) _group.alpha = _alpha;

            // Fully gone: stop drawing AND drop the document, so a re-entry re-reads content.json and the
            // founder's edits show up without restarting the cabinet.
            if (!_machine.IsOpen && _alpha <= 0f && _canvas != null && _canvas.gameObject.activeSelf)
            {
                _canvas.gameObject.SetActive(false);
                ClearDocument();
            }
        }

        private void OpenDocument()
        {
            if (_canvas == null) return;
            _canvas.gameObject.SetActive(true);
            _content = AboutContentLoader.LoadFromStreamingAssets();
            BuildDocument(_content);
            _scroll.Reset();
            ApplyScroll();
            _warmupFrames = 5;
        }

        private void CloseDocument()
        {
            _scroll.Reset();
            // The canvas itself stays up until the fade has run out (see TickFade).
        }

        // -------- document view --------

        /// <summary>Build the (empty) screen once. Idempotent; safe before <see cref="Start"/>.</summary>
        public void EnsureBuilt()
        {
            if (_canvas != null) return;

            _displayFont = Resources.Load<Font>("Fonts/RussoOne")
                           ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _bodyFont = Resources.Load<Font>("Fonts/Arimo-Regular") ?? _displayFont;

            var canvasGO = new GameObject("AboutCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CanvasSortingOrder; // over the reel AND over the hold-to-launch stack
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(AboutLayout.RefWidth, AboutLayout.RefHeight);
            _group = canvasGO.GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            // An OPAQUE field in the reel's own tone: the document is a screen of its own, not a caption
            // laid over a video, and the frozen reel must not read through the text.
            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(_canvas.transform, false);
            Stretch(bg.GetComponent<RectTransform>());
            var bgImage = bg.GetComponent<Image>();
            bgImage.color = AboutLayout.Background;
            bgImage.raycastTarget = false;

            BuildViewport();
            BuildScrollBar();
            BuildHint();

            _canvas.gameObject.SetActive(false);
        }

        private void BuildViewport()
        {
            var go = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            go.transform.SetParent(_canvas.transform, false);
            _viewport = go.GetComponent<RectTransform>();
            _viewport.anchorMin = new Vector2(0.5f, 0f);
            _viewport.anchorMax = new Vector2(0.5f, 1f);
            _viewport.pivot = new Vector2(0.5f, 0.5f);
            _viewport.sizeDelta = new Vector2(AboutLayout.ColumnWidth,
                -(AboutLayout.ViewportTopMargin + AboutLayout.ViewportBottomMargin));
            _viewport.anchoredPosition =
                new Vector2(0f, (AboutLayout.ViewportBottomMargin - AboutLayout.ViewportTopMargin) / 2f);

            var docGO = new GameObject("Document", typeof(RectTransform));
            docGO.transform.SetParent(_viewport, false);
            _document = docGO.GetComponent<RectTransform>();
            _document.anchorMin = new Vector2(0.5f, 1f);
            _document.anchorMax = new Vector2(0.5f, 1f);
            _document.pivot = new Vector2(0.5f, 1f); // grows downward from the viewport's top edge
            _document.sizeDelta = new Vector2(AboutLayout.ColumnWidth, 0f);
            _document.anchoredPosition = Vector2.zero;
        }

        private void BuildScrollBar()
        {
            var trackGO = new GameObject("ScrollTrack", typeof(RectTransform), typeof(Image));
            trackGO.transform.SetParent(_canvas.transform, false);
            _scrollTrack = trackGO.GetComponent<RectTransform>();
            _scrollTrack.anchorMin = new Vector2(0.5f, 0f);
            _scrollTrack.anchorMax = new Vector2(0.5f, 1f);
            _scrollTrack.pivot = new Vector2(0.5f, 0.5f);
            _scrollTrack.sizeDelta = new Vector2(AboutLayout.ScrollBarWidth,
                -(AboutLayout.ViewportTopMargin + AboutLayout.ViewportBottomMargin));
            _scrollTrack.anchoredPosition = new Vector2(
                AboutLayout.ColumnWidth / 2f + AboutLayout.ScrollBarGap,
                (AboutLayout.ViewportBottomMargin - AboutLayout.ViewportTopMargin) / 2f);
            var trackImage = trackGO.GetComponent<Image>();
            trackImage.color = AboutLayout.ScrollBarTrackColor;
            trackImage.raycastTarget = false;

            var thumbGO = new GameObject("ScrollThumb", typeof(RectTransform), typeof(Image));
            thumbGO.transform.SetParent(_scrollTrack, false);
            _scrollThumb = thumbGO.GetComponent<RectTransform>();
            _scrollThumb.anchorMin = new Vector2(0f, 1f);
            _scrollThumb.anchorMax = new Vector2(1f, 1f);
            _scrollThumb.pivot = new Vector2(0.5f, 1f);
            _scrollThumb.sizeDelta = new Vector2(0f, AboutLayout.ScrollBarMinThumb);
            _scrollThumb.anchoredPosition = Vector2.zero;
            var thumbImage = thumbGO.GetComponent<Image>();
            thumbImage.color = AboutLayout.ScrollBarThumbColor;
            thumbImage.raycastTarget = false;
        }

        private void BuildHint()
        {
            var go = new GameObject("Hint", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(AboutLayout.RefWidth, AboutLayout.ViewportBottomMargin);
            rt.anchoredPosition = new Vector2(0f, 18f);

            _hint = go.GetComponent<Text>();
            _hint.font = _bodyFont;
            _hint.fontSize = AboutLayout.HintFontSize;
            _hint.color = AboutLayout.HintColor;
            _hint.alignment = TextAnchor.LowerCenter;
            _hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hint.verticalOverflow = VerticalWrapMode.Overflow;
            _hint.raycastTarget = false;
            _hint.text = AboutLayout.HintText;
        }

        private void BuildDocument(AboutContent content)
        {
            ClearDocument();
            if (content == null) return;

            foreach (AboutBlock block in content.Blocks)
            {
                if (block == null) continue;
                switch (block.Kind)
                {
                    case AboutBlockType.Title:
                        AddText(block.text, _displayFont, AboutLayout.TitleFontSize, AboutLayout.TitleColor,
                            TextAnchor.UpperCenter, AboutLayout.DisplayLineSpacing,
                            AboutLayout.SpaceBeforeTitle, AboutBlockType.Title);
                        break;
                    case AboutBlockType.Heading:
                        AddText(block.text, _displayFont, AboutLayout.HeadingFontSize, AboutLayout.HeadingColor,
                            TextAnchor.UpperLeft, AboutLayout.DisplayLineSpacing,
                            AboutLayout.SpaceBeforeHeading, AboutBlockType.Heading);
                        break;
                    case AboutBlockType.Paragraph:
                        AddText(block.text, _bodyFont, AboutLayout.ParagraphFontSize, AboutLayout.ParagraphColor,
                            TextAnchor.UpperLeft, AboutLayout.ParagraphLineSpacing,
                            AboutLayout.SpaceBeforeParagraph, AboutBlockType.Paragraph);
                        break;
                    case AboutBlockType.Caption:
                        AddText(block.text, _bodyFont, AboutLayout.CaptionFontSize, AboutLayout.CaptionColor,
                            TextAnchor.UpperLeft, AboutLayout.CaptionLineSpacing,
                            AboutLayout.SpaceBeforeCaption, AboutBlockType.Caption);
                        break;
                    case AboutBlockType.Photo:
                        AddPhoto(block.file);
                        break;
                    default:
                        // An unknown type is the founder's typo, not a crash: skip it, say so once.
                        Debug.LogWarning($"[Hub] About document: skipping block of unknown type '{block.type}'.");
                        break;
                }
            }

            Relayout();
        }

        private void AddText(string text, Font font, int fontSize, Color color, TextAnchor align,
            float lineSpacing, float spaceBefore, AboutBlockType kind)
        {
            var go = new GameObject(kind.ToString(), typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_document, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f); // full column width, height driven by the text itself
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 0f);
            rt.anchoredPosition = Vector2.zero;

            var label = go.GetComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.lineSpacing = lineSpacing;
            label.color = color;
            label.alignment = align;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.supportRichText = false;
            label.text = text ?? string.Empty;

            _blocks.Add(new BlockView { Rect = rt, Label = label, SpaceBefore = spaceBefore, Kind = kind });
        }

        // A photo from the document's own folder, drawn at the column width (never upscaled past its own
        // pixels) with its aspect kept. A missing or unreadable file is a warning and a skipped block —
        // the story still reads without it.
        private void AddPhoto(string fileName)
        {
            string path = AboutContentLoader.PhotoPathFor(AboutContentLoader.ContentPath, fileName);
            Texture2D texture = LoadTexture(path);
            if (texture == null) return;

            float width = Mathf.Min(texture.width, AboutLayout.ColumnWidth);
            float height = width * texture.height / Mathf.Max(1f, texture.width);

            var go = new GameObject("Photo", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(_document, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = Vector2.zero;

            var image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;

            _blocks.Add(new BlockView
            {
                Rect = rt,
                FixedHeight = height,
                SpaceBefore = AboutLayout.SpaceBeforePhoto,
                Kind = AboutBlockType.Photo,
            });
        }

        private Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning($"[Hub] About document: photo '{path}' not found — block skipped.");
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Debug.LogWarning($"[Hub] About document: photo '{path}' is not a readable image — block skipped.");
                Destroy(texture);
                return null;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            _photoTextures.Add(texture);
            return texture;
        }

        // Re-stack the whole document top-down. Called on build and again over the first frames of a
        // showing (dynamic font atlases settle late — the same warm-up the attract ticker's period needs).
        private void Relayout()
        {
            float cursor = AboutLayout.ContentLeadIn;
            foreach (BlockView block in _blocks)
            {
                cursor += block.SpaceBefore;
                float height = block.Label != null ? block.Label.preferredHeight : block.FixedHeight;
                block.Rect.sizeDelta = new Vector2(block.Rect.sizeDelta.x, height);
                block.Rect.anchoredPosition = new Vector2(block.Rect.anchoredPosition.x, -cursor);
                cursor += height;
            }
            cursor += AboutLayout.ContentLeadOut;

            _documentHeight = cursor;
            _document.sizeDelta = new Vector2(AboutLayout.ColumnWidth, cursor);
            _scroll.MaxPosition = Mathf.Max(0f, cursor - ViewportHeight);
            ApplyScroll();
        }

        // The reading window's height. A canvas that was activated THIS frame has not been sized yet, so
        // its children's rects can still read as nonsense; fall back to the reference geometry until the
        // real one shows up (the warm-up relayouts then pick up the true value).
        private float ViewportHeight
        {
            get
            {
                float measured = _viewport != null ? _viewport.rect.height : 0f;
                if (measured > 1f) return measured;
                return AboutLayout.RefHeight - AboutLayout.ViewportTopMargin - AboutLayout.ViewportBottomMargin;
            }
        }

        private void ApplyScroll()
        {
            if (_document == null) return;
            // The document hangs from the viewport's top edge and slides UP as the reading position
            // travels down the story.
            _document.anchoredPosition = new Vector2(0f, _scroll.Position);
            UpdateScrollBar();
        }

        private void UpdateScrollBar()
        {
            if (_scrollTrack == null || _scrollThumb == null) return;

            bool scrollable = _scroll.MaxPosition > 1f;
            if (_scrollTrack.gameObject.activeSelf != scrollable)
                _scrollTrack.gameObject.SetActive(scrollable); // a one-screen document shows no bar at all
            if (!scrollable) return;

            float trackHeight = _scrollTrack.rect.height;
            float visible = Mathf.Clamp01(ViewportHeight / Mathf.Max(1f, _documentHeight));
            float thumb = Mathf.Max(AboutLayout.ScrollBarMinThumb, trackHeight * visible);
            float travel = Mathf.Max(0f, trackHeight - thumb);
            float progress = Mathf.Clamp01(_scroll.Position / Mathf.Max(1f, _scroll.MaxPosition));

            _scrollThumb.sizeDelta = new Vector2(0f, thumb);
            _scrollThumb.anchoredPosition = new Vector2(0f, -progress * travel);
        }

        private void ClearDocument()
        {
            foreach (BlockView block in _blocks)
                if (block.Rect != null) Destroy(block.Rect.gameObject);
            _blocks.Clear();

            foreach (Texture2D texture in _photoTextures)
                if (texture != null) Destroy(texture);
            _photoTextures.Clear();

            _documentHeight = 0f;
            if (_document != null) _document.sizeDelta = new Vector2(AboutLayout.ColumnWidth, 0f);
            _scroll.MaxPosition = 0f;
        }

        private void OnDestroy()
        {
            foreach (Texture2D texture in _photoTextures)
                if (texture != null) Destroy(texture);
            _photoTextures.Clear();
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
