using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using AiGameStudio.ArcadeControls;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// attract-screen v2 (founder, 2026-08-05). The reel keeps only its BOTTOM zone — the pixel hands
    /// and controls — visible; the launcher masks the clip's baked credits line and its baked
    /// «НАЖМИ ЧТО-НИБУДЬ» title behind a panel in the clip's own background tone and redraws both
    /// itself: a brighter, actually-scrolling credits ticker, and a title that names the cabinet when
    /// nobody is playing and the GAME OF THE WORKED CONTROL the moment somebody touches one.
    ///
    /// What is pinned here is what a screenshot cannot argue with: the mask really covers the rows the
    /// baked artwork occupies and really stops short of the hands (measured off the clip — see
    /// <see cref="AttractZones"/>); the ticker is brighter than the line it replaces and loops without
    /// a seam; the title follows the same slot signal the charge bar does, via a fade rather than a cut;
    /// and the whole overlay stays UNDER the hold-to-launch canvas so the themed rain still falls over
    /// the title, as the founder's mockup requires.
    /// </summary>
    public class AttractOverlayPlayModeTests
    {
        private FakeBackend _fake;

        private static readonly BackendSnapshot Neutral = default;
        private const int SisyphusSlot = 0; // Crank → "Endless Sisyphus" in launcher-config.json

        private HubMenuController _host;
        private AttractOverlay _overlay;
        private HoldToLaunchController _htl;

        private IEnumerator LoadAttractScreen()
        {
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null; // HubMenuController.Start builds the video + overlay
            yield return null;

            foreach (var r in Object.FindObjectsByType<ArcadeInputRunner>(FindObjectsSortMode.None))
                Object.DestroyImmediate(r);
            _fake = new FakeBackend();
            ArcadeInput.Initialize(_fake);

            _host = Object.FindAnyObjectByType<HubMenuController>();
            Assert.IsNotNull(_host, "HubMenuController (attract host) missing from HubMenu scene.");
            _overlay = _host.Overlay;
            Assert.IsNotNull(_overlay, "The attract host must build its AttractOverlay.");
            _htl = Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(_htl, "HoldToLaunchController must be wired into the HubMenu scene.");

            // Deterministic time: the tests drive both loops with a fixed dt.
            _overlay.AutoTick = false;
            _htl.AutoTick = false;
        }

        // ---------------- the mask: only the hands zone survives ----------------

        // Where an overlay rect's edges land in the CLIP's own top-down pixel space, so assertions can
        // be written against the numbers measured off the video rather than against anchor arithmetic.
        private static void ClipRowsOf(RectTransform rect, RectTransform videoRect, out float topRow, out float bottomRow)
        {
            var video = new Vector3[4];
            videoRect.GetWorldCorners(video); // 0 = bottom-left, 1 = top-left, 2 = top-right, 3 = bottom-right
            float videoTopY = video[1].y;
            float videoHeight = video[1].y - video[0].y;

            var r = new Vector3[4];
            rect.GetWorldCorners(r);
            topRow = (videoTopY - r[1].y) / videoHeight * AttractZones.ClipHeight;
            bottomRow = (videoTopY - r[0].y) / videoHeight * AttractZones.ClipHeight;
        }

        [UnityTest]
        public IEnumerator ZoneMask_HidesBakedCreditsAndTitle_AndSparesTheHands()
        {
            yield return LoadAttractScreen();

            AttractVideoScreen video = _host.Video;
            RectTransform mask = video.ZoneMask;
            Assert.IsNotNull(mask, "The attract video must paint a zone mask over its baked top zones.");

            var videoRect = (RectTransform)video.Screen.transform;
            ClipRowsOf(mask, videoRect, out float top, out float bottom);

            Assert.LessOrEqual(top, AttractZones.BakedCreditsTop,
                "The mask must start at (or above) the clip's baked credits line — no dim baked names left showing.");
            Assert.GreaterOrEqual(bottom, AttractZones.BakedTitleBottom,
                "The mask must extend past the bottom of the clip's baked «НАЖМИ ЧТО-НИБУДЬ» title.");
            Assert.Less(bottom, AttractZones.HandsTop,
                "The mask must stop before the hands zone — that is the ONE zone the founder keeps visible.");

            // It has to actually hide, not tint: opaque, and in the clip's own flat background tone so
            // the seam is invisible.
            var img = mask.GetComponent<Image>();
            Assert.IsNotNull(img, "The zone mask is a solid panel.");
            Assert.AreEqual(1f, img.color.a, 1e-3f, "The zone mask must be fully opaque.");
            Assert.AreEqual(AttractZones.BackgroundColor.r, img.color.r, 1f / 255f,
                "The mask must be the clip's own background tone (#262626) — a mismatched panel shows a seam.");
            Assert.AreEqual(AttractZones.BackgroundColor.g, img.color.g, 1f / 255f);
            Assert.AreEqual(AttractZones.BackgroundColor.b, img.color.b, 1f / 255f);

            // Full width — a mask narrower than the reel would leave the ends of the baked line visible.
            var maskCorners = new Vector3[4];
            var videoCorners = new Vector3[4];
            mask.GetWorldCorners(maskCorners);
            videoRect.GetWorldCorners(videoCorners);
            Assert.LessOrEqual(maskCorners[0].x, videoCorners[0].x + 0.01f, "The mask spans the reel's full width (left).");
            Assert.GreaterOrEqual(maskCorners[3].x, videoCorners[3].x - 0.01f, "The mask spans the reel's full width (right).");
        }

        // ---------------- the overlay renders above the reel but below the rain ----------------

        [UnityTest]
        public IEnumerator Overlay_DrawsAboveTheReel_ButUnderTheFallingObjects()
        {
            yield return LoadAttractScreen();

            Canvas overlayCanvas = _overlay.TitleLabel.canvas;
            Assert.IsNotNull(overlayCanvas, "The title must live on a canvas.");
            Assert.IsNotNull(_htl.Canvas, "The hold-to-launch stack must have its canvas.");

            Assert.Less(overlayCanvas.sortingOrder, _htl.Canvas.sortingOrder,
                "The themed falling objects must rain OVER the title (founder's mockup), so the overlay's " +
                "canvas has to sort BELOW the hold-to-launch canvas.");

            // …and above the reel: the title/ticker are children of the video image itself, so they are
            // drawn after it within the same canvas.
            Assert.IsTrue(_overlay.TitleLabel.transform.IsChildOf(_host.Video.Screen.transform),
                "The overlay is parented onto the video rect so it tracks the letterboxed reel exactly.");
            Assert.Greater(_overlay.TitleLabel.transform.GetSiblingIndex(),
                _host.Video.ZoneMask.GetSiblingIndex(),
                "The title must be drawn after the mask, otherwise the mask would paint over it.");
        }

        // ---------------- the credits ticker ----------------

        [UnityTest]
        public IEnumerator Credits_AreBrighterThanTheBakedLine_AndNameEveryAuthor()
        {
            yield return LoadAttractScreen();

            Text[] copies = _overlay.CreditsCopies;
            Assert.AreEqual(2, copies.Length, "The ticker loops by drawing the line twice, end to end.");
            foreach (Text c in copies)
                Assert.IsNotNull(c, "Both ticker copies must be built.");

            // The clip's own credits peak at luminance 83/255 — a dim grey. The founder asked for
            // "поярче": ours must be decisively brighter, not marginally.
            const float BakedPeakLuminance = 83f / 255f;
            float ours = Mathf.Max(copies[0].color.r, Mathf.Max(copies[0].color.g, copies[0].color.b)) * copies[0].color.a;
            Assert.Greater(ours, BakedPeakLuminance * 2f,
                "Our credits must be far brighter than the baked line they replace (baked peak = 83/255).");

            string line = copies[0].text;
            foreach (string author in new[]
            {
                "Ирина Пузанова", "Екатерина Гребенюк", "Лада Реботунова", "Иван Меркурьев",
                "Александра Новохацкая", "Максим Юнин", "Антон Михалёв",
            })
                StringAssert.Contains(author, line, $"The ticker must credit {author}.");

            Assert.AreEqual(copies[0].text, copies[1].text,
                "Both copies carry the same string — that is what makes the loop seamless.");

            // It sits in the band the mask reclaimed, not over the hands.
            ClipRowsOf(_overlay.CreditsViewport, (RectTransform)_host.Video.Screen.transform,
                out float top, out float bottom);
            Assert.GreaterOrEqual(top, 0f, "The ticker stays on screen.");
            Assert.Less(bottom, AttractZones.MaskBottom,
                "The ticker must sit inside the masked band, never over the hands.");
        }

        [UnityTest]
        public IEnumerator Credits_Scroll_AndWrapWithoutASeam()
        {
            yield return LoadAttractScreen();

            // One frame of real layout so the font has generated glyphs and preferredWidth is meaningful.
            _overlay.AutoTick = true;
            yield return null;
            yield return null;
            _overlay.AutoTick = false;

            float period = _overlay.CreditsCopies[0].preferredWidth;
            Assert.Greater(period, 1f, "The ticker's line must have a measurable width.");

            float before = _overlay.CreditsScroll;
            _overlay.Tick(0.5f);
            float after = _overlay.CreditsScroll;
            Assert.Less(after, before, "The ticker scrolls right-to-left (offset decreases).");

            // Run it well past a full period: the offset must WRAP rather than march off to -infinity,
            // and the two copies must stay exactly one period apart — that is the seamless loop.
            for (int i = 0; i < 400; i++)
            {
                _overlay.Tick(0.1f);
                Assert.Greater(_overlay.CreditsScroll, -period - 1f,
                    "The ticker offset must wrap within one period, never run away.");
                Assert.LessOrEqual(_overlay.CreditsScroll, 1f, "The ticker offset stays within its period.");
            }

            var a = (RectTransform)_overlay.CreditsCopies[0].transform;
            var b = (RectTransform)_overlay.CreditsCopies[1].transform;
            Assert.AreEqual(period, b.anchoredPosition.x - a.anchoredPosition.x, 1f,
                "The trailing copy must sit exactly one period behind the leading one (no gap, no overlap).");

            // The rects must be as wide as the glyphs. A zero-width rect still LOOKS right in every
            // structural assertion above, but anything that clips or culls by layout (a RectMask2D, a
            // layout group) then makes the ticker vanish — which is exactly how it first shipped blank.
            Assert.Greater(a.rect.width, 1000f, "The ticker's rect must cover the text it draws, not collapse to zero.");
            Assert.Greater(a.rect.height, 1f, "The ticker's rect must have height.");
        }

        // ---------------- the title ----------------

        [UnityTest]
        public IEnumerator Title_ShowsTheCabinetName_WhenNobodyIsPlaying()
        {
            yield return LoadAttractScreen();

            Assert.AreEqual(AttractOverlay.CabinetName, _overlay.ShownTitle,
                "Idle attract screen names the cabinet.");
            Assert.AreEqual(AttractOverlay.CabinetName, _overlay.TitleLabel.text);

            for (int i = 0; i < 60; i++) _overlay.Tick(0.1f); // six idle seconds change nothing
            Assert.AreEqual(AttractOverlay.CabinetName, _overlay.ShownTitle);
            Assert.AreEqual(1f, _overlay.TitleAlpha, 1e-3f, "An unchanging title is fully opaque.");
        }

        // Engage the crank (Sisyphus) far enough for the charge machine to own the slot, without
        // charging anywhere near a launch.
        private void EngageCrank()
        {
            for (int i = 0; i < 5; i++)
            {
                _fake.Next = new BackendSnapshot { CrankDeltaDegrees = 10f };
                ArcadeInput.Update(0.1f);
                _htl.Tick(0.1f);
            }
            Assert.AreEqual(SisyphusSlot, _htl.ActiveSlot, "The crank must own the Sisyphus slot.");
        }

        [UnityTest]
        public IEnumerator Title_FadesToTheWorkedControlsGame()
        {
            yield return LoadAttractScreen();
            string expected = _htl.Config.slots[SisyphusSlot].displayName;
            Assert.IsFalse(string.IsNullOrEmpty(expected));

            EngageCrank();

            // The first overlay tick notices the slot and starts a fade — it must NOT hard-cut.
            _overlay.Tick(0.05f);
            Assert.IsTrue(_overlay.IsFading, "A title change is faded, never cut.");
            Assert.AreEqual(expected, _overlay.DesiredTitle, "The title names the game of the worked control.");
            Assert.AreEqual(AttractOverlay.CabinetName, _overlay.ShownTitle,
                "Mid-fade the OLD words are still the ones on screen — they dip out first.");
            Assert.Less(_overlay.TitleAlpha, 1f, "The fade is an alpha ramp.");

            // Hold the control (do NOT tick the charge machine, so the slot stays owned) and let the
            // fade finish.
            float min = 1f;
            for (int i = 0; i < 40 && _overlay.IsFading; i++)
            {
                _overlay.Tick(0.05f);
                min = Mathf.Min(min, _overlay.TitleAlpha);
            }

            Assert.IsFalse(_overlay.IsFading, "The fade must complete.");
            Assert.Less(min, 0.2f, "The fade dips low enough to be a real fade, not a flicker.");
            Assert.AreEqual(expected, _overlay.ShownTitle, "After the fade the game's name is on screen.");
            Assert.AreEqual(expected, _overlay.TitleLabel.text);
            Assert.AreEqual(1f, _overlay.TitleAlpha, 1e-3f, "The new title settles fully opaque.");
        }

        [UnityTest]
        public IEnumerator Title_FadesBackToTheCabinet_AfterTheControlGoesQuiet()
        {
            yield return LoadAttractScreen();
            string game = _htl.Config.slots[SisyphusSlot].displayName;

            EngageCrank();
            for (int i = 0; i < 40 && (_overlay.IsFading || _overlay.ShownTitle != game); i++)
                _overlay.Tick(0.05f);
            Assert.AreEqual(game, _overlay.ShownTitle, "Precondition: the game's name is up.");

            // Let go: the charge decays and the machine drops the slot.
            for (int i = 0; i < 30 && _htl.ActiveSlot >= 0; i++)
            {
                _fake.Next = Neutral;
                ArcadeInput.Update(0.1f);
                _htl.Tick(0.1f);
            }
            Assert.AreEqual(-1, _htl.ActiveSlot, "Precondition: nothing is being worked any more.");

            // The title lingers on the game for a beat — a player who briefly lets go of the crank must
            // not get the name yanked away mid-glance.
            _overlay.Tick(0.1f);
            Assert.AreEqual(game, _overlay.ShownTitle, "The game's name survives a momentary release.");

            for (int i = 0; i < 100 && _overlay.ShownTitle != AttractOverlay.CabinetName; i++)
                _overlay.Tick(0.1f);

            Assert.AreEqual(AttractOverlay.CabinetName, _overlay.ShownTitle,
                "Once the control has been quiet for the return delay, the cabinet's name comes back.");
            for (int i = 0; i < 20 && _overlay.IsFading; i++) _overlay.Tick(0.05f);
            Assert.AreEqual(1f, _overlay.TitleAlpha, 1e-3f, "…fully opaque again.");
        }

        // ---------------- founder-facing contract screenshots ----------------

        // Wall-clock wait: decode runs on a real-time background thread while an unfocused editor spins
        // test frames far faster than real time.
        private static IEnumerator WaitRealtime(System.Func<bool> done, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline) yield return null;
        }

        /// <summary>
        /// Assert that a band of CLIP rows actually carries ink brighter than the reel's background.
        /// Guards the failure mode a green test suite happily misses: a label that is present, correctly
        /// positioned and correctly coloured in the object graph, yet draws nothing (the first cut of the
        /// ticker was culled by a RectMask2D because its rect was zero-width — every structural assertion
        /// passed while the screen showed an empty strip).
        /// </summary>
        private static void AssertBandHasInk(Texture2D tex, int topRow, int bottomRow, string what)
        {
            Color32[] px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            int bright = 0;
            for (int row = topRow; row <= bottomRow; row++)
            {
                int y = h - 1 - row; // ReadPixels is bottom-up; clip rows are top-down
                if (y < 0 || y >= h) continue;
                for (int x = 0; x < w; x++)
                {
                    Color32 p = px[y * w + x];
                    // The reel's background is #262626; our text is white.
                    if (p.r > 120 && p.g > 120 && p.b > 120) bright++;
                }
            }
            Assert.Greater(bright, 200,
                $"{what}: clip rows {topRow}-{bottomRow} must actually be PAINTED (found {bright} bright pixels). " +
                "A correctly-built but invisible widget is the bug this guards.");
        }

        private IEnumerator Capture(string fileName, int inkTopRow = -1, int inkBottomRow = -1, string inkWhat = null)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                yield break;
            for (int i = 0; i < 4; i++) yield return null;

            const int W = 1920, H = 1080;
            Camera cam = Camera.main;
            if (cam == null)
                foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                    if (c.isActiveAndEnabled) { cam = c; break; }
            Assert.IsNotNull(cam, "A camera is needed to capture the attract screen.");

            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
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
            cam.targetTexture = null;

            string dir = System.Environment.GetEnvironmentVariable("HUB_SHOT_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Application.temporaryCachePath;
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, fileName);
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[attract-v2] wrote '{path}' ({W}x{H})");

            if (inkTopRow >= 0)
                AssertBandHasInk(tex, inkTopRow, inkBottomRow, inkWhat ?? fileName);

            Object.Destroy(tex);
            Object.Destroy(rt);
        }

        /// <summary>
        /// The founder's three review states in one pass (the reel keeps decoding between them):
        /// idle on the cabinet's name, mid-fade, and a control being worked with its game named and its
        /// themed rain falling over the title.
        /// </summary>
        [UnityTest]
        public IEnumerator Capture_AttractV2_FounderStates()
        {
            yield return LoadAttractScreen();

            AttractVideoScreen video = _host.Video;
            yield return WaitRealtime(() => video.IsShowingFrames && video.Player.frame > 0, 20f);
            if (Application.isBatchMode && (!video.IsShowingFrames || video.Player.frame <= 0))
                Assert.Ignore("VideoPlayer produced no frames in -batchmode; the visual gate runs in the live editor.");

            // Park on a frame whose hands are all drawn, so the shots show the zone that survives.
            video.Player.frame = 20;
            yield return WaitRealtime(() => System.Math.Abs(video.Player.frame - 20) < 30, 10f);

            // Let the ticker lay itself out and drift off its start position.
            _overlay.AutoTick = true;
            yield return null;
            yield return null;
            _overlay.AutoTick = false;
            _overlay.Tick(1.5f);

            // (a) idle — the cabinet's name, no bar, no rain.
            Assert.AreEqual(AttractOverlay.CabinetName, _overlay.ShownTitle);
            Assert.AreEqual(0, _htl.FlowerCount, "Idle: nothing raining.");
            // Both replacements must be VISIBLE, not merely built: the credits band and the title band.
            yield return Capture("attract-v2-a-idle.png",
                (int)AttractZones.CreditsBandTop, (int)AttractZones.CreditsBandBottom, "the credits ticker");

            // (b) mid-fade — the crank has been grabbed and the title is ramping in.
            EngageCrank();
            for (int i = 0; i < 40 && !(_overlay.ShownTitle != AttractOverlay.CabinetName
                                        && _overlay.TitleAlpha > 0.35f && _overlay.TitleAlpha < 0.75f); i++)
                _overlay.Tick(0.03f);
            Assert.IsTrue(_overlay.IsFading, "Capture (b) must catch the title mid-fade.");
            Debug.Log($"[attract-v2] fade shot at alpha={_overlay.TitleAlpha:F2}, showing '{_overlay.ShownTitle}'");
            yield return Capture("attract-v2-b-fade.png");

            // (c) engaged — «Endless Sisyphus» settled, boulders raining OVER it, bar charged.
            for (int i = 0; i < 40 && _overlay.IsFading; i++) _overlay.Tick(0.05f);
            for (int i = 0; i < 15; i++)
            {
                _fake.Next = new BackendSnapshot { CrankDeltaDegrees = 10f };
                ArcadeInput.Update(0.1f);
                _htl.Tick(0.1f);
                _overlay.Tick(0.1f);
            }
            Assert.AreEqual(_htl.Config.slots[SisyphusSlot].displayName, _overlay.ShownTitle);
            Assert.Greater(_htl.FlowerCount, 0, "Capture (c) must show the themed rain over the title.");
            Assert.IsTrue(_htl.BarFill.gameObject.activeSelf, "Capture (c) shows the charge bar.");
            Debug.Log($"[attract-v2] engaged shot: charge={_htl.Charge:F2}, flowers={_htl.FlowerCount}");
            yield return Capture("attract-v2-c-engaged.png");
        }

        [UnityTest]
        public IEnumerator Title_NamesTheSameSlotTheChargeBarIsCharging()
        {
            yield return LoadAttractScreen();

            // The green button is a DIFFERENT slot — the title must follow whichever control is worked,
            // reading the very signal hold-to-launch charges against, so the two can never disagree.
            for (int i = 0; i < 5; i++)
            {
                _fake.Next = new BackendSnapshot { GreenHeld = true };
                ArcadeInput.Update(0.1f);
                _htl.Tick(0.1f);
            }

            int slot = _htl.ActiveSlot;
            Assert.GreaterOrEqual(slot, 0, "The green button must own a slot.");
            string expected = _htl.Config.slots[slot].displayName;

            for (int i = 0; i < 40 && (_overlay.IsFading || _overlay.ShownTitle != expected); i++)
                _overlay.Tick(0.05f);

            Assert.AreEqual(expected, _overlay.ShownTitle,
                $"The title must name slot {slot}'s game ({expected}) — the same slot the bar is charging.");
        }
    }
}
