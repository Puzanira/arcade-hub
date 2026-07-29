using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using AiGameStudio.ArcadeControls;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// The built-in Test Game rig loop on the attract screen. The old menu list + joystick navigation are
    /// REMOVED (attract-video-screen increment; their render/navigate tests went with them — recorded in
    /// the increment's §прогресса; the video/rain surface is covered by <c>AttractVideoPlayModeTests</c>).
    /// What remains here: the Test Game rig (kept in the project though it left the shipped config) still
    /// loops cleanly through the ONLY launch path — a config injected into the scene's real
    /// HoldToLaunchController binds TestGame to the crank; crank to full → launch, MENU returns.
    /// </summary>
    public class HubFlowPlayModeTests
    {
        private FakeBackend _fake;

        private static readonly BackendSnapshot Neutral = default;
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

        [UnityTest]
        public IEnumerator BuiltInTestGame_StillLoopsCleanly_ViaHoldToLaunch_InjectedConfig()
        {
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
                "The attract-screen host is back.");
        }
    }
}
