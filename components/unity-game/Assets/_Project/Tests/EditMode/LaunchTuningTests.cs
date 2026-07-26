using NUnit.Framework;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// The tuning comes from launcher-config.json (5.0 / 1.0 defaults) and degrades safely: an omitted or
    /// partial "tuning" block falls back to shipped defaults rather than 0-second charges.
    /// </summary>
    public class LaunchTuningTests
    {
        [Test]
        public void Config_WithoutTuningBlock_UsesDefaults()
        {
            LauncherConfig config = LauncherConfig.FromJson(@"{ ""slots"": [] }");
            LaunchTuning t = config.TuningOrDefault();

            Assert.AreEqual(5.0f, t.chargeSeconds, 1e-4);
            Assert.AreEqual(1.0f, t.decaySeconds, 1e-4);
            Assert.AreEqual(0.5f, t.joystickDeflectThreshold, 1e-4);
            Assert.AreEqual(2.0f, t.crankDegreesThreshold, 1e-4);
            Assert.AreEqual(0.25f, t.crankActiveTimeout, 1e-4);
            Assert.AreEqual(0.5f, t.heightEngageThreshold, 1e-4);
        }

        [Test]
        public void Config_WithTuningBlock_ParsesValues()
        {
            LauncherConfig config = LauncherConfig.FromJson(
                @"{ ""slots"": [], ""tuning"": { ""chargeSeconds"": 3.0, ""decaySeconds"": 2.0,
                    ""joystickDeflectThreshold"": 0.6, ""crankDegreesThreshold"": 4.0,
                    ""crankActiveTimeout"": 0.4, ""heightEngageThreshold"": 0.7 } }");
            LaunchTuning t = config.TuningOrDefault();

            Assert.AreEqual(3.0f, t.chargeSeconds, 1e-4);
            Assert.AreEqual(2.0f, t.decaySeconds, 1e-4);
            Assert.AreEqual(0.6f, t.joystickDeflectThreshold, 1e-4);
            Assert.AreEqual(4.0f, t.crankDegreesThreshold, 1e-4);
            Assert.AreEqual(0.4f, t.crankActiveTimeout, 1e-4);
            Assert.AreEqual(0.7f, t.heightEngageThreshold, 1e-4);
        }

        [Test]
        public void Normalized_ReplacesNonPositiveWithDefaults()
        {
            var t = new LaunchTuning
            {
                chargeSeconds = 0f,
                decaySeconds = -1f,
                joystickDeflectThreshold = 0f,
                crankDegreesThreshold = 0f,
                crankActiveTimeout = 0f,
                heightEngageThreshold = 0f,
            }.Normalized();

            Assert.AreEqual(LaunchTuning.DefaultChargeSeconds, t.chargeSeconds, 1e-4);
            Assert.AreEqual(LaunchTuning.DefaultDecaySeconds, t.decaySeconds, 1e-4);
            Assert.AreEqual(LaunchTuning.DefaultJoystickDeflectThreshold, t.joystickDeflectThreshold, 1e-4);
            Assert.AreEqual(LaunchTuning.DefaultCrankDegreesThreshold, t.crankDegreesThreshold, 1e-4);
            Assert.AreEqual(LaunchTuning.DefaultCrankActiveTimeout, t.crankActiveTimeout, 1e-4);
            Assert.AreEqual(LaunchTuning.DefaultHeightEngageThreshold, t.heightEngageThreshold, 1e-4);
        }

        [Test]
        public void Normalized_KeepsPositiveOverrides_ButDefaultsMissingOnes()
        {
            // Simulates a partial JSON block (JsonUtility leaves unset numbers at 0).
            var t = new LaunchTuning
            {
                chargeSeconds = 8.0f,
                decaySeconds = 0f, // "missing" in JSON
            }.Normalized();

            Assert.AreEqual(8.0f, t.chargeSeconds, 1e-4, "explicit override kept");
            Assert.AreEqual(1.0f, t.decaySeconds, 1e-4, "missing decay falls back to default");
        }
    }
}
