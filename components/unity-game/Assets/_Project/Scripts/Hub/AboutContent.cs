using System;
using System.Collections.Generic;
using UnityEngine;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// The kinds of block the "История проекта" document is built out of. The founder writes the
    /// document as DATA (StreamingAssets/About/content.json) — this is the whole vocabulary she has,
    /// and it is deliberately tiny: a document, not a page-builder.
    /// </summary>
    public enum AboutBlockType
    {
        /// <summary>A type string the launcher does not know — kept in the model, skipped by the view.</summary>
        Unknown,

        /// <summary>The document's one big opening line (Russo One, largest).</summary>
        Title,

        /// <summary>A section head (Russo One, mid).</summary>
        Heading,

        /// <summary>Body copy (Arimo Regular).</summary>
        Paragraph,

        /// <summary>An image file sitting next to content.json, drawn at the column width, aspect kept.</summary>
        Photo,

        /// <summary>A small grey line under a photo (Arimo Regular).</summary>
        Caption,
    }

    /// <summary>
    /// One block of the about document, exactly as it appears in content.json. Flat on purpose:
    /// <see cref="JsonUtility"/> has no polymorphism, so the block carries every field any type could
    /// use and the type string says which ones matter.
    /// </summary>
    [Serializable]
    public class AboutBlock
    {
        /// <summary>"title" | "heading" | "paragraph" | "photo" | "caption".</summary>
        public string type;

        /// <summary>The words — for everything except <see cref="AboutBlockType.Photo"/>.</summary>
        public string text;

        /// <summary>Image file name, resolved against the folder content.json itself lives in.</summary>
        public string file;

        /// <summary>The parsed <see cref="type"/>; unknown/missing strings become <see cref="AboutBlockType.Unknown"/>.</summary>
        public AboutBlockType Kind => ParseType(type);

        /// <summary>Map a content.json type string onto a block type. Unknown = never a crash, just skipped.</summary>
        public static AboutBlockType ParseType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return AboutBlockType.Unknown;
            switch (type.Trim().ToLowerInvariant())
            {
                case "title": return AboutBlockType.Title;
                case "heading": return AboutBlockType.Heading;
                case "paragraph": case "text": return AboutBlockType.Paragraph;
                case "photo": case "image": return AboutBlockType.Photo;
                case "caption": return AboutBlockType.Caption;
                default: return AboutBlockType.Unknown;
            }
        }
    }

    /// <summary>
    /// The parsed about document: an ordered list of blocks. Parsing is pure and EditMode-testable and
    /// mirrors <see cref="LauncherConfig"/> exactly — malformed JSON throws a clear
    /// <see cref="FormatException"/> and the LOADER decides how to degrade, so the screen can never crash
    /// the cabinet over a typo in the founder's text file.
    /// </summary>
    [Serializable]
    public class AboutContent
    {
        /// <summary>The heading shown when content.json cannot be read at all (done-contract: never blank-crash).</summary>
        public const string FallbackTitle = "История проекта";

        public List<AboutBlock> blocks = new List<AboutBlock>();

        /// <summary>The blocks, in document order.</summary>
        public IReadOnlyList<AboutBlock> Blocks => blocks;

        /// <summary>
        /// True when this content is the placeholder the loader produces from an unreadable file, rather
        /// than something the founder actually wrote. Never serialized — it is a fact about the LOAD.
        /// </summary>
        public bool IsFallback { get; private set; }

        /// <summary>
        /// What an unreadable content.json degrades to: the document's placeholder heading and nothing
        /// else, so the screen still opens, still closes, and still says what it is.
        /// </summary>
        public static AboutContent Fallback()
        {
            var content = new AboutContent { IsFallback = true };
            content.blocks.Add(new AboutBlock { type = "title", text = FallbackTitle });
            return content;
        }

        /// <summary>
        /// Parse a document from a JSON string. Throws <see cref="FormatException"/> on empty or malformed
        /// input. A valid document with an empty (or missing) block array yields an empty, usable document.
        /// </summary>
        public static AboutContent FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("About content JSON is empty.");

            AboutContent content;
            try
            {
                content = JsonUtility.FromJson<AboutContent>(json);
            }
            catch (Exception e)
            {
                throw new FormatException("About content JSON is not valid: " + e.Message, e);
            }

            if (content == null)
                throw new FormatException("About content JSON did not deserialize to an object.");
            if (content.blocks == null)
                content.blocks = new List<AboutBlock>();

            return content;
        }
    }
}
