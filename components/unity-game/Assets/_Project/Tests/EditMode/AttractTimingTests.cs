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

        [Test]
        public void ReelFades_AreStillTheFounderApprovedDurations()
        {
            // The one place the actual seconds are written down. Pinned as literals on purpose: every
            // other assertion here goes through the constants and would happily follow a wrong retune.
            Assert.AreEqual(1.2f, AttractZones.ReelFadeOutSeconds, 1e-5f,
                "«выцветают (медленно)» — the reel leaves over 1.2 s.");
            Assert.AreEqual(0.8f, AttractZones.ReelFadeInSeconds, 1e-5f,
                "«при отжатии все восстанавливается» — and comes back over 0.8 s.");
        }
    }
}
