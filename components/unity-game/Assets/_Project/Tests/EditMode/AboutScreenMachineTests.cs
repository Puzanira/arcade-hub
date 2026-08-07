using NUnit.Framework;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// The «История проекта» screen's open/close rules, engine-free: MENU toggles the document on its
    /// PRESS EDGE (never on the hold), a press held through the scene load cannot open anything, and an
    /// untouched cabinet takes itself back to the attract reel after the inactivity timeout.
    /// </summary>
    public class AboutScreenMachineTests
    {
        private static AboutScreenMachine Armed(float timeout = 60f)
        {
            var machine = new AboutScreenMachine(timeout);
            machine.Tick(menuHeld: false, otherInput: false, dt: 0.016f); // a release seen — the gesture is now live
            Assert.IsTrue(machine.IsArmed);
            return machine;
        }

        [Test]
        public void MenuPressEdge_Opens_AndTheSecondPressCloses()
        {
            AboutScreenMachine machine = Armed();
            Assert.IsFalse(machine.IsOpen);

            Assert.AreEqual(AboutScreenEvent.Opened, machine.Tick(true, false, 0.016f), "The press edge opens it.");
            Assert.IsTrue(machine.IsOpen);

            // Holding the button does NOT re-toggle: only edges count.
            for (int i = 0; i < 30; i++)
                Assert.AreEqual(AboutScreenEvent.None, machine.Tick(true, false, 0.016f));
            Assert.IsTrue(machine.IsOpen, "A held MENU button must not flap the document open and shut.");

            Assert.AreEqual(AboutScreenEvent.None, machine.Tick(false, false, 0.016f), "Releasing changes nothing.");
            Assert.AreEqual(AboutScreenEvent.Closed, machine.Tick(true, false, 0.016f), "The next press closes it.");
            Assert.IsFalse(machine.IsOpen);
        }

        [Test]
        public void MenuHeldThroughEntry_CannotOpenTheDocument()
        {
            // The universal cabinet exit is MENU; a player's finger is very often still on it when the
            // launcher scene comes up. That leftover hold must not fly the document open in his face.
            var machine = new AboutScreenMachine(60f);

            for (int i = 0; i < 60; i++)
                Assert.AreEqual(AboutScreenEvent.None, machine.Tick(true, false, 0.016f));
            Assert.IsFalse(machine.IsOpen, "A press held from before entry is not a gesture.");
            Assert.IsFalse(machine.IsArmed);

            machine.Tick(false, false, 0.016f); // release seen — now it is armed
            Assert.IsTrue(machine.IsArmed);
            Assert.AreEqual(AboutScreenEvent.Opened, machine.Tick(true, false, 0.016f),
                "The first FRESH press after the release opens it.");
        }

        [Test]
        public void Inactivity_ReturnsToTheReel_ByItself()
        {
            AboutScreenMachine machine = Armed(timeout: 2f);
            machine.Tick(true, false, 0.016f);
            Assert.IsTrue(machine.IsOpen);

            for (int i = 0; i < 19; i++) // 1.9 s of nothing
                Assert.AreEqual(AboutScreenEvent.None, machine.Tick(false, false, 0.1f));
            Assert.IsTrue(machine.IsOpen, "Still open just before the timeout.");
            Assert.AreEqual(1.9f, machine.IdleSeconds, 1e-3f);

            Assert.AreEqual(AboutScreenEvent.ClosedByTimeout, machine.Tick(false, false, 0.1f));
            Assert.IsFalse(machine.IsOpen, "An untouched cabinet goes back to the attract reel.");
        }

        [Test]
        public void AnyInput_KeepsTheDocumentOpen()
        {
            AboutScreenMachine machine = Armed(timeout: 2f);
            machine.Tick(true, false, 0.016f);

            // Somebody is scrolling: 10 s of reading must not time out.
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(AboutScreenEvent.None, machine.Tick(false, true, 0.1f));
            Assert.IsTrue(machine.IsOpen, "Reading (any control touched) keeps the document up.");
            Assert.AreEqual(0f, machine.IdleSeconds, 1e-4f);

            // Stop touching it and the timer runs from zero again.
            for (int i = 0; i < 19; i++) machine.Tick(false, false, 0.1f);
            Assert.IsTrue(machine.IsOpen);
            Assert.AreEqual(AboutScreenEvent.ClosedByTimeout, machine.Tick(false, false, 0.1f));
        }

        [Test]
        public void IdleTimer_DoesNotRun_WhileClosed()
        {
            AboutScreenMachine machine = Armed(timeout: 1f);
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(AboutScreenEvent.None, machine.Tick(false, false, 0.1f));
            Assert.AreEqual(0f, machine.IdleSeconds, 1e-4f, "The inactivity timer belongs to the OPEN screen only.");
            Assert.IsFalse(machine.IsOpen);
        }

        [Test]
        public void DefaultTimeout_IsTheShippedSixtySeconds()
        {
            Assert.AreEqual(AboutLayout.IdleReturnSeconds, new AboutScreenMachine().IdleTimeout, 1e-4f);
            Assert.AreEqual(60f, AboutLayout.IdleReturnSeconds, 1e-4f, "[tune] default auto-return.");
        }
    }
}
