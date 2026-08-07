using NUnit.Framework;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// The attract screen's two animated things — the reel's fade and the title's change — run on ONE
    /// timeline (founder, 2026-08-08: «анимация смены заголовка должна быть с теми же таймингами, что и
    /// фейд — согласованы, читаться как будто это реакция на нашу игру»).
    ///
    /// AttractOverlayPlayModeTests pins the BEHAVIOUR: both start on the same frame off the
    /// same event and the title lands inside the reel's window. This file pins the thing that behaviour
    /// rests on and that a future tuning session could quietly break — that the title's durations are
    /// DERIVED from the reel's rather than being a second pair of numbers that has to be remembered.
    /// Retune <see cref="AttractZones.ReelFadeOutSeconds"/> or <see cref="AttractZones.ReelFadeInSeconds"/>
    /// and the title follows; add a copy anywhere and these tests are what should stop being true.
    /// </summary>
    public class AttractTimingTests
    {
        [Test]
        public void TitleChange_IsCutFromTheReelsOwnFade()
        {
            Assert.AreEqual(AttractZones.ReelFadeOutSeconds * AttractZones.TitleChangeFraction,
                AttractZones.TitleFadeOutSeconds, 1e-5f,
                "The title's change on the way out is a fraction of the REEL's fade-out, not a number of its own.");
            Assert.AreEqual(AttractZones.ReelFadeInSeconds * AttractZones.TitleChangeFraction,
                AttractZones.TitleFadeInSeconds, 1e-5f,
                "…and its return is a fraction of the reel's fade-in.");
        }

        [Test]
        public void TitleChange_FitsInsideTheReelsWindow_AndLeadsIt()
        {
            // Strictly inside: the title has to have SETTLED while the picture is still moving, so the
            // pair reads as the name answering the hand on the control and the reel then getting out of
            // its way. Equal durations would land them together (the swap sitting at the exact middle of
            // the picture's dip — one long blank); longer is the desync the founder reported.
            Assert.Less(AttractZones.TitleFadeOutSeconds, AttractZones.ReelFadeOutSeconds,
                "The title must finish becoming the game's name BEFORE the reel has finished leaving.");
            Assert.Less(AttractZones.TitleFadeInSeconds, AttractZones.ReelFadeInSeconds,
                "«название сменяется позже, чем снова вцветает стартовый экран» — the cabinet's name must " +
                "be back BEFORE the picture is, never after it.");

            // …but not so far ahead that it stops looking like the same movement.
            Assert.Greater(AttractZones.TitleChangeFraction, 0.5f,
                "A title change over in a blink no longer reads as part of the reel's movement.");
            Assert.LessOrEqual(AttractZones.TitleChangeFraction, 1f,
                "The title's change can never outlast the window it rides.");
        }

        // ---- the second tempo: a button press, not a charging control (founder, 2026-08-08) ----

        [Test]
        public void TheDocumentsTempo_IsDecisivelyQuickerThanTheLaunchCeremonys()
        {
            // «При нажатии кнопки меню и нашей текстовой сценки слишком долгий фейд — надо быстрее.»
            // The same veil, two speeds, chosen by the REASON the reel is yielding.
            Assert.AreEqual(AttractZones.DocumentReelFadeSeconds,
                AttractZones.ReelFadeOutSecondsFor(ReelYield.Document), 1e-5f);
            Assert.AreEqual(AttractZones.DocumentReelFadeSeconds,
                AttractZones.ReelFadeInSecondsFor(ReelYield.Document), 1e-5f);
            Assert.AreEqual(AttractZones.ReelFadeOutSeconds,
                AttractZones.ReelFadeOutSecondsFor(ReelYield.Charge), 1e-5f,
                "A charging control keeps the slow ceremony fade — the two tempos must not have merged.");
            Assert.AreEqual(AttractZones.ReelFadeInSeconds,
                AttractZones.ReelFadeInSecondsFor(ReelYield.Charge), 1e-5f);

            Assert.Less(AttractZones.DocumentReelFadeSeconds, AttractZones.ReelFadeInSeconds,
                "A pressed button must answer quicker than the launch ceremony's quickest half.");
            Assert.GreaterOrEqual(AttractZones.DocumentReelFadeSeconds, 0.25f,
                "…and still be a fade: quicker than this the veil reads as a cut and the seam at the zone " +
                "mask flicks into view on the way past.");

            // Press → readable text, and text gone → picture back: the whole gesture, both ways.
            Assert.LessOrEqual(AttractZones.DocumentReelFadeSeconds + AboutLayout.FadeInSeconds, 0.8f,
                "Press to readable text must stay under 0.8 s (it was 1.65 s).");
            Assert.LessOrEqual(AboutLayout.FadeOutSeconds + AttractZones.DocumentReelFadeSeconds, 0.8f,
                "Closing must be just as prompt as opening.");
            Assert.Less(AboutLayout.FadeOutSeconds, AboutLayout.FadeInSeconds,
                "The text leaves quicker than it arrives — it has to be gone before the picture may start back.");
        }

        [Test]
        public void TitleWindows_FollowTheChargeFade_NeverTheDocumentOne()
        {
            // The title is hidden behind the document for the whole time the button tempo is in use, so
            // it has nothing to synchronise with there. If a future retune ever wires it to the document's
            // number, the title would start racing the picture on the charge path again.
            Assert.AreEqual(AttractZones.ReelFadeOutSeconds * AttractZones.TitleChangeFraction,
                AttractZones.TitleFadeOutSeconds, 1e-5f);
            Assert.AreNotEqual(AttractZones.DocumentReelFadeSeconds * AttractZones.TitleChangeFraction,
                AttractZones.TitleFadeOutSeconds,
                "The title's window must be cut from the CHARGE fade, not from the document's.");
            Assert.Greater(AttractZones.TitleFadeInSeconds, AttractZones.DocumentReelFadeSeconds * 0.5f,
                "Sanity: the title still runs on the launch ceremony's clock, which is the slower one.");
        }

        [Test]
        public void ReelFades_AreStillTheFounderApprovedDurations()
        {
            // The one place the actual seconds are written down. Pinned as literals on purpose: every
            // other assertion here goes through the constants and would happily follow a wrong retune.
            Assert.AreEqual(1.2f, AttractZones.ReelFadeOutSeconds, 1e-5f,
                "«выцветают (медленно)» — the reel leaves over 1.2 s.");
            Assert.AreEqual(0.8f, AttractZones.ReelFadeInSeconds, 1e-5f,
                "«при отжатии все восстанавливается» — and comes back over 0.8 s.");
            Assert.AreEqual(0.5f, AttractZones.DocumentReelFadeSeconds, 1e-5f,
                "…and the MENU button's own transition runs the same veil in 0.5 s, both ways.");
            Assert.AreEqual(0.25f, AboutLayout.FadeInSeconds, 1e-5f, "The document arrives in 0.25 s.");
            Assert.AreEqual(0.2f, AboutLayout.FadeOutSeconds, 1e-5f, "…and leaves in 0.2 s.");
        }
    }
}
