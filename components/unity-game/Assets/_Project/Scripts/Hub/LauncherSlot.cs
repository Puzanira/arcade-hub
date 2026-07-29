using System;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// One "control -> game" slot in the launcher menu. Pure data, parsed from
    /// <c>launcher-config.json</c> in StreamingAssets (editable on disk, no rebuild — the museum case).
    /// </summary>
    [Serializable]
    public class LauncherSlot
    {
        /// <summary>Logical launch control bound to this slot (e.g. "Crank", "Joystick"). Metadata for v0.</summary>
        public string controlName;

        /// <summary>Human-readable game name shown in the menu row.</summary>
        public string displayName;

        /// <summary>Scene loaded when an installed slot is chosen. Empty for planned slots.</summary>
        public string entryScene;

        /// <summary>"installed" or anything else (e.g. "soon"/"planned"). Anything not "installed" is treated as planned.</summary>
        public string status;

        /// <summary>
        /// True for games that DON'T read input through <see cref="AiGameStudio.ArcadeControls.ArcadeInput"/>
        /// (native/legacy input — e.g. Lady Bug on the old Input Manager, Factory on the raw new Input System).
        /// Such a game never pumps ArcadeInput, so the launcher's return watchdog would see a frozen MenuButton.
        /// When set, the launcher attaches its own input pump alongside the return watchdog so the universal
        /// MenuButton exit gesture still works. ArcadeInput-native games (Sisyphus, Home Alone, Life Choices)
        /// leave this false — they pump ArcadeInput themselves and must not be double-pumped.
        /// </summary>
        public bool nativeInput;

        /// <summary>True only when the slot is installed AND has a non-empty entry scene to load.</summary>
        public bool IsInstalled =>
            string.Equals(status, "installed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(entryScene);
    }
}
