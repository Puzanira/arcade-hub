using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// Done-contract #2 at the LOADER and MENU level (the FromJson throw is covered separately):
    /// a broken config file on disk logs one clear console error, degrades to an empty config,
    /// and the menu controller still comes up with zero rows — never a crash.
    /// </summary>
    public class LauncherConfigLoaderTests
    {
        private static readonly Regex LoadErrorPattern =
            new Regex(@"Failed to load launcher config .* empty slot list");

        private string _tempPath;

        [TearDown]
        public void TearDown()
        {
            if (_tempPath != null && File.Exists(_tempPath)) File.Delete(_tempPath);
            _tempPath = null;
        }

        private string WriteTempConfig(string content)
        {
            _tempPath = Path.Combine(Path.GetTempPath(), "hub-loader-test-" + Path.GetRandomFileName() + ".json");
            File.WriteAllText(_tempPath, content);
            return _tempPath;
        }

        [Test]
        public void LoadFrom_MalformedFile_LogsOneClearError_AndReturnsEmptyConfig()
        {
            string path = WriteTempConfig("{ this is not json ");

            LogAssert.Expect(LogType.Error, LoadErrorPattern);
            LauncherConfig config = LauncherConfigLoader.LoadFrom(path);
            LogAssert.NoUnexpectedReceived(); // exactly the one expected error, nothing else

            Assert.IsNotNull(config, "Loader must return a config object, never null.");
            Assert.IsNotNull(config.slots);
            Assert.AreEqual(0, config.slots.Count, "Broken file must degrade to an empty slot list.");
        }

        [Test]
        public void LoadFrom_MissingFile_LogsOneClearError_AndReturnsEmptyConfig()
        {
            string path = Path.Combine(Path.GetTempPath(), "hub-loader-test-does-not-exist.json");
            Assert.IsFalse(File.Exists(path));

            LogAssert.Expect(LogType.Error, LoadErrorPattern);
            LauncherConfig config = LauncherConfigLoader.LoadFrom(path);
            LogAssert.NoUnexpectedReceived();

            Assert.IsNotNull(config);
            Assert.AreEqual(0, config.slots.Count, "Missing file must degrade to an empty slot list.");
        }

        [Test]
        public void ShippedConfig_MatchesFoundersLayout_SixInstalled_OneSoon_NoTestGame()
        {
            // all-games-wiring (founder's layout 2026-07-26, Медитация landed 2026-08-01): SIX installed
            // games, ONE "soon" placeholder, and NO built-in Test Game slot (its scene/rig stay in the
            // project but leave the launcher menu).
            LauncherConfig config = LauncherConfigLoader.LoadFromStreamingAssets();

            int installed = 0, soon = 0;
            foreach (LauncherSlot s in config.slots)
            {
                if (s.IsInstalled) installed++;
                else soon++;
            }
            Assert.AreEqual(7, config.slots.Count, "Seven cabinet controls -> seven slots.");
            Assert.AreEqual(6, installed,
                "Sisyphus, Factory, Life Choices, Lady Bug, Home Alone, Медитация are installed.");
            Assert.AreEqual(1, soon, "Only Arcade Prototype is still 'soon'.");

            Assert.IsNull(config.slots.Find(s => s.displayName == "Test Game"),
                "Test Game must NOT appear in the shipped launcher menu.");

            // Each installed game sits on its founder-assigned control with a real entry scene.
            AssertInstalledOn(config, "Crank",       "Бесконечный Сизиф");
            AssertInstalledOn(config, "RedButton",   "Завод");
            AssertInstalledOn(config, "GreenButton", "Спасибо, не надо");
            AssertInstalledOn(config, "HeightA",     "Lady Bug Hit The Road");
            AssertInstalledOn(config, "HeightB",     "Кошачьи будни");
            AssertInstalledOn(config, "Joystick",    "Медитация в спешке");

            // The one "soon" slot is a placeholder — a planned control binding, no entry scene.
            LauncherSlot bang = config.slots.Find(s => s.controlName == "BangButton");
            Assert.AreEqual("Таблетка в космосе", bang.displayName);
            Assert.IsFalse(bang.IsInstalled, "Arcade Prototype is a 'soon' placeholder.");

            // Native-input games (legacy/raw input) carry the pump flag so the return watchdog stays live.
            Assert.IsTrue(config.slots.Find(s => s.displayName == "Lady Bug Hit The Road").nativeInput, "Lady Bug is native-input.");
            Assert.IsTrue(config.slots.Find(s => s.displayName == "Завод").nativeInput, "Factory is native-input.");
            // …and the ArcadeInput-native ones must NOT, or they get pumped twice: Медитация's entry scene
            // carries its own ArcadeInputRunner, and a doubled pump would count every crank degree twice —
            // which on this game is the whole collection mechanic.
            Assert.IsFalse(config.slots.Find(s => s.displayName == "Медитация в спешке").nativeInput,
                "Медитация pumps ArcadeInput itself.");

            // Sisyphus needs the flag DESPITE reading through ArcadeInput, and this line used to say the
            // opposite (it was unreachable behind the installed-count assertion above, so nothing caught
            // it). Its ArcadeControlsAdapter takes ownership only when ArcadeInput.Backend is null; in the
            // hub the backend is NOT null — the menu scene's runner left it behind — so Sisyphus refuses
            // ownership and never pumps, and the watchdog's own runner has to. The shipped config already
            // sets it; what was stale was the expectation. (Медитация avoids the trap by keying its guard
            // on a runner IN THE SCENE rather than on the static backend.)
            Assert.IsTrue(config.slots.Find(s => s.displayName == "Бесконечный Сизиф").nativeInput,
                "Sisyphus yields backend ownership in the hub, so the launcher must pump for it.");
        }

        [Test]
        public void ShippedConfig_EverySlotCarriesALoadableRainSpriteSet()
        {
            // Founder's gate-2 wave: "по прямоугольникам не понятно, что запускается" — every slot rains
            // themed sprites. Each declared Resources path must actually LOAD (a typo'd path would silently
            // fall back to the solid tile; the runtime fallback stays, but the shipped config must be clean).
            LauncherConfig config = LauncherConfigLoader.LoadFromStreamingAssets();
            Assert.AreEqual(7, config.slots.Count);

            foreach (LauncherSlot slot in config.slots)
            {
                bool hasSet = slot.rainSprites != null && slot.rainSprites.Length > 0;
                bool hasSingle = !string.IsNullOrEmpty(slot.rainSprite);
                Assert.IsTrue(hasSet || hasSingle,
                    $"Slot '{slot.displayName}' must declare rainSprites or rainSprite.");

                if (hasSet)
                    foreach (string path in slot.rainSprites)
                        Assert.IsNotNull(Resources.Load<Sprite>(path),
                            $"Slot '{slot.displayName}': rainSprites path '{path}' must load from Resources.");
                else
                    Assert.IsNotNull(Resources.Load<Sprite>(slot.rainSprite),
                        $"Slot '{slot.displayName}': rainSprite path '{slot.rainSprite}' must load from Resources.");
            }
        }

        private static void AssertInstalledOn(LauncherConfig config, string control, string displayName)
        {
            LauncherSlot slot = config.slots.Find(s => s.controlName == control);
            Assert.IsNotNull(slot, $"Slot bound to {control} present.");
            Assert.AreEqual(displayName, slot.displayName, $"{control} launches {displayName}.");
            Assert.IsTrue(slot.IsInstalled, $"{displayName} is installed with a real entry scene.");
        }

        [Test]
        public void BrokenFile_ToLoader_ToHoldToLaunch_ComesUpEmpty_NoCrash()
        {
            // The degradation chain on the attract screen (the game list is gone — hold-to-launch is the
            // only config consumer on screen): broken JSON on disk -> loader degrades with one console
            // error -> the hold-to-launch stack builds with ZERO slots instead of crashing.
            string path = WriteTempConfig(@"{ ""slots"": [ { ""displayName"": ");

            LogAssert.Expect(LogType.Error, LoadErrorPattern);
            LauncherConfig config = LauncherConfigLoader.LoadFrom(path);

            var go = new GameObject("HoldToLaunchUnderTest");
            try
            {
                var htl = go.AddComponent<HoldToLaunchController>();
                htl.InitializeWith(config); // must not throw

                Assert.AreEqual(0f, htl.Charge, 1e-6f, "Empty config: nothing can charge.");
                Assert.AreEqual(-1, htl.ActiveSlot, "Empty config: no active slot.");
                Assert.AreEqual(0, htl.FlowerCount, "Empty config: nothing piled.");
                htl.Tick(0.1f); // a frame with zero slots must not throw either
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
