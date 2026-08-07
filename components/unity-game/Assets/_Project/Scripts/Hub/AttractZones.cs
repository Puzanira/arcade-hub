namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The attract reel's THREE ZONES, measured off the shipped clip itself
    /// (<c>StreamingAssets/Attract.mp4</c>, 1920×1080, 6.33 s, 152 frames) rather than eyeballed:
    /// every frame was diffed against the clip's flat background and the rows carrying ink were
    /// collected into a union, so these numbers bound the baked artwork over the WHOLE loop, not
    /// just one frame.
    ///
    /// <code>
    ///   rows    0 …  23   empty
    ///   rows   24 …  47   BAKED CREDITS  — the author names, burnt into the clip, very dim (peak
    ///                                      luminance 83/255) and, despite reading like a ticker,
    ///                                      completely STATIC: identical pixels in all 152 frames.
    ///   rows  144 … 271   BAKED TITLE    — «НАЖМИ ЧТО-НИБУДЬ», animated on/off across the loop.
    ///   rows  335 …1006   HANDS / CONTROLS — the only zone the founder keeps visible (the
    ///                                      "red zone" of the 2026-08-05 mockup).
    /// </code>
    ///
    /// (Rows 283-284, 309 and 327-329 carry 6 stray anti-aliasing pixels in total across the whole
    /// loop — accounted for by <see cref="MaskBottom"/> sitting below them.)
    ///
    /// The launcher therefore paints an opaque <see cref="BackgroundColor"/> panel over rows
    /// 0…<see cref="MaskBottom"/>, hiding the baked credits and the baked title, and draws its own
    /// brighter credits ticker and its own game/cabinet title inside that reclaimed band. Because
    /// the panel is the clip's own background colour, the seam is invisible: the reel simply looks
    /// like it has nothing above the hands.
    ///
    /// All values are in the clip's OWN pixel space, top-down (row 0 = top). Callers turn them into
    /// normalised anchors against the video rect via <see cref="TopFraction"/> /
    /// <see cref="BottomFraction"/>, so the layout follows the video however it is letterboxed.
    /// </summary>
    public static class AttractZones
    {
        /// <summary>The shipped clip's native size — also the cabinet's surface (1920×1080).</summary>
        public const float ClipWidth = 1920f;
        public const float ClipHeight = 1080f;

        // ---- measured extents of the baked artwork (union over all 152 frames) ----

        /// <summary>First/last row of the clip's own (dim, static) credits line.</summary>
        public const float BakedCreditsTop = 24f;
        public const float BakedCreditsBottom = 47f;

        /// <summary>First/last row of the clip's own «НАЖМИ ЧТО-НИБУДЬ» title.</summary>
        public const float BakedTitleTop = 144f;
        public const float BakedTitleBottom = 271f;

        /// <summary>First/last row of the hands-and-controls zone — the part that stays visible.</summary>
        public const float HandsTop = 335f;
        public const float HandsBottom = 1006f;

        /// <summary>
        /// Bottom row of the cover panel. Sits in the 3-pixel gutter between the last stray pixel of
        /// the title zone (329) and the first pixel of the hands (335), so the panel hides every baked
        /// element above the hands without clipping a single hand pixel.
        /// </summary>
        public const float MaskBottom = 332f;

        /// <summary>
        /// The clip's background, sampled from the frames: a perfectly flat #262626 over the entire
        /// loop (1.8 M of 2.07 M pixels in a frame are exactly this value — no gradient, no vignette),
        /// which is why a solid panel in this tone is seamless.
        /// </summary>
        public static readonly UnityEngine.Color BackgroundColor = new UnityEngine.Color32(38, 38, 38, 255);

        // ---- where the launcher draws ITS OWN text (same top-down clip pixel space) ----

        /// <summary>Our credits ticker's band — a little taller than the baked line it replaces.</summary>
        public const float CreditsBandTop = 10f;
        public const float CreditsBandBottom = 64f;

        /// <summary>
        /// Our title's band. It used to be centred on the baked title's optical centre (row 207.5); the
        /// founder's «полосу прогрузки вставлять под название» (2026-08-05) moved the charge bar into the
        /// same reclaimed strip, so the band gives up its bottom 15 rows to open
        /// <see cref="ChargeBandTop"/>…<see cref="ChargeBandBottom"/> underneath. It is still taller than
        /// the baked title it replaces (rows 144…271), so the title lost no size — only the empty air
        /// below it — and title + bar now read as ONE centred block inside the masked region.
        /// </summary>
        public const float TitleBandTop = 118f;
        public const float TitleBandBottom = 278f;

        /// <summary>Side margin for the title band, in clip pixels.</summary>
        public const float TitleSideMargin = 50f;

        /// <summary>
        /// The charge bar's band — directly UNDER the title (founder, 2026-08-05: «полосу прогрузки
        /// вставлять под название»), no longer stuck to the bottom of the screen. It sits in the last
        /// clear rows of the reclaimed strip: 8 rows of air under the title, 10 rows of background left
        /// between it and <see cref="MaskBottom"/>, so the bar never crowds the hands zone.
        /// </summary>
        public const float ChargeBandTop = 286f;
        public const float ChargeBandBottom = 322f;

        /// <summary>The bar's outer (framed) width in clip pixels — narrower than the title, centred.</summary>
        public const float ChargeBarWidth = 1208f;

        /// <summary>The bar's pixel-art frame thickness, in clip pixels.</summary>
        public const float ChargeBarBorder = 4f;

        /// <summary>How many notches the fill is cut into (an arcade energy bar, not a smooth gradient).</summary>
        public const int ChargeBarSegments = 24;

        /// <summary>Width of the gap punched between two notches, in clip pixels.</summary>
        public const float ChargeBarSegmentGap = 6f;

        // ---- the attract screen's own palette ----

        /// <summary>
        /// The launcher's charge colour — ONE colour for every slot (founder, 2026-08-05: «полосу
        /// прогрузки хочется дизайн сделать получше и единого цвета»). Deliberately the amber the
        /// approved "СКОРО" overlay already uses, so the bar and that overlay read as the same system;
        /// per-slot identity is carried by the themed rain and by the title, not by the bar.
        /// </summary>
        public static readonly UnityEngine.Color ChargeColor = new UnityEngine.Color(1f, 0.85f, 0.2f);

        /// <summary>
        /// The empty part of the bar: a near-black slot the amber notches punch out of. FULLY opaque — at
        /// 235 the amber frame behind it bled through and turned the unfilled half a muddy olive instead
        /// of reading as an empty socket (seen on the first pass of the founder shot).
        /// </summary>
        public static readonly UnityEngine.Color ChargeTrackColor = new UnityEngine.Color32(14, 14, 14, 255);

        /// <summary>
        /// The bar's frame: the charge colour held back to a whisper, so an EMPTY bar is still visibly the
        /// same object as a full one rather than a stray black rectangle on the reel.
        /// </summary>
        public static readonly UnityEngine.Color ChargeFrameColor = new UnityEngine.Color(1f, 0.85f, 0.2f, 0.55f);

        // ---- the reel's own fade (a control is being charged) ----

        /// <summary>
        /// Seconds the reel takes to fade out when a player starts charging a slot (founder: «выцветают
        /// (медленно)»). The ramp is linear in time and smoothstepped in alpha, so it leaves and arrives
        /// without a visible edge.
        /// </summary>
        public const float ReelFadeOutSeconds = 1.2f;

        /// <summary>Seconds the reel takes to come back once the control is released («при отжатии все восстанавливается»).</summary>
        public const float ReelFadeInSeconds = 0.8f;

        /// <summary>
        /// How far the reel fades: a veil in the clip's OWN background tone, so a faded reel becomes the
        /// same flat #262626 the masked band already is (no seam at <see cref="MaskBottom"/>, which a
        /// fade-to-black would tear open). FULLY opaque — founder tune 2026-08-08 («уводить ещё сильнее,
        /// практически в 0», then «контролы должны выцветать ДО КОНЦА»): the hands must not ghost through
        /// a charged or a reading screen at all. At 1 the veil, the zone mask and the clip's own
        /// background are the same #262626, so a fully faded reel is ONE flat field with no seam at
        /// <see cref="MaskBottom"/> to find — the veil covers the whole video rect, mask included.
        ///
        /// This value is also the gate the about document waits on: it starts arriving only once the
        /// veil has reached this alpha (see <see cref="AboutScreenController"/>).
        /// </summary>
        public const float ReelFadeMaxAlpha = 1f;

        /// <summary>Normalised anchor Y (0 = bottom, 1 = top) for a top-down clip row.</summary>
        public static float TopFraction(float clipRow) => 1f - clipRow / ClipHeight;

        /// <summary>Alias of <see cref="TopFraction"/> read for the lower edge of a band.</summary>
        public static float BottomFraction(float clipRow) => 1f - clipRow / ClipHeight;
    }
}
