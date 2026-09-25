using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using AiGameStudio.ArcadeControls;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// The cabinet's ONE input grabber, across the launcher's REAL scene transitions. This is the live-stand
    /// bug the founder reported (2026-09): «всё время задержка в поиске ардуино… сделать так, чтобы он это
    /// делал только один раз при запуске игры».
    ///
    /// What it was: the <see cref="ArcadeInputRunner"/> was a plain component in <c>HubMenu.unity</c>. Every
    /// scene change destroyed it, which disposed the <see cref="SerialBackend"/> — closing the boards' ports
    /// and stopping the scan thread — and the next scene built a fresh one that re-ran the whole port scan
    /// and handshake. A board that resets when its port opens needs ~1.5–2 s before it answers, times every
    /// candidate port: seconds of dead controls on EVERY launch and EVERY return to the menu.
    ///
    /// What it is now: the grabber lives on a DontDestroyOnLoad object for the whole process. These tests
    /// drive the two transitions that used to kill it (menu → game scene, game scene → menu) and assert the
    /// grabber AND ITS BACKEND INSTANCE come out the other side unchanged — same instance means the serial
    /// reader was never rebuilt, which is exactly "the boards are found once per cabinet start". Plus the two
    /// invariants that must not regress: the hub's DDOL janitor must not sweep the grabber, and there must
    /// never be two grabbers (a second pump per frame doubles crank speed — the cabinet's known disease).
    /// </summary>
    public class InputGrabberLifetimePlayModeTests
    {
        // Counts polls so a doubled pump cannot hide behind identical values.
        private sealed class CountingBackend : IArcadeBackend
        {
            public int Polls;
            public BackendSnapshot Next;
            public BackendSnapshot Poll(float deltaTime) { Polls++; return Next; }
        }

        [SetUp]
        public void SetUp()
        {
            // No grabber from a previous test class: these tests count live runners.
            ArcadeInputRunner.Shutdown();
            foreach (var stray in Object.FindObjectsByType<ArcadeInputRunner>())
                Object.DestroyImmediate(stray);   // its OnDestroy releases the boards and clears Live
        }

        [TearDown]
        public void TearDown()
        {
            ArcadeInputRunner.Shutdown();   // release the ports + scan thread
        }

        private static int LiveRunners() =>
            Object.FindObjectsByType<ArcadeInputRunner>().Length;

        [UnityTest]
        public IEnumerator MenuEntry_BuildsExactlyOneGrabber_OutsideEveryScene()
        {
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;

            ArcadeInputRunner grabber = ArcadeInputRunner.Live;
            Assert.IsNotNull(grabber, "Entering the menu must bring the cabinet's input grabber up.");
            Assert.IsTrue(grabber.IsGrabber);
            Assert.AreEqual(ArcadeInputRunner.GrabberObjectName, grabber.gameObject.name);
            Assert.AreEqual("DontDestroyOnLoad", grabber.gameObject.scene.name,
                "A grabber that belongs to the menu scene dies with it — and takes the open ports with it.");
            Assert.AreEqual(1, LiveRunners(), "Exactly one runner in the process (one port owner, one pump).");
            Assert.IsInstanceOf<CompositeBackend>(ArcadeInput.Backend,
                "The cabinet runs on the composite backend: keyboard always live, boards overlaid.");
        }

        [UnityTest]
        public IEnumerator LaunchAndReturn_KeepTheSameGrabberAndTheSameBackend_NoRescan()
        {
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;

            ArcadeInputRunner grabber = ArcadeInputRunner.Live;
            Assert.IsNotNull(grabber);
            IArcadeBackend backend = ArcadeInput.Backend;
            SerialBackend serial = grabber.Serial;
            Assert.IsNotNull(serial, "The grabber owns the serial board reader.");

            // Menu → game: the destructive single-mode load the launcher does on every launch. TestGame is
            // the built-in rig, so this is the transition itself with no external package in the way. Note
            // that TestGame.unity ALSO carries a runner component — it must stand down, not take over.
            yield return SceneManager.LoadSceneAsync(HubScenes.TestGame, LoadSceneMode.Single);
            yield return null;
            yield return null;

            Assert.AreSame(grabber, ArcadeInputRunner.Live, "The grabber must survive the launch.");
            Assert.AreSame(backend, ArcadeInput.Backend,
                "Same backend INSTANCE inside the game: nothing was rebuilt, so no port was closed and no scan restarted.");
            Assert.AreSame(serial, grabber.Serial, "The same serial reader — the boards are NOT re-scanned on launch.");
            Assert.AreEqual(1, LiveRunners(), "The game scene's own runner must stand down, not become a second grabber.");

            // Game → menu: the return path (the second place the old code re-scanned, which is why coming
            // back to the launcher was slow too). HubMenuController.Start also runs its DDOL janitor here.
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;
            yield return null;

            Assert.AreSame(grabber, ArcadeInputRunner.Live, "The grabber must survive the return to the launcher.");
            Assert.AreSame(backend, ArcadeInput.Backend, "Same backend INSTANCE back in the launcher — no re-scan.");
            Assert.AreSame(serial, grabber.Serial);
            Assert.AreEqual(1, LiveRunners());
        }

        [UnityTest]
        public IEnumerator DdolJanitor_NeverSweepsTheGrabber()
        {
            // The janitor sweeps FOREIGN DontDestroyOnLoad roots left by external games. The grabber now
            // lives in exactly that scene, so a whitelist mistake would silently kill the cabinet's input
            // on every menu entry.
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;

            ArcadeInputRunner grabber = ArcadeInputRunner.Live;
            Assert.IsNotNull(grabber);

            HubDdolJanitor.CleanForeigners();
            yield return null;   // Destroy is deferred — let the frame close
            yield return null;

            Assert.IsTrue(grabber != null, "The janitor must not sweep the cabinet's input grabber.");
            Assert.AreSame(grabber, ArcadeInputRunner.Live);
            Assert.AreEqual(1, LiveRunners());
        }

        [UnityTest]
        public IEnumerator ArmingTheReturnWatchdog_ReusesTheGrabber_AndDoesNotDoubleThePump()
        {
            // The watchdog used to carry its OWN ArcadeInputRunner for nativeInput slots: a second serial
            // owner on the same ports and, while the menu scene was still up, a second pump per frame.
            yield return SceneManager.LoadSceneAsync(HubScenes.HubMenu, LoadSceneMode.Single);
            yield return null;

            ArcadeInputRunner grabber = ArcadeInputRunner.Live;
            Assert.IsNotNull(grabber);

            var counting = new CountingBackend();
            ArcadeInput.Initialize(counting);   // swap the backend only; the live grabber keeps pumping
            yield return null;

            LauncherReturn.ArmFor(HubScenes.HubMenu);
            try
            {
                yield return null;

                Assert.IsNotNull(LauncherReturn.Instance, "Sanity: the watchdog is armed.");
                Assert.IsNull(LauncherReturn.Instance.GetComponent<ArcadeInputRunner>(),
                    "The watchdog must not carry an input pump of its own any more.");
                Assert.AreSame(grabber, ArcadeInputRunner.Live, "Arming must reuse the live grabber.");
                Assert.AreSame(counting, ArcadeInput.Backend,
                    "Arming must not re-Initialize ArcadeInput (that is what re-scanned the boards).");
                Assert.AreEqual(1, LiveRunners());

                int before = counting.Polls;
                yield return null;
                Assert.AreEqual(before + 1, counting.Polls,
                    "ONE pump per frame with the watchdog armed — two would make the crank spin twice as fast.");
            }
            finally
            {
                if (LauncherReturn.Instance != null) Object.DestroyImmediate(LauncherReturn.Instance.gameObject);
            }
        }
    }
}
