using System;
using NUnit.Framework;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    public class LauncherConfigTests
    {
        private const string ValidJson = @"{
            ""slots"": [
                { ""controlName"": ""Crank"",     ""displayName"": ""Test Game"",  ""entryScene"": ""TestGame"", ""status"": ""installed"" },
                { ""controlName"": ""RedButton"", ""displayName"": ""Home Alone"", ""entryScene"": """",         ""status"": ""planned"" }
            ]
        }";

        [Test]
        public void FromJson_ValidConfig_ParsesSlotsInOrder()
        {
            LauncherConfig config = LauncherConfig.FromJson(ValidJson);

            Assert.AreEqual(2, config.slots.Count);
            Assert.AreEqual("Crank", config.slots[0].controlName);
            Assert.AreEqual("Test Game", config.slots[0].displayName);
            Assert.AreEqual("TestGame", config.slots[0].entryScene);
            Assert.AreEqual("Home Alone", config.slots[1].displayName);
        }

        [Test]
        public void FromJson_InstalledSlotWithScene_IsInstalled()
        {
            LauncherConfig config = LauncherConfig.FromJson(ValidJson);

            Assert.IsTrue(config.slots[0].IsInstalled, "installed slot with an entry scene should be installed");
            Assert.IsFalse(config.slots[1].IsInstalled, "planned slot should not be installed");
        }

        [Test]
        public void FromJson_InstalledStatusButEmptyScene_IsNotInstalled()
        {
            // Guards against a config that claims installed but has nothing to load.
            var config = LauncherConfig.FromJson(
                @"{ ""slots"": [ { ""displayName"": ""Broken"", ""entryScene"": """", ""status"": ""installed"" } ] }");

            Assert.IsFalse(config.slots[0].IsInstalled);
        }

        [Test]
        public void FromJson_EmptySlotArray_YieldsUsableEmptyConfig()
        {
            LauncherConfig config = LauncherConfig.FromJson(@"{ ""slots"": [] }");

            Assert.IsNotNull(config.slots);
            Assert.AreEqual(0, config.slots.Count);
        }

        [Test]
        public void FromJson_MissingSlotArray_YieldsEmptySlots()
        {
            LauncherConfig config = LauncherConfig.FromJson(@"{ }");

            Assert.IsNotNull(config.slots);
            Assert.AreEqual(0, config.slots.Count);
        }

        [Test]
        public void FromJson_MalformedJson_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => LauncherConfig.FromJson("{ this is not json "));
        }

        [Test]
        public void FromJson_EmptyString_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => LauncherConfig.FromJson(""));
            Assert.Throws<FormatException>(() => LauncherConfig.FromJson("   "));
        }
    }
}
