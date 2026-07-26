using NUnit.Framework;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// Full coverage of the pure charge state machine: growth, decay, "release does not accumulate
    /// backwards", exclusivity, and the single launch edge.
    /// </summary>
    public class LaunchChargeMachineTests
    {
        private static LaunchTuning Tuning(float charge = 5f, float decay = 1f) =>
            new LaunchTuning { chargeSeconds = charge, decaySeconds = decay };

        private static LaunchChargeMachine Machine(float charge = 5f, float decay = 1f) =>
            new LaunchChargeMachine(Tuning(charge, decay));

        private static bool[] Engage(int count, params int[] on)
        {
            var a = new bool[count];
            foreach (int i in on) a[i] = true;
            return a;
        }

        [Test]
        public void Charge_GrowsToFull_OverChargeSeconds()
        {
            var m = Machine(charge: 5f);
            var e = Engage(3, 0);

            int fired = -1;
            for (int i = 0; i < 60; i++) // >= chargeSeconds of holding (extra frames guard float rounding)
            {
                int r = m.Tick(e, 0.1f);
                if (r >= 0) fired = r;
            }

            Assert.AreEqual(1f, m.Charge, 1e-4, "should be fully charged after chargeSeconds of holding");
            Assert.AreEqual(0, fired, "reaching full should report slot 0 to launch");
        }

        [Test]
        public void Charge_Halfway_AtHalfTheChargeTime()
        {
            var m = Machine(charge: 5f);
            var e = Engage(1, 0);

            for (int i = 0; i < 25; i++) m.Tick(e, 0.1f); // 2.5s

            Assert.AreEqual(0.5f, m.Charge, 1e-3);
            Assert.AreEqual(0, m.ActiveSlot);
        }

        [Test]
        public void Release_DecaysToZero_OverDecaySeconds_AndDropsSlot()
        {
            var m = Machine(charge: 5f, decay: 1f);
            var held = Engage(1, 0);
            var released = Engage(1); // nothing engaged

            for (int i = 0; i < 25; i++) m.Tick(held, 0.1f); // charge to 0.5
            Assert.AreEqual(0.5f, m.Charge, 1e-3);

            // 0.5 charge, decay 1s => ~0.5s to reach 0; a couple extra ticks guard float rounding.
            for (int i = 0; i < 8; i++) m.Tick(released, 0.1f);

            Assert.AreEqual(0f, m.Charge, 1e-4, "should have fully decayed");
            Assert.AreEqual(-1, m.ActiveSlot, "the slot is dropped once charge hits zero");
            Assert.IsFalse(m.IsActive);
        }

        [Test]
        public void Decay_Shape_IsLinearAtExactlyDecaySeconds()
        {
            // Pins the decay RATE, not just "reaches zero eventually": with decaySeconds=1 a full
            // charge must sit at 0.75 after 0.25s, 0.5 after 0.5s, 0.25 after 0.75s, 0 at 1.0s.
            var m = Machine(charge: 5f, decay: 1f);
            var held = Engage(1, 0);
            var released = Engage(1);

            for (int i = 0; i < 60; i++) m.Tick(held, 0.1f); // charge to full (clamped at 1.0)
            Assert.AreEqual(1f, m.Charge, 1e-4);

            for (int i = 0; i < 5; i++) m.Tick(released, 0.05f); // 0.25s released
            Assert.AreEqual(0.75f, m.Charge, 1e-3, "after 0.25s of a 1s decay: 0.75");

            for (int i = 0; i < 5; i++) m.Tick(released, 0.05f); // 0.5s
            Assert.AreEqual(0.5f, m.Charge, 1e-3, "after 0.5s of a 1s decay: 0.5");

            for (int i = 0; i < 5; i++) m.Tick(released, 0.05f); // 0.75s
            Assert.AreEqual(0.25f, m.Charge, 1e-3, "after 0.75s of a 1s decay: 0.25");

            for (int i = 0; i < 5; i++) m.Tick(released, 0.05f); // 1.0s
            Assert.AreEqual(0f, m.Charge, 1e-3, "empty at exactly decaySeconds, not later");

            m.Tick(released, 0.05f); // one frame past zero (absorbs float residue)
            Assert.AreEqual(-1, m.ActiveSlot, "slot dropped once the decay completes");
        }

        [Test]
        public void Growth_Shape_IsLinearAtExactlyChargeSeconds()
        {
            // Pins the growth RATE: with chargeSeconds=5, 1s of holding == 0.2, 2s == 0.4, 4s == 0.8.
            var m = Machine(charge: 5f);
            var e = Engage(1, 0);

            for (int i = 0; i < 20; i++) m.Tick(e, 0.05f); // 1.0s
            Assert.AreEqual(0.2f, m.Charge, 1e-3, "1s / 5s = 0.2");

            for (int i = 0; i < 20; i++) m.Tick(e, 0.05f); // 2.0s
            Assert.AreEqual(0.4f, m.Charge, 1e-3, "2s / 5s = 0.4");

            for (int i = 0; i < 40; i++) m.Tick(e, 0.05f); // 4.0s
            Assert.AreEqual(0.8f, m.Charge, 1e-3, "4s / 5s = 0.8");
        }

        [Test]
        public void Release_DoesNotAccumulateBackwards_AFreshHoldStartsFromZero()
        {
            var m = Machine(charge: 5f, decay: 1f);
            var held0 = Engage(2, 0);
            var released = Engage(2);

            for (int i = 0; i < 20; i++) m.Tick(held0, 0.1f); // 0.4
            Assert.AreEqual(0.4f, m.Charge, 1e-3);

            for (int i = 0; i < 5; i++) m.Tick(released, 0.1f); // decay to 0, drops slot
            Assert.AreEqual(0f, m.Charge, 1e-4);
            Assert.AreEqual(-1, m.ActiveSlot);

            m.Tick(held0, 0.1f); // re-engage one frame
            Assert.AreEqual(0.02f, m.Charge, 1e-4, "a new hold starts from 0, not from the old 0.4");
        }

        [Test]
        public void Exclusivity_FirstEngagedSlotBlocksOthers_UntilItDecays()
        {
            var m = Machine(charge: 5f, decay: 1f);
            var both = Engage(6, 2, 5); // slots 2 and 5 both engaged

            m.Tick(both, 0.1f);
            Assert.AreEqual(2, m.ActiveSlot, "the lower-index engaged slot acquires charge");

            for (int i = 0; i < 10; i++) m.Tick(both, 0.1f);
            Assert.AreEqual(2, m.ActiveSlot, "slot 5 stays blocked while slot 2 is charging");

            // Release slot 2 but keep slot 5 held: slot 5 is STILL blocked until slot 2 decays to 0.
            var only5 = Engage(6, 5);
            Assert.AreEqual(2, m.ActiveSlot);
            m.Tick(only5, 0.1f);
            Assert.AreEqual(2, m.ActiveSlot, "slot 5 cannot take over while slot 2 still has charge");

            // Drain slot 2 fully, then slot 5 finally acquires.
            for (int i = 0; i < 30; i++) m.Tick(only5, 0.1f);
            Assert.AreEqual(5, m.ActiveSlot, "after slot 2 decays to 0, slot 5 acquires");
            Assert.Greater(m.Charge, 0f);
        }

        [Test]
        public void Exclusivity_LowerIndexWins_OnSimultaneousEngage()
        {
            var m = Machine();
            var both = Engage(5, 1, 3);
            m.Tick(both, 0.1f);
            Assert.AreEqual(1, m.ActiveSlot);
        }

        [Test]
        public void Launch_FiresExactlyOnce_WhileHeldAtFull()
        {
            var m = Machine(charge: 5f);
            var e = Engage(1, 0);

            int fires = 0;
            for (int i = 0; i < 80; i++) // well past full, still holding
                if (m.Tick(e, 0.1f) >= 0) fires++;

            Assert.AreEqual(1, fires, "the launch edge fires once, not every frame at full");
            Assert.AreEqual(1f, m.Charge, 1e-4);
        }

        [Test]
        public void Reset_AfterFull_AllowsReacquireFromZero()
        {
            var m = Machine(charge: 5f);
            var e = Engage(2, 1);

            for (int i = 0; i < 60; i++) m.Tick(e, 0.1f); // full on slot 1
            Assert.AreEqual(1f, m.Charge, 1e-4);

            m.Reset();
            Assert.AreEqual(0f, m.Charge);
            Assert.AreEqual(-1, m.ActiveSlot);

            m.Tick(e, 0.1f); // still engaged -> reacquire, fresh charge
            Assert.AreEqual(1, m.ActiveSlot);
            Assert.AreEqual(0.02f, m.Charge, 1e-4);
        }

        [Test]
        public void NothingEngaged_StaysIdle()
        {
            var m = Machine();
            var none = Engage(4);
            int r = m.Tick(none, 0.1f);
            Assert.AreEqual(-1, r);
            Assert.AreEqual(-1, m.ActiveSlot);
            Assert.AreEqual(0f, m.Charge);
        }
    }
}
