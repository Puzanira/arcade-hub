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
    /// Drives the whole launcher loop headless with a <see cref="FakeBackend"/> (no keyboard):
    /// HubMenu shows 7 visible rows, navigation moves the cursor, RED enters the TestGame scene, MENU
    /// returns, and a second entry is a clean start (no state leak, no DontDestroyOnLoad survivor).
    /// </summary>
    public class HubFlowPlayModeTests
    {
        private FakeBackend _fake;

        private static readonly BackendSnapshot Neutral = default;
        private static BackendSnapshot JoyUp => new BackendSnapshot { Joystick = new Vector2(0f, 1f) };
        private static BackendSnapshot JoyDown => new BackendSnapshot { Joystick = new Vector2(0f, -1f) };
        private static BackendSnapshot RedHeld => new BackendSnapshot { RedHeld = true };
        private static BackendSnapshot MenuHeld => new BackendSnapshot { MenuHeld = true };

        // Replace the scene's real keyboard input with a code-driven fake and pump it ourselves.
        private void TakeOverInput()
        {
            var runner = Object.FindAnyObjectByType<ArcadeInputRunner>();
            if (runner != null) Object.DestroyImmediate(runner);
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
            yield return null; // let the new scene's Awake/Start settle
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
        public IEnumerator HubLoop_ShowsMenu_EntersTestGame_ReturnsClean()
        {
            // --- HubMenu comes up with 7 visible, correctly-labelled rows ---
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;
            TakeOverInput();

            var menu = Object.FindAnyObjectByType<HubMenuController>();
            Assert.IsNotNull(menu, "HubMenuController missing from HubMenu scene.");
            Assert.AreEqual(7, menu.RowCount, "Expected 7 menu rows from the shipped config.");

            // Each row is visible AND shows its own game name (one-hot content: a crossed row fails).
            string[] expected = { "Test Game", "Home Alone", "Life Choices", "Factory Game",
                                  "Lady Bug", "Endless Sisyphus", "Arcade Prototype" };
            for (int i = 0; i < 7; i++)
            {
                AssertOnScreen(menu.Rows[i], $"Row {i}");
                StringAssert.Contains(expected[i], menu.Rows[i].text, $"Row {i} shows the wrong game.");
                for (int j = 0; j < 7; j++)
                    if (j != i)
                        StringAssert.DoesNotContain(expected[j], menu.Rows[i].text,
                            $"Row {i} must not show row {j}'s game name.");
            }

            Assert.AreEqual(0, menu.SelectedIndex, "Menu should start on the first slot.");

            // --- Navigation moves the cursor (and wraps) ---
            yield return NavStep(down: true);
            yield return NavStep(down: true);
            yield return NavStep(down: true);
            Assert.AreEqual(3, menu.SelectedIndex, "Three down steps should land on slot 3.");

            yield return NavStep(down: false);
            yield return NavStep(down: false);
            yield return NavStep(down: false);
            Assert.AreEqual(0, menu.SelectedIndex, "Three up steps should return to slot 0 (the test game).");

            // --- RED enters the installed TestGame scene ---
            Push(RedHeld);
            yield return WaitForActiveScene(HubScenes.TestGame, 120);
            TakeOverInput();
            Push(Neutral);
            yield return null;

            var game = Object.FindAnyObjectByType<TestGameController>();
            Assert.IsNotNull(game, "TestGameController missing after entering TestGame.");
            Assert.AreEqual(1, TestGameController.InstanceCount, "Exactly one test game should be alive.");

            var title = GameObject.Find("TestGameTitle");
            Assert.IsNotNull(title, "TestGame title label missing.");
            AssertOnScreen(title.GetComponent<Text>(), "TestGame title");

            // --- MENU returns cleanly to the launcher ---
            Push(MenuHeld);
            yield return WaitForActiveScene(HubScenes.HubMenu, 120);
            TakeOverInput();
            Push(Neutral);
            yield return null;

            Assert.AreEqual(0, TestGameController.InstanceCount,
                "Returning to the menu must destroy the test game (no leak).");
            var menu2 = Object.FindAnyObjectByType<HubMenuController>();
            Assert.IsNotNull(menu2, "HubMenuController missing after returning from TestGame.");
            Assert.AreEqual(0, menu2.SelectedIndex, "A fresh menu starts on slot 0 again.");

            // --- Second entry is a clean start: still exactly one instance, no menu survivor ---
            Push(RedHeld);
            yield return WaitForActiveScene(HubScenes.TestGame, 120);
            TakeOverInput();
            Push(Neutral);
            yield return null;

            Assert.AreEqual(1, TestGameController.InstanceCount,
                "Re-entering TestGame must be a clean start (count 1, never 2).");
            Assert.AreEqual(0, Object.FindObjectsByType<HubMenuController>(FindObjectsSortMode.None).Length,
                "No launcher menu should survive into the game scene (no DontDestroyOnLoad leak).");

            // Land back in the menu so the test leaves a clean scene.
            Push(MenuHeld);
            yield return WaitForActiveScene(HubScenes.HubMenu, 120);
        }
    }
}
