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

        /// <summary>
        /// relayout-v1 (founder, 2026-08-07): Lady Bug hangs on "любой из двух датчиков высоты" — ONE slot,
        /// two sensors. Either palm engages it, by the SAME threshold a single-sensor slot uses.
        /// </summary>
        [Test]
        public void Height_EitherSensor_EngagesTheSameSlot()
        {
            var s = new LaunchInputSampler(Slots("Height"), Tuning());
            Assert.AreEqual(LaunchControl.Height, s.ControlAt(0), "\"Height\" binds the either-sensor control.");

            Assert.IsTrue(SampleOne(s, new ControlReadings { HeightA = 0.51f, HeightB = 0f }, 0.1f),
                "A palm over sensor A alone engages the slot.");
            Assert.IsTrue(SampleOne(s, new ControlReadings { HeightA = 0f, HeightB = 0.51f }, 0.1f),
                "A palm over sensor B alone engages it just as well.");
            Assert.IsTrue(SampleOne(s, new ControlReadings { HeightA = 0.9f, HeightB = 0.8f }, 0.1f),
                "Both hands down: still engaged (one boolean, never a doubled signal).");
        }

        [Test]
        public void Height_EitherSensor_UsesTheSameThreshold_AndNeitherSensorAloneSneaksUnderIt()
        {
            var s = new LaunchInputSampler(Slots("Height"), Tuning());
            Assert.IsFalse(SampleOne(s, new ControlReadings { HeightA = 0.49f, HeightB = 0.49f }, 0.1f),
                "Two sub-threshold sensors must NOT add up to an engagement — the rule is max, not sum.");
            Assert.IsTrue(SampleOne(s, new ControlReadings { HeightA = 0.49f, HeightB = 0.51f }, 0.1f),
                "…and the louder of the two decides, exactly as for a single sensor.");
        }

        [Test]
        public void Height_EitherSensor_DisengagesOnlyWhenBothAreReleased()
        {
            var s = new LaunchInputSampler(Slots("Height"), Tuning());
            Assert.IsTrue(SampleOne(s, new ControlReadings { HeightA = 1f, HeightB = 1f }, 0.1f));
            Assert.IsTrue(SampleOne(s, new ControlReadings { HeightA = 0f, HeightB = 1f }, 0.1f),
                "Lifting one hand keeps the charge alive while the other stays down.");
            Assert.IsFalse(SampleOne(s, new ControlReadings { HeightA = 0f, HeightB = 0f }, 0.1f),
                "Engagement drops only once BOTH sensors are released.");
        }

        /// <summary>
        /// The founder-facing half of "не в два раза быстрее": engagement is a boolean, so the charge
        /// machine advances at exactly one rate no matter how many sensors are covered. Driven through the
        /// real <see cref="LaunchChargeMachine"/>, because that is where a "two signals = twice as fast"
        /// mistake would actually show up.
        /// </summary>
        [Test]
        public void Height_BothSensorsHeld_ChargeAtTheSameRateAsOne()
        {
            float ChargeAfter(ControlReadings held, int frames)
            {
                var sampler = new LaunchInputSampler(Slots("Height"), Tuning());
                var machine = new LaunchChargeMachine(new LaunchTuning { chargeSeconds = 5f, decaySeconds = 1f });
                var engaged = new bool[sampler.Count];
                for (int i = 0; i < frames; i++)
                {
                    sampler.Sample(held, 0.1f, engaged);
                    machine.Tick(engaged, 0.1f);
                }
                return machine.Charge;
            }

            float oneSensor = ChargeAfter(new ControlReadings { HeightA = 1f }, 25);
            float bothSensors = ChargeAfter(new ControlReadings { HeightA = 1f, HeightB = 1f }, 25);

            Assert.AreEqual(0.5f, oneSensor, 0.02f, "2.5s of a 5s charge is half the bar.");
            Assert.AreEqual(oneSensor, bothSensors, 1e-5f,
                "Both hands down must charge at the SAME rate as one — never twice as fast.");
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
