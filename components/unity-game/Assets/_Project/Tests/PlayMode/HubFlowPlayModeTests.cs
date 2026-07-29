using System.Collections;
using System.Collections.Generic;
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
    /// The launcher menu mechanics under the founder's layout: the shipped HubMenu comes up with SEVEN
    /// correctly-labelled rows (5 installed [PLAY] + 2 [soon]) and joystick navigation moves + wraps the
    /// INFORMATIONAL highlight. Launching happens ONLY via hold-to-launch (founder's gate-2 decision:
    /// instant-select removed — a red press charges Factory's slot instead of firing the highlighted row).
    /// The per-game entry/return loops live in <see cref="GamesIntegrationPlayModeTests"/>; this file
    /// guards the menu rendering + navigation, plus the built-in Test Game rig (which stays in the
    /// project though it left the shipped menu) via a config injected into the REAL HoldToLaunchController,
    /// proving the self-returning menu→game→menu loop still works on the hold path.
    /// </summary>
    public class HubFlowPlayModeTests
    {
        private FakeBackend _fake;

        private static readonly BackendSnapshot Neutral = default;
        private static BackendSnapshot JoyUp => new BackendSnapshot { Joystick = new Vector2(0f, 1f) };
        private static BackendSnapshot JoyDown => new BackendSnapshot { Joystick = new Vector2(0f, -1f) };
        private static BackendSnapshot MenuHeld => new BackendSnapshot { MenuHeld = true };

        private void TakeOverInput()
        {
            foreach (var r in Object.FindObjectsByType<ArcadeInputRunner>(FindObjectsSortMode.None))
                Object.DestroyImmediate(r);
            _fake = new FakeBackend();
            ArcadeInput.Initialize(_fake);
        }

        private void Push(BackendSnapshot snapshot)
        {
            _fake.Next = snapshot;
            ArcadeInput.Update(0.1f);
        }

        private IEnumerator NavStep(bool down)
        {
            Push(down ? JoyDown : JoyUp);
            yield return null; yield return null; // controller moves once and latches
            Push(Neutral);
            yield return null; yield return null; // controller re-arms for the next push
        }

        private IEnumerator WaitForActiveScene(string sceneName, int maxFrames)
        {
            int f = 0;
            while (SceneManager.GetActiveScene().name != sceneName && f < maxFrames)
            {
                yield return null;
                f++;
            }
            Assert.AreEqual(sceneName, SceneManager.GetActiveScene().name,
                $"Active scene did not become '{sceneName}' within {maxFrames} frames.");
            yield return null;
        }

        private static void AssertOnScreen(Text label, string because)
        {
            Assert.IsTrue(label.gameObject.activeInHierarchy, $"{because}: not active in hierarchy.");
            Rect r = label.rectTransform.rect;
            Assert.Greater(r.width, 1f, $"{because}: no visible width.");
            Assert.Greater(r.height, 1f, $"{because}: no visible height.");

            var corners = new Vector3[4];
            label.rectTransform.GetWorldCorners(corners);
            float xMin = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float xMax = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float yMin = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float yMax = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float overlapW = Mathf.Min(xMax, Screen.width) - Mathf.Max(xMin, 0f);
            float overlapH = Mathf.Min(yMax, Screen.height) - Mathf.Max(yMin, 0f);
            Assert.Greater(overlapW, 1f, $"{because}: lies outside the screen horizontally.");
            Assert.Greater(overlapH, 1f, $"{because}: lies outside the screen vertically.");
        }

        [UnityTest]
        public IEnumerator Menu_RendersFounderLayout_NavigatesAndWraps()
        {
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;
            TakeOverInput();

            var menu = Object.FindAnyObjectByType<HubMenuController>();
            Assert.IsNotNull(menu, "HubMenuController missing from HubMenu scene.");
            Assert.AreEqual(7, menu.RowCount, "Expected 7 menu rows from the shipped config.");

            // Founder's layout order; each row is visible AND shows its own game name (one-hot content).
            string[] expected = { "Endless Sisyphus", "Последняя смена (Factory)", "Life Choices",
                                  "Arcade Prototype", "Медитация", "Lady Bug", "Home Alone" };
            for (int i = 0; i < 7; i++)
            {
                AssertOnScreen(menu.Rows[i], $"Row {i}");
                StringAssert.Contains(expected[i], menu.Rows[i].text, $"Row {i} shows the wrong game.");
                for (int j = 0; j < 7; j++)
                    if (j != i)
                        StringAssert.DoesNotContain(expected[j], menu.Rows[i].text,
                            $"Row {i} must not show row {j}'s game name.");
            }

            // Five installed rows read [ PLAY ], two read [ soon ]; Test Game is gone from the menu.
            int play = 0, soon = 0;
            for (int i = 0; i < 7; i++)
            {
                if (menu.Rows[i].text.Contains("[ PLAY ]")) play++;
                if (menu.Rows[i].text.Contains("[ soon ]")) soon++;
                StringAssert.DoesNotContain("Test Game", menu.Rows[i].text, "Test Game must not appear in the menu.");
            }
            Assert.AreEqual(5, play, "Five installed games show [ PLAY ].");
            Assert.AreEqual(2, soon, "Two 'soon' placeholders show [ soon ].");

            Assert.AreEqual(0, menu.SelectedIndex, "Menu should start on the first slot.");

            // Navigation moves the cursor (and wraps).
            yield return NavStep(down: true);
            yield return NavStep(down: true);
            yield return NavStep(down: true);
            Assert.AreEqual(3, menu.SelectedIndex, "Three down steps should land on slot 3.");

            yield return NavStep(down: false);
            yield return NavStep(down: false);
            yield return NavStep(down: false);
            Assert.AreEqual(0, menu.SelectedIndex, "Three up steps should return to slot 0.");
        }

        [UnityTest]
        public IEnumerator BuiltInTestGame_StillLoopsCleanly_ViaHoldToLaunch_InjectedConfig()
        {
            // Test Game left the shipped menu but its scene + rig stay in the project. Decision (with
            // instant-select gone): the rig is exercised through the SAME hold-to-launch path as every
            // real game — a config injected into the scene's real HoldToLaunchController binds TestGame
            // to the crank; cranking to full launches it, MENU returns (TestGame returns itself).
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;
            TakeOverInput();

            var htl = Object.FindAnyObjectByType<HoldToLaunchController>();
            Assert.IsNotNull(htl, "HoldToLaunchController must be wired into the HubMenu scene.");
            htl.AutoTick = false;
            htl.InitializeWith(new LauncherConfig
            {
                slots = new List<LauncherSlot>
                {
                    new LauncherSlot { controlName = "Crank", displayName = "Test Game",
                                       entryScene = HubScenes.TestGame, status = "installed" },
                    new LauncherSlot { controlName = "BangButton", displayName = "Placeholder",
                                       entryScene = "", status = "soon" },
                }
            });

            // Crank the injected Test Game slot to full charge — the only launch gesture.
            int guard = 0;
            while (SceneManager.GetActiveScene().name != HubScenes.TestGame && guard < 200)
            {
                _fake.Next = new BackendSnapshot { CrankDeltaDegrees = 10f };
                ArcadeInput.Update(0.1f);
                htl.Tick(0.1f);
                yield return null;
                guard++;
            }
            yield return WaitForActiveScene(HubScenes.TestGame, 5);
            TakeOverInput();
            Push(Neutral);
            yield return null;

            var game = Object.FindAnyObjectByType<TestGameController>();
            Assert.IsNotNull(game, "TestGameController missing after entering TestGame.");
            Assert.AreEqual(1, TestGameController.InstanceCount, "Exactly one test game should be alive.");

            // MENU returns to the launcher (TestGame returns itself; no watchdog is armed for it).
            Push(MenuHeld);
            yield return WaitForActiveScene(HubScenes.HubMenu, 120);
            TakeOverInput();
            Push(Neutral);
            yield return null;

            Assert.AreEqual(0, TestGameController.InstanceCount,
                "Returning to the menu must destroy the test game (no leak).");
            Assert.IsNotNull(Object.FindAnyObjectByType<HubMenuController>(),
                "The shipped launcher menu is back.");
        }
    }
}
