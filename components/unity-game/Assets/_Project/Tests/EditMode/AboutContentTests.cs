using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    /// <summary>
    /// The about document as DATA: the founder's story is a JSON file of ordered blocks in
    /// StreamingAssets, replaceable without touching code — and an unreadable one degrades to a
    /// placeholder heading with ONE console error rather than taking the cabinet down (the same
    /// done-contract <see cref="LauncherConfigLoaderTests"/> pins for the launcher config).
    /// </summary>
    public class AboutContentTests
    {
        private static readonly Regex LoadErrorPattern =
            new Regex(@"Failed to load about content .* placeholder heading only");

        private string _tempPath;

        [TearDown]
        public void TearDown()
        {
            if (_tempPath != null && File.Exists(_tempPath)) File.Delete(_tempPath);
            _tempPath = null;
        }

        private string WriteTemp(string content)
        {
            _tempPath = Path.Combine(Path.GetTempPath(), "hub-about-test-" + Path.GetRandomFileName() + ".json");
            File.WriteAllText(_tempPath, content);
            return _tempPath;
        }

        // ---------------- parsing ----------------

        [Test]
        public void FromJson_ParsesEveryBlockType_InDocumentOrder()
        {
            const string json = @"{ ""blocks"": [
                { ""type"": ""title"", ""text"": ""6 режимов суеты"" },
                { ""type"": ""heading"", ""text"": ""Идея"" },
                { ""type"": ""paragraph"", ""text"": ""Текст будет здесь."" },
                { ""type"": ""photo"", ""file"": ""process-01.png"" },
                { ""type"": ""caption"", ""text"": ""Подпись"" }
            ] }";

            AboutContent content = AboutContent.FromJson(json);

            Assert.AreEqual(5, content.Blocks.Count, "Every block in the file is kept, in file order.");
            Assert.AreEqual(AboutBlockType.Title, content.Blocks[0].Kind);
            Assert.AreEqual("6 режимов суеты", content.Blocks[0].text);
            Assert.AreEqual(AboutBlockType.Heading, content.Blocks[1].Kind);
            Assert.AreEqual(AboutBlockType.Paragraph, content.Blocks[2].Kind);
            Assert.AreEqual(AboutBlockType.Photo, content.Blocks[3].Kind);
            Assert.AreEqual("process-01.png", content.Blocks[3].file, "A photo block carries its file name.");
            Assert.AreEqual(AboutBlockType.Caption, content.Blocks[4].Kind);
            Assert.IsFalse(content.IsFallback, "A file that parsed is not the placeholder.");
        }

        [Test]
        public void FromJson_UnknownType_IsUnknown_NotAThrow()
        {
            AboutContent content = AboutContent.FromJson(
                @"{ ""blocks"": [ { ""type"": ""marquee"", ""text"": ""?"" } ] }");

            Assert.AreEqual(1, content.Blocks.Count);
            Assert.AreEqual(AboutBlockType.Unknown, content.Blocks[0].Kind,
                "A type the launcher does not know is skipped by the view, never a parse failure.");
        }

        [Test]
        public void FromJson_EmptyOrMalformed_Throws_SoTheLoaderCanDegrade()
        {
            Assert.Throws<FormatException>(() => AboutContent.FromJson(""));
            Assert.Throws<FormatException>(() => AboutContent.FromJson("   "));
            Assert.Throws<FormatException>(() => AboutContent.FromJson("{ this is not json "));
        }

        [Test]
        public void FromJson_ValidButEmpty_IsAUsableEmptyDocument()
        {
            AboutContent content = AboutContent.FromJson(@"{ ""blocks"": [] }");
            Assert.IsNotNull(content.Blocks);
            Assert.AreEqual(0, content.Blocks.Count);

            AboutContent missingArray = AboutContent.FromJson("{}");
            Assert.IsNotNull(missingArray.Blocks, "A missing block array is an empty document, not a null one.");
            Assert.AreEqual(0, missingArray.Blocks.Count);
        }

        // ---------------- loader degradation (done-contract) ----------------

        [Test]
        public void LoadFrom_MalformedFile_LogsOneClearError_AndReturnsThePlaceholder()
        {
            string path = WriteTemp("{ blocks: oops ");

            LogAssert.Expect(LogType.Error, LoadErrorPattern);
            AboutContent content = AboutContentLoader.LoadFrom(path);
            LogAssert.NoUnexpectedReceived(); // exactly one error, nothing else

            Assert.IsNotNull(content, "The loader returns a document, never null.");
            Assert.IsTrue(content.IsFallback, "A broken file degrades to the placeholder document.");
            Assert.AreEqual(1, content.Blocks.Count, "The placeholder is the heading and nothing else.");
            Assert.AreEqual(AboutBlockType.Title, content.Blocks[0].Kind);
            Assert.AreEqual(AboutContent.FallbackTitle, content.Blocks[0].text);
        }

        [Test]
        public void LoadFrom_MissingFile_LogsOneClearError_AndReturnsThePlaceholder()
        {
            string path = Path.Combine(Path.GetTempPath(), "hub-about-test-does-not-exist.json");
            Assert.IsFalse(File.Exists(path));

            LogAssert.Expect(LogType.Error, LoadErrorPattern);
            AboutContent content = AboutContentLoader.LoadFrom(path);
            LogAssert.NoUnexpectedReceived();

            Assert.IsTrue(content.IsFallback);
            Assert.AreEqual(1, content.Blocks.Count);
        }

        [Test]
        public void LoadFrom_GoodFile_LogsNothing()
        {
            string path = WriteTemp(@"{ ""blocks"": [ { ""type"": ""title"", ""text"": ""Ок"" } ] }");

            AboutContent content = AboutContentLoader.LoadFrom(path);
            LogAssert.NoUnexpectedReceived();

            Assert.IsFalse(content.IsFallback);
            Assert.AreEqual("Ок", content.Blocks[0].text);
        }

        // ---------------- photo paths ----------------

        [Test]
        public void PhotoPathFor_ResolvesAgainstTheDocumentsOwnFolder()
        {
            string contentPath = Path.Combine("root", "About", "content.json");
            string photo = AboutContentLoader.PhotoPathFor(contentPath, "process-01.png");

            Assert.AreEqual(Path.Combine("root", "About", "process-01.png"), photo,
                "Photos are addressed by bare file name — the whole document moves as one folder.");
            Assert.IsNull(AboutContentLoader.PhotoPathFor(contentPath, null));
            Assert.IsNull(AboutContentLoader.PhotoPathFor(contentPath, "  "));
        }

        // ---------------- the SHIPPED placeholder document ----------------

        [Test]
        public void ShippedContent_Parses_AndCarriesPlaceholdersOfEveryKind()
        {
            string path = AboutContentLoader.ContentPath;
            Assert.IsTrue(File.Exists(path), $"The about document must ship in StreamingAssets ({path}).");

            AboutContent content = AboutContentLoader.LoadFrom(path);
            LogAssert.NoUnexpectedReceived();
            Assert.IsFalse(content.IsFallback, "The shipped document must parse.");

            int titles = 0, headings = 0, paragraphs = 0, photos = 0, captions = 0;
            foreach (AboutBlock block in content.Blocks)
            {
                switch (block.Kind)
                {
                    case AboutBlockType.Title: titles++; break;
                    case AboutBlockType.Heading: headings++; break;
                    case AboutBlockType.Paragraph: paragraphs++; break;
                    case AboutBlockType.Photo: photos++; break;
                    case AboutBlockType.Caption: captions++; break;
                }
            }

            Assert.AreEqual(1, titles, "One title — the cabinet's name.");
            Assert.GreaterOrEqual(headings, 1);
            Assert.GreaterOrEqual(paragraphs, 3, "At least the promised placeholder paragraphs.");
            Assert.AreEqual(2, photos, "Two placeholder process photos.");
            Assert.GreaterOrEqual(captions, 2);

            foreach (AboutBlock block in content.Blocks)
            {
                if (block.Kind != AboutBlockType.Photo) continue;
                string photoPath = AboutContentLoader.PhotoPathFor(path, block.file);
                Assert.IsTrue(File.Exists(photoPath),
                    $"Every photo the document names must ship beside it: '{photoPath}'.");
            }
        }
    }
}
