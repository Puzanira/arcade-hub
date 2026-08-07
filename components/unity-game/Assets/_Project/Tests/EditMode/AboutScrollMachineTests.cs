using NUnit.Framework;
using UnityEngine;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// The about document's joystick scroll, engine-free: a deflection ramps a velocity (so the page
    /// leans into the push and glides to a stop instead of snapping — the founder asked for «плавный»),
    /// the deadzone is re-scaled rather than cut, and the position is clamped to the document at both
    /// ends with no stored-up momentum to unwind.
    /// </summary>
    public class AboutScrollMachineTests
    {
        private const float Dt = 1f / 60f;

        private static AboutScrollMachine Machine(float maxPosition = 4000f)
        {
            return new AboutScrollMachine { MaxPosition = maxPosition };
        }

        private static void Run(AboutScrollMachine machine, float axis, float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++) machine.Tick(axis, Dt);
        }

        [Test]
        public void JoystickDown_ScrollsDownTheDocument_UpScrollsBack()
        {
            AboutScrollMachine machine = Machine();
            Assert.AreEqual(0f, machine.Position, 1e-4f);

            Run(machine, -1f, 0.5f); // stick DOWN = further into the story
            Assert.Greater(machine.Position, 100f, "Pushing down must travel down the document.");
            float reached = machine.Position;

            Run(machine, 1f, 1f); // stick UP = back towards the title
            Assert.Less(machine.Position, reached - 100f, "Pushing up must walk the reading position back.");
        }

        [Test]
        public void Scroll_IsRamped_NotInstant()
        {
            AboutScrollMachine machine = Machine();

            machine.Tick(-1f, Dt);
            float firstFrame = Mathf.Abs(machine.Velocity);
            Assert.Less(firstFrame, AboutLayout.ScrollMaxSpeed * 0.5f,
                "One frame of full deflection must NOT be full speed — the page has weight.");

            Run(machine, -1f, 0.5f);
            Assert.AreEqual(AboutLayout.ScrollMaxSpeed, Mathf.Abs(machine.Velocity), 1f,
                "Held down, it does reach the top speed.");

            // Centre the stick: it glides to a stop quickly, and stops — no endless coasting.
            Run(machine, 0f, 0.5f);
            Assert.AreEqual(0f, machine.Velocity, 1e-3f, "A centred stick brings the document to a full stop.");
        }

        [Test]
        public void Deadzone_IgnoresRestWobble_AndDoesNotJumpAtTheThreshold()
        {
            AboutScrollMachine machine = Machine();

            Run(machine, 0.2f, 1f); // a worn stick's rest wobble, under the 0.25 deadzone
            Assert.AreEqual(0f, machine.Position, 1e-4f, "Rest wobble must not scroll the document.");
            Assert.AreEqual(0f, machine.Velocity, 1e-4f);

            // Just past the deadzone the response starts from ~zero rather than at a quarter speed.
            Assert.AreEqual(0f, machine.Deflection(0.25f), 1e-4f);
            Assert.AreEqual(0f, machine.Deflection(-0.25f), 1e-4f);
            Assert.Less(Mathf.Abs(machine.Deflection(0.30f)), 0.1f, "The ramp starts at zero past the deadzone.");
            Assert.AreEqual(1f, machine.Deflection(1f), 1e-4f, "Full deflection is still full speed.");
            Assert.AreEqual(-1f, machine.Deflection(-1f), 1e-4f);
        }

        [Test]
        public void Position_IsClampedToTheDocument_AtBothEnds()
        {
            AboutScrollMachine machine = Machine(maxPosition: 1500f);

            Run(machine, -1f, 10f); // hold down far longer than the document is long
            Assert.AreEqual(1500f, machine.Position, 1e-3f, "The document cannot scroll past its own end.");
            Assert.IsTrue(machine.AtBottom);
            Assert.AreEqual(0f, machine.Velocity, 1e-4f, "Hitting the end kills the velocity — nothing to unwind.");

            // And the moment the stick reverses, the page moves — no dead frames spent draining momentum.
            machine.Tick(1f, Dt);
            machine.Tick(1f, Dt);
            Assert.Less(machine.Position, 1500f, "Reversing at the end moves immediately.");

            Run(machine, 1f, 10f);
            Assert.AreEqual(0f, machine.Position, 1e-3f, "The document cannot scroll above its first line.");
            Assert.IsTrue(machine.AtTop);
            Assert.AreEqual(0f, machine.Velocity, 1e-4f);
        }

        [Test]
        public void ShortDocument_DoesNotScrollAtAll()
        {
            AboutScrollMachine machine = Machine(maxPosition: 0f); // fits on one screen
            Run(machine, -1f, 3f);
            Assert.AreEqual(0f, machine.Position, 1e-4f, "A one-screen document has nowhere to go.");
        }

        [Test]
        public void Reset_ReturnsToTheFirstLine_Motionless()
        {
            AboutScrollMachine machine = Machine();
            Run(machine, -1f, 1f);
            Assert.Greater(machine.Position, 0f);

            machine.Reset();
            Assert.AreEqual(0f, machine.Position, 1e-4f, "Re-entering the screen starts at the top of the story.");
            Assert.AreEqual(0f, machine.Velocity, 1e-4f);
        }

        [Test]
        public void ShippedTuning_IsTheDocumentedFeel()
        {
            Assert.AreEqual(1200f, AboutLayout.ScrollMaxSpeed, 1e-4f, "[tune] top scroll speed, px/s.");
            Assert.AreEqual(4800f, AboutLayout.ScrollAcceleration, 1e-4f, "[tune] ramp-up, px/s² (0.25 s to top speed).");
            Assert.AreEqual(6000f, AboutLayout.ScrollDeceleration, 1e-4f, "[tune] ramp-down, px/s² (0.2 s to a stop).");
            Assert.Greater(AboutLayout.ScrollDeceleration, AboutLayout.ScrollAcceleration,
                "Stopping must be crisper than starting, or the page feels like it is sliding on ice.");
            Assert.AreEqual(0.25f, AboutLayout.ScrollDeadzone, 1e-4f, "[tune] joystick deadzone.");
        }
    }
}
