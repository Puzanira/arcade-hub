using System.Collections.Generic;

namespace AiGameStudio.ArcadeHub
{
    /// <summary>
    /// Pure navigation model for the launcher menu: a selection cursor over the ordered slots with
    /// wrap-around. No Unity types, no input, no scene loading — fully EditMode-testable.
    /// </summary>
    public sealed class HubMenuModel
    {
        private readonly IReadOnlyList<LauncherSlot> _slots;

        public HubMenuModel(IReadOnlyList<LauncherSlot> slots)
        {
            _slots = slots ?? new List<LauncherSlot>();
            SelectedIndex = 0;
        }

        /// <summary>Number of slots in the menu.</summary>
        public int Count => _slots.Count;

        /// <summary>Index of the highlighted slot (0 when empty).</summary>
        public int SelectedIndex { get; private set; }

        /// <summary>The highlighted slot, or null when the menu is empty.</summary>
        public LauncherSlot Selected => Count == 0 ? null : _slots[SelectedIndex];

        /// <summary>Slot at <paramref name="index"/> (for the view to render every row).</summary>
        public LauncherSlot SlotAt(int index) => _slots[index];

        /// <summary>Move the cursor up one row, wrapping from the top to the bottom.</summary>
        public void MoveUp()
        {
            if (Count == 0) return;
            SelectedIndex = (SelectedIndex - 1 + Count) % Count;
        }

        /// <summary>Move the cursor down one row, wrapping from the bottom to the top.</summary>
        public void MoveDown()
        {
            if (Count == 0) return;
            SelectedIndex = (SelectedIndex + 1) % Count;
        }
    }
}
