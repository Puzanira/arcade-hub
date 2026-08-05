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
        /// Our title's band, centred on the baked title's optical centre (row 207.5) so the replacement
        /// lands exactly where the eye already expects it, and fully inside the masked region.
        /// </summary>
        public const float TitleBandTop = 123f;
        public const float TitleBandBottom = 293f;

        /// <summary>Side margin for the title band, in clip pixels.</summary>
        public const float TitleSideMargin = 50f;

        /// <summary>Normalised anchor Y (0 = bottom, 1 = top) for a top-down clip row.</summary>
        public static float TopFraction(float clipRow) => 1f - clipRow / ClipHeight;

        /// <summary>Alias of <see cref="TopFraction"/> read for the lower edge of a band.</summary>
        public static float BottomFraction(float clipRow) => 1f - clipRow / ClipHeight;
    }
}
