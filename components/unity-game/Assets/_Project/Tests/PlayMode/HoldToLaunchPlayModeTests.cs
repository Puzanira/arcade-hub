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
    /// Drives the hold-to-launch loop headless with a <see cref="FakeBackend"/>, injecting input frame by
    /// frame and advancing the controller with a fixed dt (AutoTick off) for determinism:
    /// the charge bar grows visibly and spawns rain; releasing decays the charge and clears the rain;
    /// cranking the installed Test Game slot to full launches TestGame; holding a planned slot to full
    /// shows the "СКОРО" overlay and resets without launching.
    /// </summary>
    public class HoldToLaunchPlayModeTests
    {
        private FakeBackend _fake;
        private HoldToLaunchController _htl;

        private static BackendSnapshot Crank(float deg) => new BackendSnapshot { CrankDeltaDegrees = deg };
        private static BackendSnapshot Bang() => new BackendSnapshot { BangHeld = true };
        private static readonly BackendSnapshot Neutral = default;

        private void TakeOverInput()
        {
            var runner = Object.FindAnyObjectByType<ArcadeInputRunner>();
            if (runner != null) Object.DestroyImmediate(runner);
            _fake = new FakeBackend();
            ArcadeInput.Initialize(_fake);
        }

        // One deterministic frame: inject input, pump ArcadeInput, advance the controller by dt, present.
        private IEnumerator Step(BackendSnapshot snapshot, float dt)
        {
            _fake.Next = snapshot;
            ArcadeInput.Update(dt);
            _htl.Tick(dt);
            yield return null;
        }

        private static void AssertRectOnScreen(RectTransform rt, string because)
        {
            Assert.IsTrue(rt.gameObject.activeInHierarchy, $"{because}: not active in hierarchy.");
            Rect r = rt.rect;
            Assert.Greater(r.width, 1f, $"{because}: no visible width.");
            Assert.Greater(r.height, 1f, $"{because}: no visible height.");

            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float xMin = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float xMax = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float yMin = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float yMax = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float overlapW = Mathf.Min(xMax, Screen.width) - Mathf.Max(xMin, 0f);
            float overlapH = Mathf.Min(yMax, Screen.height) - Mathf.Max(yMin, 0f);
            Assert.Greater(overlapW, 1f, $"{because}: lies outside the screen horizontally.");
            Assert.Greater(overlapH, 1f, $"{because}: lies outside the screen vertically.");
        }

        private IEnumerator LoadMenuAndTakeOver()
        {
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;
            TakeOverInput();

            _htl = Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(_htl, "HoldToLaunchController must be wired into the HubMenu scene.");
            _htl.AutoTick = false; // we drive Tick ourselves with a fixed dt
        }

        [UnityTest]
        public IEnumerator Charge_GrowsVisiblyWithRain_ThenDecaysAndClears()
        {
            yield return LoadMenuAndTakeOver();

            Assert.AreEqual(0f, _htl.Charge, 1e-4, "starts uncharged");
            float widthAtZero = _htl.BarFill.rect.width;

            // Crank the installed Test Game slot (slot 0 = Crank) for 2.5s -> ~50% charge.
            for (int i = 0; i < 25; i++)
                yield return Step(Crank(10f), 0.1f);

            Assert.AreEqual(0.5f, _htl.Charge, 0.05f, "cranking half the charge time should be ~50%");
            Assert.AreEqual(0, _htl.ActiveSlot, "the crank-bound Test Game slot is charging");
            AssertRectOnScreen(_htl.BarFill, "Charge bar fill at ~50%");
            Assert.Greater(_htl.BarFill.rect.width, widthAtZero + 100f, "the bar fill grew visibly");
            Assert.Greater(_htl.RainCount, 0, "rain should be falling while charging");
            AssertRectOnScreen(_htl.FirstRainDrop, "A rain drop");

            // Release: crank stops. Decay over ~1s plus the crank timeout -> back to empty, rain cleared.
            for (int i = 0; i < 20; i++)
                yield return Step(Neutral, 0.1f);

            Assert.AreEqual(0f, _htl.Charge, 1e-3, "released charge decays fully to 0");
            Assert.AreEqual(-1, _htl.ActiveSlot, "slot dropped after full decay");
            Assert.AreEqual(0, _htl.RainCount, "rain clears as the charge decays");
        }

        [UnityTest]
        public IEnumerator Crank_ToFull_LaunchesInstalledTestGame()
        {
            yield return LoadMenuAndTakeOver();

            // Crank continuously; the controller loads TestGame the instant charge reaches 1.
            int guard = 0;
            while (SceneManager.GetActiveScene().name != HubScenes.TestGame && guard < 120)
            {
                yield return Step(Crank(10f), 0.1f);
                guard++;
            }

            Assert.AreEqual(HubScenes.TestGame, SceneManager.GetActiveScene().name,
                "cranking the installed slot to full should launch TestGame");
            yield return null;
            var game = Object.FindAnyObjectByType<TestGameController>();
            Assert.IsNotNull(game, "TestGame scene should have booted its controller.");
        }

        [UnityTest]
        public IEnumerator PlannedSlot_ToFull_ShowsComingSoon_ResetsAndDoesNotLaunch()
        {
            yield return LoadMenuAndTakeOver();

            // Hold Bang (slot 3 = "Factory Game", planned) until the overlay appears. (Home Alone and Life
            // Choices are now INSTALLED, so this uses a slot that is still planned.)
            int guard = 0;
            while (!_htl.ComingSoonVisible && guard < 120)
            {
                yield return Step(Bang(), 0.1f);
                guard++;
            }

            Assert.IsTrue(_htl.ComingSoonVisible, "a planned slot at full charge shows the СКОРО overlay");
            Assert.AreEqual(HubScenes.HubMenu, SceneManager.GetActiveScene().name,
                "a planned slot must NOT launch a scene");

            Text label = _htl.ComingSoonLabel;
            AssertRectOnScreen(label.rectTransform, "СКОРО overlay");
            StringAssert.Contains("СКОРО", label.text, "overlay announces coming-soon");
            StringAssert.Contains("Factory Game", label.text, "overlay names the planned slot");
            StringAssert.DoesNotContain("Test Game", label.text, "one-hot: no other game name leaks in");
            StringAssert.DoesNotContain("Home Alone", label.text, "one-hot: no other game name leaks in");

            Assert.Less(_htl.Charge, 0.1f, "charge resets after a planned slot fires");
        }
    }
}
