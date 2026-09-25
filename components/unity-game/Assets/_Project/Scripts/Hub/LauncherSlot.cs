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
        /// Optional per-slot sprite for the shared launch animation (the falling-and-piling grid in
        /// <see cref="HoldToLaunchController"/>): a <c>Resources.Load</c> path to a Sprite, e.g.
        /// "RainSprites/Flower". Empty falls back to a solid slot-coloured tile. Named for the
        /// animation's "rain of sprites" visual. Prefer <see cref="rainSprites"/> for a set; this single
        /// field stays for config backward-compatibility and is used only when the array is empty.
        /// </summary>
        public string rainSprite;

        /// <summary>
        /// Optional per-slot sprite SET for the shared launch animation — each falling object picks one
        /// at random, exactly like the Lady Bug intro this animation was adapted from mixed its ten
        /// flower sprites. Takes precedence over <see cref="rainSprite"/> when non-empty. The Lady Bug
        /// slot ships that game's own ten flowers; themed sets for the other slots arrive later from
        /// their manifests.
        /// </summary>
        public string[] rainSprites;

        /// <summary>
        /// True for games that DON'T read input through <see cref="AiGameStudio.ArcadeControls.ArcadeInput"/>
        /// (native/legacy input — e.g. Lady Bug on the old Input Manager, Factory on the raw new Input System).
        ///
        /// It no longer changes the input topology, and that is the point: the pump used to be per-launch
        /// (the return watchdog carried its own <c>ArcadeInputRunner</c> for these slots, and an
        /// ArcadeInput-native game pumped for itself), which is exactly what made the boards be re-scanned
        /// on every transition. Now the cabinet has ONE process-wide grabber
        /// (<c>ArcadeInputRunner.Ensure</c>) that owns the boards and pumps every frame in every scene, so
        /// the watchdog's MenuButton works for every slot regardless of this flag.
        ///
        /// It stays because it is a true, useful FACT about the game — whether it reads the cabinet through
        /// the shared facade at all — and the loader's tests pin it per shipped slot.
        /// </summary>
        public bool nativeInput;

        /// <summary>True only when the slot is installed AND has a non-empty entry scene to load.</summary>
        public bool IsInstalled =>
            string.Equals(status, "installed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(entryScene);
    }
}
