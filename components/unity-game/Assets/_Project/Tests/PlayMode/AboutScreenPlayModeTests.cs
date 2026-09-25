using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using AiGameStudio.ArcadeControls;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// The «История проекта» screen (about-screen increment, founder 2026-08-07: «на кнопку меню…
    /// история о проекте, как мы его собирали… скроллящийся джойстиком текст с картинками»).
    ///
    /// What is pinned here is everything a screenshot cannot argue with:
    /// • MENU opens the document and MENU closes it, and the attract reel really is paused while it is
    ///   up and really is playing again afterwards — through the reel's OWN fade-and-freeze path, not a
    ///   second copy of it;
    /// • nothing charges from under the open document — the joystick that scrolls the story is also the
    ///   Meditation slot's launch control, so this is the difference between reading and accidentally
    ///   starting a game;
    /// • the joystick moves the reading position and it clamps at both ends of the story;
    /// • the placeholder document really renders — title, paragraphs and both photo placeholders;
    /// • and an untouched cabinet takes itself back to the reel.
    /// </summary>
    public class AboutScreenPlayModeTests
    {
        private static readonly BackendSnapshot Neutral = default;

        private FakeBackend _fake;
        private HubMenuController _host;
        private AboutScreenController _about;
        private AttractOverlay _overlay;
        private HoldToLaunchController _htl;
        private AttractVideoScreen _video;

        private IEnumerator LoadHub()
        {
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null; // HubMenuController.Start builds video + overlay + about screen
            yield return null;

            foreach (var runner in UnityEngine.Object.FindObjectsByType<ArcadeInputRunner>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(runner);
            _fake = new FakeBackend();
            ArcadeInput.Initialize(_fake);

            _host = UnityEngine.Object.FindAnyObjectByType<HubMenuController>();
            Assert.IsNotNull(_host, "HubMenuController (attract host) missing from HubMenu scene.");
            _about = _host.About;
            Assert.IsNotNull(_about, "The attract host must build the about screen.");
            _overlay = _host.Overlay;
            _video = _host.Video;
            _htl = UnityEngine.Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(_htl, "HoldToLaunchController must be wired into the HubMenu scene.");

            // Deterministic time: the tests drive every loop with a fixed dt.
            _about.AutoTick = false;
            _overlay.AutoTick = false;
            _htl.AutoTick = false;

            yield return Step(Neutral, 0.1f, 2); // a MENU release seen — the gesture is armed
        }

        // One frame of the whole attract stack, in the order the scene runs it: the about screen decides
        // whether the launcher is muted, then the launcher, then the overlay (which drives the reel).
        private IEnumerator Step(BackendSnapshot snapshot, float dt, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                _fake.Next = snapshot;
                ArcadeInput.Update(dt);
                _about.Tick(dt);
                _htl.Tick(dt);
                _overlay.Tick(dt);
                yield return null;
            }
        }

        // A real press: one frame held, then released — the edge the screen toggles on.
        private IEnumerator PressMenu()
        {
            yield return PressMenu(0.1f);
        }

        // The press with an explicit frame length. The sequence tests use a SHORT one so the press
        // itself does not consume most of the transition they are there to watch.
        private IEnumerator PressMenu(float dt)
        {
            yield return Step(new BackendSnapshot { MenuHeld = true }, dt, 1);
            yield return Step(Neutral, dt, 1);
        }

        // Prepare/decode runs on a real-time background thread while test frames can spin far faster,
        // so waits here are wall-clock (mirrors AttractVideoPlayModeTests).
        private static IEnumerator WaitRealtime(Func<bool> done, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
        }

        private IEnumerator RequirePreparedVideo()
        {
            yield return WaitRealtime(() => _video != null && _video.Player != null && _video.Player.isPrepared, 20f);
            if (!_video.Player.isPrepared)
            {
                if (Application.isBatchMode)
                    Assert.Ignore("No video decoder in -batchmode; the reel's pause/resume is covered by the live-editor run.");
                Assert.Fail("The attract clip must prepare before the reel's pause/resume can be judged.");
            }
        }

        // ---------------- (а) MENU opens / closes, the reel pauses and comes back ----------------

        [UnityTest]
        public IEnumerator MenuButton_OpensTheDocument_PausesTheReel_AndTheSecondPressBringsItBack()
        {
            yield return LoadHub();
            yield return RequirePreparedVideo();

            Assert.IsFalse(_about.IsOpen, "The attract screen starts on the reel, not on the document.");
            Assert.IsFalse(_video.IsChargePaused, "The reel plays while nothing is over it.");
            Assert.IsFalse(_overlay.ChromeHidden, "The launcher's title + ticker are up on the attract screen.");

            yield return PressMenu();
            Assert.IsTrue(_about.IsOpen, "The MENU press edge opens «История проекта».");
            Assert.IsTrue(_about.Canvas.gameObject.activeSelf, "The document's canvas comes up with it.");

            // The reel's own fade takes 0.5 s on this path and it only freezes at the BOTTOM of that fade.
            yield return Step(Neutral, 0.1f, 20);
            Assert.AreEqual(1f, _video.FadeAmount, 1e-3f, "The reel must be fully faded out under the document.");
            Assert.IsTrue(_video.IsChargePaused, "The reel is FROZEN (picture and sound) while the document is up.");
            Assert.AreEqual(1f, _about.DocumentAlpha, 1e-3f, "The document is fully faded in.");
            Assert.IsTrue(_overlay.ChromeHidden, "The launcher's title + ticker step aside for the document.");
            Assert.IsFalse(_htl.ChargeBar.gameObject.activeSelf, "The charge bar is not on screen under the document.");

            yield return PressMenu();
            Assert.IsFalse(_about.IsOpen, "A second MENU press closes the document.");
            // The reel does NOT un-freeze here any more — it waits out the document's own fade first
            // (founder, 2026-08-08). That order is pinned in detail by the two sequence tests below.

            yield return Step(Neutral, 0.1f, 20); // document 0.2 s out, then the reel's 0.5 s back in
            Assert.IsFalse(_video.IsChargePaused, "The reel is playing again once the document has gone.");
            Assert.AreEqual(0f, _video.FadeAmount, 1e-3f, "The reel comes back to full brightness.");
            Assert.AreEqual(0f, _about.DocumentAlpha, 1e-3f, "The document is gone.");
            Assert.IsFalse(_about.Canvas.gameObject.activeSelf, "A closed document draws nothing at all.");
            Assert.IsFalse(_overlay.ChromeHidden, "The title + ticker come back with the reel.");
        }

        // ---------------- the hand-over is SEQUENTIAL, never a cross-fade ----------------

        [UnityTest]
        public IEnumerator Opening_ShowsNoTextUntilTheReelHasFullyFadedOut()
        {
            // Founder, 2026-08-08: «пока происходит фейд видео, уже виден текст, грязно. Нужно: СНАЧАЛА
            // выцветает видео, ПОТОМ на него вцветает текст.»
            yield return LoadHub();
            yield return RequirePreparedVideo();

            yield return PressMenu(0.02f); // short frame: the reel's fade is what this test watches
            Assert.IsTrue(_about.IsOpen);
            Assert.IsTrue(_about.IsWaitingForReel, "The document must first wait for the reel to leave.");

            // Walk the WHOLE reel fade: while the veil is anywhere short of its maximum, not one pixel
            // of text may be on screen. Driven off the veil's real state, so the loop is exact however
            // the durations are retuned — and stepped finely, because on this path the whole fade is
            // only AttractZones.DocumentReelFadeSeconds long (founder, 2026-08-08: «надо быстрее»).
            int seenMidFade = 0;
            int guard = 0;
            while (_video.FadeAmount < 1f && guard < 200)
            {
                Assert.AreEqual(0f, _about.DocumentAlpha, 1e-4f,
                    $"Mid-fade (veil {_video.FadeAmount:F2}): the document must still be COMPLETELY invisible.");
                if (_video.FadeAmount > 0.05f && _video.FadeAmount < 0.95f) seenMidFade++;
                yield return Step(Neutral, 0.02f, 1);
                guard++;
            }

            // The assertion above really was exercised on a genuinely half-faded picture over many
            // frames, not on a single instant cut.
            Assert.Greater(seenMidFade, 5, "The reel really did fade gradually with no text over it.");

            // Now the veil is at max: the document — and only now — starts arriving.
            Assert.AreEqual(1f, _video.FadeAmount, 1e-3f, "The reel has finished leaving.");
            Assert.IsFalse(_about.IsWaitingForReel);
            Assert.IsTrue(_video.IsChargePaused, "The picture is frozen before the text arrives.");
            Assert.AreEqual(0f, _about.DocumentAlpha, 1e-4f, "Text has still not started at the hand-over point.");

            yield return Step(Neutral, 0.02f, 4); // 0.08 s of the document's own 0.25 s fade-in
            float midway = _about.DocumentAlpha;
            Assert.Greater(midway, 0f, "The document starts arriving once the reel has gone.");
            Assert.Less(midway, 1f, "…and it arrives by fading, not by cutting in.");
            Assert.AreEqual(1f, _video.FadeAmount, 1e-3f, "The reel stays fully gone while the text arrives.");

            yield return Step(Neutral, 0.1f, 8);
            Assert.AreEqual(1f, _about.DocumentAlpha, 1e-3f, "The document settles fully opaque.");
        }

        [UnityTest]
        public IEnumerator Closing_KeepsTheReelAwayUntilTheDocumentHasFullyGone()
        {
            yield return LoadHub();
            yield return RequirePreparedVideo();

            yield return PressMenu();
            yield return Step(Neutral, 0.1f, 20);
            Assert.AreEqual(1f, _about.DocumentAlpha, 1e-3f, "Precondition: the document is up.");
            Assert.AreEqual(1f, _video.FadeAmount, 1e-3f, "Precondition: the reel is fully away.");

            yield return PressMenu(0.02f); // close, on a short frame so the fade-out is watchable
            Assert.IsFalse(_about.IsOpen);
            Assert.Greater(_about.DocumentAlpha, 0.5f, "The document leaves by fading, not by cutting out.");

            // Walk the document's own fade-out: for as long as ANY of it is still drawn, the reel must
            // stay exactly where it is — fully veiled and frozen.
            int seenMidFade = 0;
            int guard = 0;
            while (_about.DocumentAlpha > 0f && guard < 200)
            {
                Assert.AreEqual(1f, _video.FadeAmount, 1e-3f,
                    $"Document still visible (alpha {_about.DocumentAlpha:F2}): the reel must NOT have started coming back.");
                Assert.IsTrue(_video.IsChargePaused, "…and it must still be frozen.");
                Assert.IsTrue(_overlay.ChromeHidden, "…and the title/ticker must not surface through it.");
                seenMidFade++;
                yield return Step(Neutral, 0.02f, 1);
                guard++;
            }
            Assert.Greater(seenMidFade, 3, "The document really did fade out over several frames.");

            Assert.AreEqual(0f, _about.DocumentAlpha, 1e-4f, "The document has gone.");
            yield return Step(Neutral, 0.02f, 3);
            Assert.Less(_video.FadeAmount, 1f, "Only now does the reel start coming back.");
            Assert.Greater(_video.FadeAmount, 0f, "…and it comes back by fading, not by cutting in.");

            yield return Step(Neutral, 0.1f, 12);
            Assert.AreEqual(0f, _video.FadeAmount, 1e-3f, "The reel is fully back.");
            Assert.IsFalse(_overlay.ChromeHidden, "The title + ticker come back with it.");
        }

        // ---------------- the MENU transition answers at button speed ----------------

        // Founder, 2026-08-08: «при нажатии кнопки меню и нашей текстовой сценки слишком долгий фейд —
        // надо быстрее». It used to be the launch ceremony's 1.2 s veil plus a 0.45 s arrival = 1.65 s
        // from a button press to readable text. The same veil now runs at its own document tempo.
        private const float FineStep = 1f / 120f;

        // Advance the whole stack in fine steps until <paramref name="done"/>, and report the seconds it took.
        private IEnumerator RunUntil(Func<bool> done, float budgetSeconds, Action<float> report)
        {
            float t = 0f;
            int guard = Mathf.CeilToInt(budgetSeconds / FineStep) + 4;
            while (!done() && guard-- > 0)
            {
                yield return Step(Neutral, FineStep, 1);
                t += FineStep;
            }
            report(t);
        }

        [UnityTest]
        public IEnumerator MenuTransition_RunsAtButtonSpeed_NotAtTheLaunchCeremonysPace()
        {
            yield return LoadHub();
            yield return RequirePreparedVideo();

            // ---- opening ----
            yield return PressMenu(FineStep);
            float pressed = 2f * FineStep; // the press gesture itself: held frame + released frame

            float veilSeconds = 0f;
            yield return RunUntil(() => _video.FadeAmount >= 1f, 3f, s => veilSeconds = s);
            float toVeil = pressed + veilSeconds;

            Assert.AreEqual(1f, _video.FadeAmount, 1e-3f, "The reel must reach the full veil.");
            Assert.AreEqual(0f, _about.DocumentAlpha, 1e-4f,
                "…and not a pixel of text may have arrived before it did — the founder's sequence is untouched.");
            Assert.LessOrEqual(toVeil, 0.7f,
                $"A pressed button has to answer: the picture must be gone within 0.7 s (took {toVeil:F2} s). " +
                "At the launch ceremony's 1.2 s the cabinet read as stuck.");
            Assert.Greater(toVeil, 0.25f,
                "…but it must still be a FADE. Quicker than this and the veil reads as a cut, with the seam " +
                "at the zone mask flicking into view on the way past.");

            float textSeconds = 0f;
            yield return RunUntil(() => _about.DocumentAlpha >= 1f, 3f, s => textSeconds = s);
            float toText = toVeil + textSeconds;
            Debug.Log($"[about-speed] press → veil home {toVeil:F2} s, press → text readable {toText:F2} s");
            Assert.LessOrEqual(toText, 1f,
                $"Press to readable text must be under a second (took {toText:F2} s; it was ~1.65 s).");

            // ---- closing, symmetrically prompt ----
            yield return PressMenu(FineStep);
            float gone = 0f;
            yield return RunUntil(() => _about.DocumentAlpha <= 0f, 3f, s => gone = s);
            float toTextGone = 2f * FineStep + gone;
            // The sequence still holds: within the frame that takes the text to zero, the about screen
            // ticks first and the reel only then reads the freed screen — so exactly one frame of the
            // return has run by the time this loop notices. (Closing_KeepsTheReelAwayUntilTheDocumentHasFullyGone
            // walks that boundary frame by frame; here we only check it did not run AHEAD.)
            Assert.Greater(_video.FadeAmount, 0.95f,
                "The reel may not have started back while any text was still drawn — the sequence, again.");

            float back = 0f;
            yield return RunUntil(() => _video.FadeAmount <= 0f, 3f, s => back = s);
            float toReel = toTextGone + back;
            Debug.Log($"[about-speed] press → text gone {toTextGone:F2} s, press → reel back {toReel:F2} s");
            Assert.LessOrEqual(toReel, 0.9f,
                $"Closing must be as prompt as opening (took {toReel:F2} s).");
            Assert.IsFalse(_video.IsChargePaused, "…and the reel is running again at the end of it.");
        }

        [UnityTest]
        public IEnumerator ChargingAControl_StillUsesTheSlowLaunchCeremonyFade()
        {
            // The two tempos must not have been merged: the document's promptness is not allowed to
            // leak into the fade the founder tuned for a control being worked. (The exact charge-path
            // numbers, and the title's synchronisation with them, are pinned in
            // AttractOverlayPlayModeTests — this is the guard that the about screen did not steal them.)
            yield return LoadHub();

            Assert.AreEqual(ReelYield.Charge, _overlay.ReelYieldReason, "Idle sits on the charge tempo.");

            // Work the crank (slot 0) — no document anywhere near this.
            for (int i = 0; i < 5; i++)
                yield return Step(new BackendSnapshot { CrankDeltaDegrees = 10f }, 0.1f, 1);
            Assert.GreaterOrEqual(_htl.ActiveSlot, 0, "Precondition: a control owns a slot.");
            Assert.AreEqual(ReelYield.Charge, _overlay.ReelYieldReason,
                "A charging control must NOT be running the document's button tempo.");

            // Half of the document's fade would already be over; the launch ceremony's is barely started.
            _overlay.Tick(AttractZones.DocumentReelFadeSeconds);
            Assert.Less(_video.FadeAmount, 0.95f,
                "After a whole document-fade's worth of time the launch fade is still under way — the slow " +
                "1.2 s the founder approved is intact.");

            // And the document really is the faster of the two.
            Assert.Less(AttractZones.DocumentReelFadeSeconds, AttractZones.ReelFadeOutSeconds);
            Assert.Less(AttractZones.DocumentReelFadeSeconds, AttractZones.ReelFadeInSeconds);
        }

        [Test]
        public void Veil_GoesAllTheWay_SoTheHandsCannotGhostThrough()
        {
            // Founder, 2026-08-08: «контролы (руки на видео) должны выцветать ДО КОНЦА».
            Assert.AreEqual(1f, AttractZones.ReelFadeMaxAlpha, 1e-4f,
                "The veil must reach FULL opacity — a fully faded reel shows nothing of the hands.");
        }

        // ---------------- (б) nothing launches from under the document ----------------

        [UnityTest]
        public IEnumerator WhileOpen_HoldingAControl_NeverCharges_AndNeverLaunches()
        {
            yield return LoadHub();

            yield return PressMenu();
            Assert.IsTrue(_about.IsOpen);
            Assert.IsTrue(_htl.Suspended, "An open document mutes the launcher.");

            // The red button charges the Factory slot in 5 s. Hold it for eight.
            yield return Step(new BackendSnapshot { RedHeld = true }, 0.1f, 80);

            Assert.AreEqual(0f, _htl.Charge, 1e-4f, "Nothing may charge while the document is up.");
            Assert.AreEqual(-1, _htl.ActiveSlot, "No slot may become active under the document.");
            Assert.AreEqual(0, _htl.FlowerCount, "No falling objects while the document is up.");
            Assert.IsFalse(_htl.ChargeBar.gameObject.activeSelf, "No charge bar while the document is up.");
            Assert.AreEqual(HubScenes.HubMenu, SceneManager.GetActiveScene().name,
                "Eight seconds of a held control under the document must NOT have launched a game.");

            // The joystick that scrolls the document is the Meditation slot's own launch control.
            yield return Step(new BackendSnapshot { Joystick = new Vector2(0f, -1f) }, 0.1f, 80);
            Assert.AreEqual(0f, _htl.Charge, 1e-4f, "Scrolling the story must not charge the joystick's slot.");
            Assert.AreEqual(HubScenes.HubMenu, SceneManager.GetActiveScene().name,
                "Scrolling must never start Meditation.");

            // And the launcher is live again once the document has actually left the screen (the mute
            // outlasts the MENU press by the document's own fade-out — nothing may charge under a
            // document that is still half-drawn).
            yield return PressMenu();
            Assert.IsTrue(_htl.Suspended, "Still muted while the document is fading out.");
            yield return Step(Neutral, 0.1f, 6);
            Assert.IsFalse(_htl.Suspended, "Once the document has gone, the controls are the launcher's again.");
            yield return Step(new BackendSnapshot { RedHeld = true }, 0.1f, 10);
            Assert.Greater(_htl.Charge, 0f, "After closing, a held control charges again.");
        }

        // ---------------- (в) the joystick scrolls, and clamps ----------------

        [UnityTest]
        public IEnumerator Joystick_ScrollsTheDocument_AndClampsAtBothEnds()
        {
            yield return LoadHub();
            yield return PressMenu();

            Assert.Greater(_about.MaxScroll, 0f,
                "The placeholder story must be longer than one screen — otherwise this proves nothing.");
            Assert.AreEqual(0f, _about.ScrollPosition, 1e-3f, "A fresh entry starts at the first line.");

            float documentTopY = _about.Document.anchoredPosition.y;

            yield return Step(new BackendSnapshot { Joystick = new Vector2(0f, -1f) }, 1f / 60f, 30);
            Assert.Greater(_about.ScrollPosition, 10f, "Pushing the stick DOWN travels down the story.");
            Assert.Greater(_about.Document.anchoredPosition.y, documentTopY,
                "The document itself really moved — not just a number.");

            // Hold it down far past the end: it must stop at the end, not run off.
            yield return Step(new BackendSnapshot { Joystick = new Vector2(0f, -1f) }, 1f / 60f, 400);
            Assert.AreEqual(_about.MaxScroll, _about.ScrollPosition, 1f, "Scrolling clamps at the end of the story.");

            // And back up, equally far.
            yield return Step(new BackendSnapshot { Joystick = new Vector2(0f, 1f) }, 1f / 60f, 400);
            Assert.AreEqual(0f, _about.ScrollPosition, 1f, "Scrolling clamps at the first line.");
            Assert.AreEqual(documentTopY, _about.Document.anchoredPosition.y, 1f);

            // Rest wobble under the deadzone must not creep the page.
            float parked = _about.ScrollPosition;
            yield return Step(new BackendSnapshot { Joystick = new Vector2(0.1f, -0.2f) }, 1f / 60f, 120);
            Assert.AreEqual(parked, _about.ScrollPosition, 1e-3f, "A resting stick must not creep the document.");
        }

        // ---------------- the shipped placeholders really render ----------------

        [UnityTest]
        public IEnumerator Document_RendersTheStory_WithEveryPhotoItNames()
        {
            yield return LoadHub();
            yield return PressMenu();
            yield return Step(Neutral, 0.1f, 6);

            Assert.IsNotNull(_about.Content);
            Assert.IsFalse(_about.Content.IsFallback, "The shipped content.json must load (not the error placeholder).");

            var texts = _about.TextBlocks;
            Assert.GreaterOrEqual(texts.Count, 6, "Title, headings, paragraphs and captions are all drawn.");

            bool titleFound = false;
            foreach (Text label in texts)
            {
                Assert.IsNotNull(label.font, "Every block is set in a real font.");
                if (label.text == AttractOverlay.CabinetName) titleFound = true;
                if (label.fontSize == AboutLayout.TitleFontSize || label.fontSize == AboutLayout.HeadingFontSize)
                    StringAssert.Contains("Russo", label.font.name, "Display type is Russo One — the launcher's own face.");
                if (label.fontSize == AboutLayout.ParagraphFontSize || label.fontSize == AboutLayout.CaptionFontSize)
                    StringAssert.Contains("Arimo", label.font.name, "Body type is Arimo — the launcher's own body face.");
            }
            Assert.IsTrue(titleFound, $"The document opens on the cabinet's name «{AttractOverlay.CabinetName}».");

            var photos = _about.PhotoBlocks;
            // Count is the founder's call, not ours — the document is authored content and grows.
            // What must hold is that EVERY photo it names is drawn and fits the reading column.
            Assert.GreaterOrEqual(photos.Count, 2, "The story is illustrated — at least two images.");
            int photoBlocks = 0;
            foreach (AboutBlock b in _about.Content.Blocks)
                if (b.Kind == AboutBlockType.Photo) photoBlocks++;
            Assert.AreEqual(photoBlocks, photos.Count, "Every photo block in the document is actually drawn.");
            foreach (RectTransform photo in photos)
            {
                Assert.LessOrEqual(photo.rect.width, AboutLayout.ColumnWidth + 0.5f,
                    "A photo is never wider than the reading column.");
                Assert.AreEqual(1200f / 800f, photo.rect.width / photo.rect.height, 0.01f,
                    "The placeholder's 3:2 aspect is preserved.");
            }

            // The whole story is stacked top-down inside the column, in file order and without overlap.
            Assert.Greater(_about.DocumentHeight, _about.Viewport.rect.height,
                "The document is longer than its window — that is what makes it a scrolling document.");
        }

        // ---------------- (д) the cabinet takes itself back ----------------

        [UnityTest]
        public IEnumerator NoInput_ReturnsToTheReel_ByItself()
        {
            yield return LoadHub();
            yield return RequirePreparedVideo();

            _about.IdleReturnSeconds = 0.5f; // [tune] ships at 60 s; shortened so the rule is provable here
            yield return PressMenu();
            Assert.IsTrue(_about.IsOpen);

            // Reading (the stick being worked) must NOT time out.
            yield return Step(new BackendSnapshot { Joystick = new Vector2(0f, -1f) }, 0.1f, 20);
            Assert.IsTrue(_about.IsOpen, "Two seconds of scrolling keeps the document up.");

            // Hands off.
            yield return Step(Neutral, 0.1f, 8);
            Assert.IsFalse(_about.IsOpen, "An untouched cabinet returns to the attract reel by itself.");

            yield return Step(Neutral, 0.1f, 15);
            Assert.IsFalse(_video.IsChargePaused, "…and the reel is playing again after the automatic return.");
            Assert.AreEqual(0f, _video.FadeAmount, 1e-3f);
            Assert.IsFalse(_overlay.ChromeHidden);
        }

        // ---------------- founder-facing stills ----------------

        [UnityTest]
        public IEnumerator Capture_AttractThenDocumentThenBack()
        {
            yield return LoadHub();
            yield return RequirePreparedVideo();

            // (a) the attract screen, mid-clip so the still shows the reel's actual content.
            yield return WaitRealtime(() => _video.IsShowingFrames && _video.Player.frame > 0, 20f);
            long mid = _video.Player.frameCount > 0 ? (long)_video.Player.frameCount / 2 : 400;
            _video.Player.frame = mid;
            yield return WaitRealtime(() => Math.Abs(_video.Player.frame - mid) < 40, 10f);
            yield return Step(Neutral, 0.1f, 2);
            yield return Capture("hub-about-attract.png");

            // (a2) the moment the reel is HALF gone: a dimming picture and, deliberately, no text at all.
            yield return PressMenu();
            int guard = 0;
            while (_video.FadeAmount < 0.5f && guard < 200) { yield return Step(Neutral, 0.02f, 1); guard++; }
            Assert.AreEqual(0f, _about.DocumentAlpha, 1e-4f, "Mid-reel-fade still has no text on it.");
            yield return Capture("hub-about-reelfading.png");

            // (b1) the HAND-OVER: the veil has arrived at full, the text is on its way in (founder's
            // requested still — «вуаль дошла, текст на полпути»).
            guard = 0;
            while (_video.FadeAmount < 1f && guard < 200) { yield return Step(Neutral, 0.02f, 1); guard++; }
            Assert.AreEqual(0f, _about.DocumentAlpha, 1e-4f, "No text until the veil is home.");

            // The bare fully-veiled field, with no document over it yet: this is the frame where a seam
            // at AttractZones.MaskBottom (veil vs zone mask) would show, if the veil were not fully opaque.
            // It arrives half a second after the press now, not 1.2 s (founder, 2026-08-08).
            yield return Capture("hub-about-veiled.png");
            yield return Step(Neutral, 0.02f, 5); // 0.1 s into the document's 0.25 s fade-in
            Assert.Greater(_about.DocumentAlpha, 0.1f);
            Assert.Less(_about.DocumentAlpha, 0.95f);
            Debug.Log($"[about-screen] hand-over still: veil={_video.FadeAmount:F2}, documentAlpha={_about.DocumentAlpha:F2}");
            yield return Capture("hub-about-handover.png");

            // (b2) the document, fully faded in.
            yield return Step(Neutral, 0.1f, 10);
            Assert.IsTrue(_about.IsOpen);
            Assert.AreEqual(1f, _about.DocumentAlpha, 1e-3f);
            yield return Capture("hub-about-open.png");

            yield return Step(new BackendSnapshot { Joystick = new Vector2(0f, -1f) }, 1f / 60f, 90);
            Assert.Greater(_about.ScrollPosition, 100f);
            yield return Capture("hub-about-scrolled.png");

            // (c) closed again — the reel is back.
            yield return PressMenu();
            yield return Step(Neutral, 0.1f, 20);
            Assert.IsFalse(_about.IsOpen);
            Assert.IsFalse(_video.IsChargePaused);
            yield return Capture("hub-about-closed.png");
        }

        // -------- screenshot helper (mirrors the attract-video harness) --------

        private IEnumerator Capture(string fileName)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                yield break;

            for (int i = 0; i < 4; i++) yield return null;

            const int W = 1920, H = 1080;

            Camera cam = Camera.main;
            if (cam == null)
                foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                    if (c.isActiveAndEnabled) { cam = c; break; }
            Assert.IsNotNull(cam, "The HubMenu scene must carry a camera for the capture.");

            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.isActiveAndEnabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 1f;
                }
            }
            yield return null;

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            Color32[] px = tex.GetPixels32();
            int magenta = 0, varied = 0;
            Color32 first = px.Length > 0 ? px[0] : default;
            foreach (var p in px)
            {
                if (p.r > 220 && p.g < 60 && p.b > 220) magenta++;
                if (Mathf.Abs(p.r - first.r) + Mathf.Abs(p.g - first.g) + Mathf.Abs(p.b - first.b) > 12) varied++;
            }

            string dir = Environment.GetEnvironmentVariable("HUB_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Application.temporaryCachePath;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[about-screen] wrote screenshot '{path}' ({W}x{H}); magenta={magenta}px, varied={varied}px");

            cam.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(rt);

            Assert.AreEqual(0, magenta, $"'{fileName}' must contain ZERO magenta (no missing shaders/sprites).");
            Assert.Greater(varied, px.Length / 200, $"'{fileName}' is not a blank frame — the screen really rendered.");
        }
    }
}
