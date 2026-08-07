using UnityEngine;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// Every tunable number of the «История проекта» screen in one place — typography, the reading
    /// column, the joystick scroll's feel, the fades and the inactivity timeout. This is a SCAFFOLD for
    /// the founder's forthcoming mock-up: when it arrives, the shape of the document changes here (and
    /// the words/photos change in StreamingAssets/About), not in the controller.
    ///
    /// All sizes are in the cabinet's reference pixel space (1920×1080), the same space
    /// <see cref="AttractZones"/> measures the attract reel in.
    /// </summary>
    public static class AboutLayout
    {
        // ---- surface ----

        public const float RefWidth = 1920f;
        public const float RefHeight = 1080f;

        /// <summary>
        /// The reading column's width. ~1200 px of a 1920 px screen is roughly 70–90 characters per line
        /// at the body size below — the band where long-form text stays readable; a full-width line on a
        /// cabinet screen loses the eye on the way back to the left margin.
        /// </summary>
        public const float ColumnWidth = 1200f;

        /// <summary>Air above the scrolling viewport, in reference px.</summary>
        public const float ViewportTopMargin = 64f;

        /// <summary>
        /// Air below the viewport. Bigger than the top margin because the controls hint line lives down
        /// there, outside the scroll — the document must never slide under it.
        /// </summary>
        public const float ViewportBottomMargin = 96f;

        /// <summary>Blank leading the document starts with, so the title is not welded to the top edge.</summary>
        public const float ContentLeadIn = 48f;

        /// <summary>Blank trailing after the last block, so the end of the story can scroll clear of the edge.</summary>
        public const float ContentLeadOut = 140f;

        // ---- typography ----

        public const int TitleFontSize = 96;
        public const int HeadingFontSize = 54;
        public const int ParagraphFontSize = 34;
        public const int CaptionFontSize = 26;

        /// <summary>Body leading. 1.35 is the long-form default: tight enough to hold a paragraph together, open enough to read at 34 px.</summary>
        public const float ParagraphLineSpacing = 1.35f;
        public const float DisplayLineSpacing = 1.1f;
        public const float CaptionLineSpacing = 1.25f;

        // ---- vertical rhythm (blank space BEFORE a block of each kind) ----

        public const float SpaceBeforeTitle = 0f;
        public const float SpaceBeforeHeading = 72f;
        public const float SpaceBeforeParagraph = 28f;
        public const float SpaceBeforePhoto = 48f;
        public const float SpaceBeforeCaption = 14f;

        // ---- palette (the attract screen's own tones, so the two screens are one cabinet) ----

        /// <summary>The document's background — the attract clip's own flat #262626.</summary>
        public static Color Background => AttractZones.BackgroundColor;

        public static readonly Color TitleColor = Color.white;

        /// <summary>Section heads in the launcher's charge amber — the cabinet's single accent.</summary>
        public static readonly Color HeadingColor = AttractZones.ChargeColor;

        /// <summary>Body copy: not pure white — a hair down, which is what stops a wall of text glaring.</summary>
        public static readonly Color ParagraphColor = new Color32(222, 222, 222, 255);

        /// <summary>Photo captions: grey, deliberately quieter than the body.</summary>
        public static readonly Color CaptionColor = new Color32(144, 144, 144, 255);

        /// <summary>The controls hint at the bottom edge — quieter still.</summary>
        public static readonly Color HintColor = new Color32(122, 122, 122, 255);

        // ---- scrollbar ----

        public const float ScrollBarWidth = 6f;
        public const float ScrollBarGap = 28f;      // distance from the column's right edge
        public const float ScrollBarMinThumb = 64f;
        public static readonly Color ScrollBarTrackColor = new Color32(58, 58, 58, 255);
        public static readonly Color ScrollBarThumbColor = new Color32(150, 150, 150, 255);

        // ---- joystick scroll feel ----

        /// <summary>
        /// Top scroll speed at full joystick deflection, in reference px/s. At 1200 px/s a full push
        /// moves a bit more than one screen-height per second — fast enough to cross a long document
        /// without a wrist ache, slow enough that the text does not become a blur.
        /// </summary>
        public const float ScrollMaxSpeed = 1200f;

        /// <summary>Ramp UP rate, px/s². 4800 = top speed in a quarter second: the document leans into the push instead of snapping.</summary>
        public const float ScrollAcceleration = 4800f;

        /// <summary>Ramp DOWN rate, px/s². Deliberately steeper than the acceleration (0.2 s to a full stop) — a scroll that coasts on after the stick is centred feels broken, not smooth.</summary>
        public const float ScrollDeceleration = 6000f;

        /// <summary>Deflection below this counts as centred. 0.25 clears a worn arcade stick's rest wobble; past it the response is re-scaled from zero, so there is no jump at the threshold.</summary>
        public const float ScrollDeadzone = 0.25f;

        // ---- transitions ----

        /// <summary>Document fade-IN, seconds (the reel's own fade-out is 1.2 s and runs underneath it).</summary>
        public const float FadeInSeconds = 0.45f;

        /// <summary>Document fade-OUT, seconds — shorter than the reel's 0.8 s fade-in, so the picture is already coming back as the text leaves.</summary>
        public const float FadeOutSeconds = 0.3f;

        /// <summary>
        /// [tune] Inactivity before the screen returns to the attract reel by itself. A cabinet in a
        /// public room must never be left stuck on a document by somebody who walked away mid-read;
        /// 60 s is long enough to read a screenful, short enough that the reel is back before the next
        /// person arrives.
        /// </summary>
        public const float IdleReturnSeconds = 60f;

        /// <summary>What the bottom edge tells the player about the two controls this screen uses.</summary>
        public const string HintText = "ДЖОЙСТИК ВВЕРХ-ВНИЗ — ЛИСТАТЬ    ·    МЕНЮ — НАЗАД";

        public const int HintFontSize = 24;
    }
}
