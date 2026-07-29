using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// Guards the reload contract behind the gate-2 bug «после победы lady_bug запускается Сизиф».
    ///
    /// lady_bug's and Sisyphus's entry scenes are (currently) BOTH named "Main", and in the hub build
    /// they coexist in Build Settings — so <c>SceneManager.LoadScene(GetActiveScene().name)</c> is
    /// ambiguous: Unity resolves the FIRST enabled scene with that name (Sisyphus's), booting the wrong
    /// game. The fix in lady_bug reloads by <c>GetActiveScene().buildIndex</c>.
    ///
    /// Pinned here, in two parts:
    ///  1. WHEN the two entry scenes share a name (today's reality), their enabled build indices must be
    ///     distinct — i.e. a buildIndex reload is unambiguous where a name reload is not. If either game
    ///     legitimately renames its entry scene later, the collision is gone and this test passes — it
    ///     does NOT demand the names stay "Main" forever.
    ///  2. The lady_bug package source contains NO by-name scene loads (no string-literal LoadScene, no
    ///     LoadScene(GetActiveScene().name)) outside comments — the actual behavioral fix, guarded at
    ///     the source level so a regression re-introducing a name reload fails here even if scene names
    ///     drift apart in the meantime.
    /// </summary>
    public class SceneCollisionContractTests
    {
        private struct EntryScene
        {
            public string Path;
            public string Name;
            public int BuildIndex;
        }

        // The two known entry scenes by their package-path marker in Build Settings.
        private static EntryScene? FindEnabledEntry(string pathMarker)
        {
            int buildIndex = 0;
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (!s.enabled) continue;
                if (s.path.Contains(pathMarker))
                    return new EntryScene
                    {
                        Path = s.path,
                        Name = System.IO.Path.GetFileNameWithoutExtension(s.path),
                        BuildIndex = buildIndex,
                    };
                buildIndex++;
            }
            return null;
        }

        [Test]
        public void WhenEntrySceneNamesCollide_BuildIndicesAreDistinct()
        {
            EntryScene? ladyBug = FindEnabledEntry("com.aigamestudio.game-lady-bug/");
            EntryScene? sisyphus = FindEnabledEntry("com.aigamestudio.game-endless-sisyphus/");
            Assert.IsTrue(ladyBug.HasValue, "lady_bug's entry scene must be enabled in Build Settings.");
            Assert.IsTrue(sisyphus.HasValue, "Sisyphus's entry scene must be enabled in Build Settings.");

            if (ladyBug.Value.Name != sisyphus.Value.Name)
            {
                // Names no longer collide (a legitimate rename happened) — the by-name ambiguity is
                // gone; nothing further to pin here. The source-level guard below still holds.
                Assert.Pass("Entry scene names differ ('" + ladyBug.Value.Name + "' vs '"
                            + sisyphus.Value.Name + "') — no collision to guard.");
            }

            // Same name (today: both "Main") — the exact ambiguity that broke by-name reloads. The
            // buildIndex reload each game uses must be able to tell them apart.
            Assert.AreNotEqual(ladyBug.Value.BuildIndex, sisyphus.Value.BuildIndex,
                "Colliding scene names must at least sit at distinct build indices, or even a "
                + "buildIndex reload could not disambiguate them.");
        }

        [Test]
        public void LadyBugPackage_HasNoByNameSceneLoads()
        {
            // Resolve the file:-referenced package to its real folder and scan its runtime sources.
            string pkgRoot = Path.GetFullPath("Packages/com.aigamestudio.game-lady-bug");
            Assert.IsTrue(Directory.Exists(pkgRoot),
                "lady_bug package folder must resolve (file: dependency): " + pkgRoot);

            // By-name loads: LoadScene("...") / LoadSceneAsync("...") string literals, or the
            // GetActiveScene().name self-reload pattern. buildIndex overloads do not match.
            var byNameCall = new Regex(
                @"LoadScene(Async)?\s*\(\s*(""|SceneManager\s*\.\s*GetActiveScene\s*\(\s*\)\s*\.\s*name)");

            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(pkgRoot, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    // Strip line comments; the patterns of interest never span lines, and the
                    // package carries no block-commented load calls worth a full parser.
                    int slash = line.IndexOf("//", System.StringComparison.Ordinal);
                    string code = slash >= 0 ? line.Substring(0, slash) : line;
                    if (byNameCall.IsMatch(code))
                        offenders.Add(file.Substring(pkgRoot.Length + 1) + ":" + (i + 1) + "  " + code.Trim());
                }
            }

            Assert.IsEmpty(offenders,
                "lady_bug must reload scenes by buildIndex, never by name (gate-2 «после победы "
                + "запускается Сизиф»). By-name loads found:\n" + string.Join("\n", offenders));
        }
    }
}
