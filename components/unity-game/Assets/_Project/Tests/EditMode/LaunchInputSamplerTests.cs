using System.Collections.Generic;
using NUnit.Framework;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// Per-control-kind engagement rules: buttons held, height/joystick thresholds, and the crank's
    /// |degrees|&gt;threshold with a decay timeout that bridges the gaps between discrete turns.
    /// </summary>
    public class LaunchInputSamplerTests
    {
        private static List<LauncherSlot> Slots(params string[] controlNames)
        {
            var list = new List<LauncherSlot>();
            foreach (string c in controlNames)
                list.Add(new LauncherSlot { controlName = c, displayName = c, status = "planned" });
            return list;
        }

        private static LaunchTuning Tuning() => new LaunchTuning
        {
            joystickDeflectThreshold = 0.5f,
            crankDegreesThreshold = 2.0f,
            crankActiveTimeout = 0.25f,
            heightEngageThreshold = 0.5f,
        };

        private static bool SampleOne(LaunchInputSampler s, ControlReadings r, float dt)
        {
            var e = new bool[s.Count];
            s.Sample(r, dt, e);
            return e[0];
        }

        [Test]
        public void Button_EngagedOnlyWhenHeld()
        {
            var s = new LaunchInputSampler(Slots("RedButton"), Tuning());
            Assert.IsFalse(SampleOne(s, new ControlReadings { RedHeld = false }, 0.1f));
            Assert.IsTrue(SampleOne(s, new ControlReadings { RedHeld = true }, 0.1f));
        }

        [Test]
        public void Height_EngagedAboveThreshold_Only()
        {
            var s = new LaunchInputSampler(Slots("HeightA"), Tuning());
            Assert.IsFalse(SampleOne(s, new ControlReadings { HeightA = 0.49f }, 0.1f));
            Assert.IsTrue(SampleOne(s, new ControlReadings { HeightA = 0.51f }, 0.1f));
        }

        [Test]
        public void Joystick_EngagedByDeflectionMagnitude()
        {
            var s = new LaunchInputSampler(Slots("Joystick"), Tuning());
            // magnitude 0.4 < 0.5 -> not engaged
            Assert.IsFalse(SampleOne(s, new ControlReadings { JoystickX = 0.4f, JoystickY = 0f }, 0.1f));
            // magnitude sqrt(0.4^2+0.4^2)=0.566 > 0.5 -> engaged
            Assert.IsTrue(SampleOne(s, new ControlReadings { JoystickX = 0.4f, JoystickY = 0.4f }, 0.1f));
        }

        [Test]
        public void Crank_EngagedWhenTurningAboveThreshold()
        {
            var s = new LaunchInputSampler(Slots("Crank"), Tuning());
            Assert.IsTrue(SampleOne(s, new ControlReadings { CrankDeltaDegrees = 5f }, 0.1f));
        }

        [Test]
        public void Crank_BelowThreshold_NotEngaged()
        {
            var s = new LaunchInputSampler(Slots("Crank"), Tuning());
            Assert.IsFalse(SampleOne(s, new ControlReadings { CrankDeltaDegrees = 1f }, 0.1f));
        }

        [Test]
        public void Crank_StaysEngaged_ThroughShortGaps_ThenDropsAfterTimeout()
        {
            var s = new LaunchInputSampler(Slots("Crank"), Tuning());

            // A qualifying turn latches "active" for crankActiveTimeout (0.25s).
            Assert.IsTrue(SampleOne(s, new ControlReadings { CrankDeltaDegrees = 5f }, 0.1f));

            // A sub-threshold frame 0.1s later: still within the 0.25s timeout -> stays engaged.
            Assert.IsTrue(SampleOne(s, new ControlReadings { CrankDeltaDegrees = 0f }, 0.1f));

            // Another 0.1s (0.2s total) -> still within timeout.
            Assert.IsTrue(SampleOne(s, new ControlReadings { CrankDeltaDegrees = 0f }, 0.1f));

            // Another 0.1s (0.3s total, past 0.25s) -> now dropped.
            Assert.IsFalse(SampleOne(s, new ControlReadings { CrankDeltaDegrees = 0f }, 0.1f));
        }

        [Test]
        public void UnknownControl_NeverEngaged()
        {
            var s = new LaunchInputSampler(Slots("Nonsense"), Tuning());
            Assert.AreEqual(LaunchControl.None, s.ControlAt(0));
            Assert.IsFalse(SampleOne(s, new ControlReadings { RedHeld = true, HeightA = 1f, CrankDeltaDegrees = 90f }, 0.1f));
        }

        [Test]
        public void Sample_MapsEachSlotToItsOwnControl()
        {
            var s = new LaunchInputSampler(Slots("Crank", "RedButton", "GreenButton"), Tuning());
            var e = new bool[3];
            s.Sample(new ControlReadings { RedHeld = false, GreenHeld = true, CrankDeltaDegrees = 10f }, 0.1f, e);
            Assert.IsTrue(e[0], "crank turning -> slot 0 engaged");
            Assert.IsFalse(e[1], "red not held -> slot 1 not engaged");
            Assert.IsTrue(e[2], "green held -> slot 2 engaged");
        }
    }
}
