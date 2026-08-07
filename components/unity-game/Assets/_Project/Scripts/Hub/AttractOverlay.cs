using UnityEngine;
using UnityEngine.UI;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// What the attract screen shows in the band <see cref="AttractVideoScreen"/> reclaimed from the
    /// clip (everything above the hands) — the launcher's own, live replacements for the two things
    /// baked into the video.
    /// </summary>
    public interface IAttractSlotSource
    {
        /// <summary>The slot whose control is being worked right now, or -1 when nobody is touching anything.</summary>
        int ActiveSlot { get; }

        /// <summary>The loaded launcher config — the slot's <c>displayName</c> is the title we show.</summary>
        LauncherConfig Config { get; }
    }

    /// <summary>
    /// Something that needs the attract reel out of the way — currently the «История проекта» document
    /// (<see cref="AboutScreenController"/>), which takes the whole screen.
    ///
    /// The reel's fade-out-and-freeze is NOT re-implemented for it: this flag is OR-ed into the very
    /// signal a charging control raises (<see cref="AttractOverlay.TickReel"/>), so both cases run
    /// through the one <see cref="AttractVideoScreen.TickEngagement"/> path — one fade, one pause, one
    /// return with sound.
    /// </summary>
    public interface IReelSuspender
    {
        /// <summary>True while the reel must stay faded out and frozen.</summary>
        bool SuspendsReel { get; }
    }

    /// <summary>
    /// attract-screen v2 (founder, 2026-08-05). <see cref="AttractVideoScreen"/> hides the clip's baked
    /// credits line and its baked «НАЖМИ ЧТО-НИБУДЬ» title behind a background-tone panel; this
    /// component draws the launcher's own versions of both into that reclaimed band:
    ///
    /// • <b>Credits ticker</b> (top strip) — the seven authors, in the launcher's own text at full
    ///   white. The baked line peaks at luminance 83/255 (a dim grey) and, despite reading like a
    ///   ticker, never moves — it is the same pixels in all 152 frames of the loop. Ours is genuinely
    ///   brighter and genuinely scrolls, right-to-left, seamlessly looped by drawing the string twice
    ///   end-to-end and wrapping the scroll by one period. It is set in a LIGHT face, not the heavy
    ///   display one the title uses (founder, 2026-08-05: «текст титров не жирным — он плохо читается»).
    ///
    /// • <b>Title</b> (the clip's old title zone) — the CABINET's name «6 режимов суеты» while nobody
    ///   is playing, and the NAME OF THE GAME a control launches the moment that control is worked.
    ///   The signal is the very one hold-to-launch charges against
    ///   (<see cref="IAttractSlotSource.ActiveSlot"/>), so the title can never disagree with the bar
    ///   and the themed rain. Changes are a soft alpha fade (out, swap, in) rather than a hard cut.
    ///
    /// This component is also the attract screen's single CLOCK, and — since the founder's title-sync note
    /// (2026-08-08: «анимация смены заголовка должна быть с теми же таймингами, что и фейд… сейчас
    /// название сменяется позже, чем снова вцветает стартовый экран») — the owner of ONE timeline for
    /// both. Every <see cref="Tick"/> reads the engagement signal ONCE
    /// (<see cref="IAttractSlotSource.ActiveSlot"/> ≥ 0, OR-ed with <see cref="IReelSuspender"/>) and hands
    /// the same boolean to the title and to the reel (<see cref="AttractVideoScreen.TickEngagement"/>). So
    /// both start on the same frame, from the same event, in both directions:
    ///
    /// <code>
    ///   grabbed:   reel fades OUT over 1.2 s ─ title becomes the game's name inside that window (0.9 s)
    ///   released:  reel fades IN  over 0.8 s ─ title returns to the cabinet name inside it   (0.6 s)
    /// </code>
    ///
    /// The title's durations are DERIVED from the reel's (<see cref="AttractZones.TitleFadeOutSeconds"/> /
    /// <see cref="AttractZones.TitleFadeInSeconds"/>), never copied, so retuning the reel's fade moves the
    /// title with it. The title keeps its own clock rather than reading
    /// <see cref="AttractVideoScreen.FadeAmount"/> directly for one reason: while one game's control is let
    /// go and another is grabbed straight away, the reel does not move at all (it is already out), and the
    /// title still has to cross-fade from one game's name to the other's instead of hard-cutting.
    ///
    /// The widgets are parented onto the video's own rect with NORMALISED anchors, so they track the
    /// letterboxed video exactly (see <see cref="AttractZones"/> for the measured geometry). The canvas
    /// they live on is the video's (sortingOrder 0), added after the video image — so they sit above
    /// the reel but BELOW the hold-to-launch canvas (sortingOrder 10), which is what keeps the themed
    /// falling objects raining OVER the title, as the founder's mockup shows.
    /// </summary>
    [AddComponentMenu("Arcade Hub/Attract Overlay")]
    [DisallowMultipleComponent]
    public sealed class AttractOverlay : MonoBehaviour
    {
        /// <summary>
        /// The cabinet's own name — what the screen says when nobody is playing. The number counts the
        /// GAMES on the cabinet, so it moves with the layout: relayout-v1 (founder, 2026-08-07) deferred
        /// «Таблетка в космосе» out of the first iteration, and «7 режимов суеты» became «6 режимов суеты».
        /// </summary>
        public const string CabinetName = "6 режимов суеты";

        /// <summary>
        /// The authors, read off the shipped clip's baked credits line (which is legible but dim).
        /// Kept here rather than in launcher-config.json because it credits the CABINET, not a slot.
        /// </summary>
        public const string CreditsLine =
            "Авторы: Ирина Пузанова, Екатерина Гребенюк, Лада Реботунова, Иван Меркурьев, " +
            "Александра Новохацкая, Максим Юнин, Антон Михалёв";

        /// <summary>Tail appended to each repetition so the looped ticker reads as one continuous line.</summary>
        public const string CreditsSeparator = "   ·   ";

        [Header("Credits ticker")]
        [Tooltip("Scroll speed in reference pixels per second, right-to-left.")]
        [SerializeField] private float creditsSpeed = 110f;
        [Tooltip("Ticker font size, in reference pixels (the baked line it replaces is ~24 px tall). A " +
                 "touch larger than the old display-face setting: the light face reads smaller at the " +
                 "same nominal size, and the band is 54 px tall, so it costs nothing.")]
        [SerializeField] private int creditsFontSize = 36;

        [Header("Title")]
        [Tooltip("Largest title size; long game names shrink to fit via best-fit.")]
        [SerializeField] private int titleFontSizeMax = 132;
        [SerializeField] private int titleFontSizeMin = 44;
        // NB: no duration fields here. A title change lasts exactly what the reel's own fade lasts,
        // scaled by AttractZones.TitleChangeFraction — a serialized copy is precisely how the two
        // timelines drifted apart in the first place.

        /// <summary>When false, <see cref="Update"/> does not advance — tests drive <see cref="Tick"/> with a fixed dt.</summary>
        public bool AutoTick = true;

        private IAttractSlotSource _source;
        private IReelSuspender _suspender;
        private AttractVideoScreen _video;
        private bool _chromeHidden;
        private Font _font;
        private Font _creditsFont;

        private RectTransform _creditsViewport;
        private readonly Text[] _creditsCopies = new Text[2];
        private float _creditsScroll;
        private float _creditsPeriod;

        private Text _title;
        private string _shown = CabinetName;
        private string _desired = CabinetName;
        private float _titleAlpha = 1f;

        // -------- read surface (tests / tooling) --------

        /// <summary>The title label (test seam: text, colour, on-screen rect).</summary>
        public Text TitleLabel => _title;

        /// <summary>The words currently ON the title label (mid-fade this is still the OLD text).</summary>
        public string ShownTitle => _shown;

        /// <summary>The words the title is heading towards.</summary>
        public string DesiredTitle => _desired;

        /// <summary>Title alpha, 0..1 — dips through 0 during a change.</summary>
        public float TitleAlpha => _title != null ? _title.color.a : 0f;

        /// <summary>True while a title change is being faded — dipping out, or ramping back in.</summary>
        public bool IsFading => _shown != _desired || _titleAlpha < 1f;

        /// <summary>The clipping viewport of the credits ticker (test seam: it sits in the top band).</summary>
        public RectTransform CreditsViewport => _creditsViewport;

        /// <summary>The two end-to-end copies that make the ticker loop seamlessly.</summary>
        public Text[] CreditsCopies => _creditsCopies;

        /// <summary>The light face the ticker is set in — deliberately NOT the heavy display title face.</summary>
        public Font CreditsFont => _creditsFont;

        /// <summary>The heavy display face the title is set in.</summary>
        public Font TitleFont => _font;

        /// <summary>Current ticker offset, in reference pixels; wraps within one period.</summary>
        public float CreditsScroll => _creditsScroll;

        // -------- wiring --------

        /// <summary>
        /// Build the overlay onto <paramref name="video"/>'s rect and read the active slot from
        /// <paramref name="source"/>. Idempotent; <paramref name="source"/> may be null (the title then
        /// simply stays on the cabinet name).
        /// </summary>
        public void Bind(AttractVideoScreen video, IAttractSlotSource source)
        {
            _source = source;
            if (video != null) _video = video;
            if (_title != null) return; // already built
            if (video == null) return;

            video.EnsureBuilt();
            BuildView((RectTransform)video.Screen.transform);
        }

        /// <summary>
        /// Register whatever may need the reel suspended for reasons other than a charging control (the
        /// about document). Optional; without one the reel only responds to hold-to-launch.
        /// </summary>
        public void BindSuspender(IReelSuspender suspender)
        {
            _suspender = suspender;
        }

        /// <summary>
        /// Take the launcher's own chrome — the title and the credits ticker — off the screen while
        /// something full-screen is up. The charge bar hides itself (it only exists while charging, and
        /// nothing can charge while the document is open).
        /// </summary>
        public void SetChromeHidden(bool hidden)
        {
            if (_chromeHidden == hidden) return;
            _chromeHidden = hidden;
            if (_title != null) _title.gameObject.SetActive(!hidden);
            if (_creditsViewport != null) _creditsViewport.gameObject.SetActive(!hidden);
        }

        /// <summary>True while the title + ticker are hidden behind a full-screen screen (test seam).</summary>
        public bool ChromeHidden => _chromeHidden;

        private void Start()
        {
            if (_title != null) return;
            var video = GetComponentInParent<HubMenuController>() != null
                ? GetComponentInParent<HubMenuController>().Video
                : FindAnyObjectByType<AttractVideoScreen>();
            Bind(video, _source ?? FindAnyObjectByType<HoldToLaunchController>());
        }

        private void Update()
        {
            if (AutoTick) Tick(Time.deltaTime);
        }

        // -------- core loop --------

        /// <summary>
        /// Advance one frame: scroll the ticker, and drive the title and the reel off ONE reading of the
        /// engagement signal — see the class summary. The two are handed the same boolean in the same
        /// frame, which is what keeps the picture and the words on a single timeline.
        /// </summary>
        public void Tick(float dt)
        {
            if (dt < 0f) dt = 0f;

            // Two reasons to put the reel away, ONE mechanism: a control being charged, or a full-screen
            // screen over the launcher (the about document). Read ONCE, here, so the title cannot be
            // answering a slightly different question than the picture is.
            bool charging = _source != null && _source.ActiveSlot >= 0;
            bool suspended = _suspender != null && _suspender.SuspendsReel;
            bool engaged = charging || suspended;

            TickCredits(dt);
            TickTitle(engaged, dt);
            TickReel(engaged, dt);
        }

        // The reel fades out and freezes while a control is being charged, and comes back when it is
        // released. Driven from HERE, off the very signal the title runs on one line above, so the picture
        // leaving, the title becoming the game's name and the bar filling are one gesture — and the reel
        // returns on exactly the event that sends the title back to the cabinet name.
        private void TickReel(bool engaged, float dt)
        {
            if (_video == null) return;
            // Both cases fade it out over the same 1.2 s and freeze it at the bottom of that fade; both
            // bring it back, with sound, the same way.
            _video.TickEngagement(engaged, dt);
        }

        private void TickCredits(float dt)
        {
            if (_creditsCopies[0] == null) return;

            // The loop's period is the rendered width of one repetition. It is re-read every frame
            // rather than latched once: with a DYNAMIC font, preferredWidth shifts slightly as the font
            // atlas fills in the glyphs over the first frames, and a stale period would leave the two
            // copies fractionally apart — exactly the seam this design exists to avoid.
            float measured = _creditsCopies[0].preferredWidth;
            if (measured > 1f) _creditsPeriod = measured;
            if (_creditsPeriod <= 1f) return;

            _creditsScroll -= creditsSpeed * dt;
            // Keep the offset inside (-period, 0] — the second loop also re-seats it if the period just
            // shrank under our feet.
            while (_creditsScroll <= -_creditsPeriod)
                _creditsScroll += _creditsPeriod;
            while (_creditsScroll > 0f)
                _creditsScroll -= _creditsPeriod;

            for (int i = 0; i < _creditsCopies.Length; i++)
            {
                var rt = (RectTransform)_creditsCopies[i].transform;
                // Keep the rect as wide as the glyphs actually are: a zero-width rect makes the label
                // invisible to anything that reasons about layout rather than about the drawn text.
                rt.sizeDelta = new Vector2(_creditsPeriod, rt.sizeDelta.y);
                rt.anchoredPosition = new Vector2(_creditsScroll + i * _creditsPeriod, 0f);
            }
        }

        // The title's half of the shared timeline. <paramref name="engaged"/> is the same boolean the reel
        // is about to be handed, so a change starts on the frame the picture starts moving and — running
        // at TitleChangeFraction of the reel's window — has landed before the picture finishes.
        private void TickTitle(bool engaged, float dt)
        {
            if (_title == null) return;

            if (_chromeHidden)
            {
                // Nothing of ours is on screen (the about document has the whole surface). Come back from
                // behind it already on the cabinet's name instead of fading to it in front of the player:
                // an animation nobody could see is not an animation worth finishing.
                _desired = CabinetName;
                ShowTitle(CabinetName);
                SetTitleAlpha(1f);
                return;
            }

            // What SHOULD be on screen: the worked control's game, or — the instant nothing is being
            // worked — the cabinet's own name. No extra delay of its own: the title leaves and returns on
            // the reel's events, or it drifts out of step with the picture (founder, 2026-08-08).
            _desired = DisplayNameFor(_source != null ? _source.ActiveSlot : -1) ?? CabinetName;

            // Whichever window the reel is in, the title's whole out-swap-in fits inside it. Half of it
            // dips out and half ramps back in, hence the doubled rate.
            float window = engaged ? AttractZones.TitleFadeOutSeconds : AttractZones.TitleFadeInSeconds;
            float step = dt * 2f / Mathf.Max(0.01f, window);

            if (_desired != _shown)
            {
                // Dip through zero and swap at the bottom. A true two-layer cross-fade would overlap two
                // different strings in the same centred rect and read as mush for the whole transition;
                // dipping through zero keeps it legible and is just as soft at this duration.
                _titleAlpha -= step;
                if (_titleAlpha <= 0f)
                {
                    ShowTitle(_desired);
                    // Whatever the dip overshot below zero belongs to the arrival — so a change lasts the
                    // window it was given, not the window plus a frame.
                    _titleAlpha = Mathf.Min(1f, -_titleAlpha);
                }
            }
            else
            {
                _titleAlpha = Mathf.Min(1f, _titleAlpha + step);
            }

            SetTitleAlpha(_titleAlpha);
        }

        private void ShowTitle(string words)
        {
            if (_shown == words) return;
            _shown = words;
            _title.text = _shown;
        }

        // The slot's game name, or null when nothing is being worked / the config cannot resolve it.
        private string DisplayNameFor(int slot)
        {
            if (slot < 0 || _source == null) return null;
            LauncherConfig config = _source.Config;
            if (config?.slots == null || slot >= config.slots.Count) return null;
            string name = config.slots[slot].displayName;
            return string.IsNullOrEmpty(name) ? null : name;
        }

        private void SetTitleAlpha(float a)
        {
            _titleAlpha = Mathf.Clamp01(a);
            Color c = _title.color;
            c.a = _titleAlpha;
            _title.color = c;
        }

        // -------- view --------

        private void BuildView(RectTransform videoRect)
        {
            // Russo One (SIL OFL, shipped in Assets/Resources/Fonts): a heavy squared-off display face —
            // the closest thing to the clip's chunky pixel lettering that also carries the full Cyrillic
            // range the titles need. Falls back to the built-in face if the asset ever goes missing.
            _font = Resources.Load<Font>("Fonts/RussoOne")
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // The TICKER does not get the display face. Russo One has a single, heavy weight — glorious at
            // 130 px for the title, a solid grey smear at 34 px on a line the eye has to read while it
            // slides past (founder, 2026-08-05: «текст титров не жирным — он плохо читается»). Arimo
            // Regular (Apache-2.0, full Cyrillic, the studio's Helvetica stand-in) is the light, open
            // grotesque that line needs. Brightness is unchanged — full white was already approved.
            _creditsFont = Resources.Load<Font>("Fonts/Arimo-Regular") ?? _font;

            BuildCredits(videoRect);
            BuildTitle(videoRect);
        }

        private void BuildCredits(RectTransform videoRect)
        {
            // NB: deliberately NO RectMask2D. The band is full-screen-width, so the screen edge already
            // does all the clipping the ticker needs — and a RectMask2D culls its children by their
            // RECT, which for an overflowing single-line label is not where the glyphs actually are.
            var viewportGO = new GameObject("CreditsViewport", typeof(RectTransform));
            viewportGO.transform.SetParent(videoRect, false);
            _creditsViewport = viewportGO.GetComponent<RectTransform>();
            _creditsViewport.anchorMin = new Vector2(0f, AttractZones.BottomFraction(AttractZones.CreditsBandBottom));
            _creditsViewport.anchorMax = new Vector2(1f, AttractZones.TopFraction(AttractZones.CreditsBandTop));
            _creditsViewport.offsetMin = Vector2.zero;
            _creditsViewport.offsetMax = Vector2.zero;

            // Two copies, one period apart, both scrolling: whichever leaves on the left has its twin
            // already entering on the right, so the line never shows a seam or an empty screen.
            for (int i = 0; i < _creditsCopies.Length; i++)
            {
                var go = new GameObject($"CreditsCopy{i}", typeof(RectTransform), typeof(Text));
                go.transform.SetParent(_creditsViewport, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(0f, AttractZones.CreditsBandBottom - AttractZones.CreditsBandTop);
                rt.anchoredPosition = Vector2.zero;

                var label = go.GetComponent<Text>();
                label.font = _creditsFont;
                label.fontStyle = FontStyle.Normal; // never bold: the ticker is read on the move
                label.fontSize = creditsFontSize;
                label.alignment = TextAnchor.MiddleLeft;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.raycastTarget = false;
                // Full white against the clip's #262626: far brighter than the baked line's peak of 83/255.
                label.color = Color.white;
                label.text = CreditsLine + CreditsSeparator;
                _creditsCopies[i] = label;
            }

            _creditsPeriod = 0f;
            _creditsScroll = 0f;
        }

        private void BuildTitle(RectTransform videoRect)
        {
            var go = new GameObject("AttractTitle", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(videoRect, false);
            var rt = go.GetComponent<RectTransform>();
            float sideMargin = AttractZones.TitleSideMargin / AttractZones.ClipWidth;
            rt.anchorMin = new Vector2(sideMargin, AttractZones.BottomFraction(AttractZones.TitleBandBottom));
            rt.anchorMax = new Vector2(1f - sideMargin, AttractZones.TopFraction(AttractZones.TitleBandTop));
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _title = go.GetComponent<Text>();
            _title.font = _font;
            _title.alignment = TextAnchor.MiddleCenter;
            _title.raycastTarget = false;
            // Best-fit rather than a fixed size: «6 режимов суеты» gets to be huge while a long slot name
            // like «Последняя смена (Factory)» shrinks instead of overflowing into the hands zone.
            _title.resizeTextForBestFit = true;
            _title.resizeTextMinSize = titleFontSizeMin;
            _title.resizeTextMaxSize = titleFontSizeMax;
            _title.fontSize = titleFontSizeMax;
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.verticalOverflow = VerticalWrapMode.Truncate;
            _title.color = Color.white;
            _title.text = _shown;
        }
    }
}
