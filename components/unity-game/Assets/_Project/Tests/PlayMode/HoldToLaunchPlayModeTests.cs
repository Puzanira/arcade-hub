using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using AiGameStudio.ArcadeControls;
using AiGameStudio.ArcadeHub;
using EndlessSisyphus;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// Drives the hold-to-launch loop headless with a <see cref="FakeBackend"/>, injecting input frame by
    /// frame and advancing the controller with a fixed dt (AutoTick off) for determinism:
    /// the charge bar grows visibly and piles the flower grid in proportion to the charge; releasing
    /// decays the charge and clears the pile;
    /// cranking the installed Sisyphus slot (Crank, slot 0 in the founder's layout) to full launches
    /// Endless Sisyphus; holding a planned slot to full shows the "СКОРО" overlay and resets without launching.
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

        private static bool IsRectOnScreen(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            Rect r = rt.rect;
            if (r.width <= 1f || r.height <= 1f) return false;

            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float xMin = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float xMax = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float yMin = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float yMax = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float overlapW = Mathf.Min(xMax, Screen.width) - Mathf.Max(xMin, 0f);
            float overlapH = Mathf.Min(yMax, Screen.height) - Mathf.Max(yMin, 0f);
            return overlapW > 1f && overlapH > 1f;
        }

        private static void AssertRectOnScreen(RectTransform rt, string because)
        {
            Assert.IsTrue(rt.gameObject.activeInHierarchy, $"{because}: not active in hierarchy.");
            Rect r = rt.rect;
            Assert.Greater(r.width, 1f, $"{because}: no visible width.");
            Assert.Greater(r.height, 1f, $"{because}: no visible height.");
            Assert.IsTrue(IsRectOnScreen(rt), $"{because}: lies outside the screen.");
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
        public IEnumerator Charge_GrowsVisiblyWithFlowers_ThenDecaysAndClears()
        {
            yield return LoadMenuAndTakeOver();

            Assert.AreEqual(0f, _htl.Charge, 1e-4, "starts uncharged");
            float widthAtZero = _htl.BarFill.rect.width;

            // Crank the installed Sisyphus slot (slot 0 = Crank) for 2.5s -> ~50% charge.
            for (int i = 0; i < 25; i++)
                yield return Step(Crank(10f), 0.1f);

            Assert.AreEqual(0.5f, _htl.Charge, 0.05f, "cranking half the charge time should be ~50%");
            Assert.AreEqual(0, _htl.ActiveSlot, "the crank-bound Sisyphus slot is charging");
            AssertRectOnScreen(_htl.BarFill, "Charge bar fill at ~50%");
            Assert.Greater(_htl.BarFill.rect.width, widthAtZero + 100f, "the bar fill grew visibly");
            Assert.Greater(_htl.FlowerCount, 0, "flowers should be piling while charging");
            // Proportion: the pile covers ~charge of the grid capacity (bottom row up). At ~50% charge it
            // is roughly half full — the launch animation's whole point is "screen filled in proportion".
            int cap = _htl.FlowerCapacity;
            Assert.Greater(_htl.FlowerCount, (int)(0.30f * cap), "at ~50% charge the pile is well past empty");
            Assert.Less(_htl.FlowerCount, (int)(0.70f * cap), "at ~50% charge the pile is well short of full");
            // At least one sprite is visibly on-screen (the pile grew from the bottom row up).
            bool anyFlowerOnScreen = false;
            foreach (var flower in _htl.Flowers)
                if (IsRectOnScreen(flower)) { anyFlowerOnScreen = true; break; }
            Assert.IsTrue(anyFlowerOnScreen, "At least one piled flower must be visibly on-screen while charging.");
            AssertRectOnScreen(_htl.FirstFlower, "First (bottom-row) flower at ~50% charge");

            // Release: crank stops. Decay over ~1s plus the crank timeout -> back to empty, pile cleared.
            for (int i = 0; i < 20; i++)
                yield return Step(Neutral, 0.1f);

            Assert.AreEqual(0f, _htl.Charge, 1e-3, "released charge decays fully to 0");
            Assert.AreEqual(-1, _htl.ActiveSlot, "slot dropped after full decay");
            Assert.AreEqual(0, _htl.FlowerCount, "the flower pile clears as the charge decays");
        }

        [UnityTest]
        public IEnumerator Crank_ToFull_LaunchesInstalledSisyphus()
        {
            yield return LoadMenuAndTakeOver();

            // Crank continuously; the controller loads the Sisyphus scene the instant charge reaches 1.
            int guard = 0;
            while (SceneManager.GetActiveScene().name != "Main" && guard < 160)
            {
                yield return Step(Crank(10f), 0.1f);
                guard++;
            }

            Assert.AreEqual("Main", SceneManager.GetActiveScene().name,
                "cranking the installed Sisyphus slot to full should launch its scene");
            yield return null;
            // Sisyphus is code-genned: Bootstrap builds SisyphusGame when its scene loads from the launcher.
            var game = Object.FindAnyObjectByType<SisyphusGame>();
            Assert.IsNotNull(game, "Sisyphus scene should have booted its game object from the launcher.");
        }

        [UnityTest]
        public IEnumerator PlannedSlot_ToFull_ShowsComingSoon_ResetsAndDoesNotLaunch()
        {
            yield return LoadMenuAndTakeOver();

            // Hold Bang (slot 3 = "Arcade Prototype", 'soon') until the overlay appears.
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
            StringAssert.Contains("Arcade Prototype", label.text, "overlay names the planned slot");
            StringAssert.DoesNotContain("Endless Sisyphus", label.text, "one-hot: no other game name leaks in");
            StringAssert.DoesNotContain("Home Alone", label.text, "one-hot: no other game name leaks in");

            Assert.Less(_htl.Charge, 0.1f, "charge resets after a planned slot fires");
        }
    }
}
