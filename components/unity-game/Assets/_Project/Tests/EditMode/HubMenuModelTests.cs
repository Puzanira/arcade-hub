using System.Collections.Generic;
using NUnit.Framework;
using AiGameStudio.ArcadeHub;

namespace AiGameStudio.ArcadeHub.Tests
{
    public class HubMenuModelTests
    {
        private static List<LauncherSlot> SevenSlots()
        {
            var slots = new List<LauncherSlot>();
            slots.Add(new LauncherSlot { displayName = "Test Game", entryScene = "TestGame", status = "installed" });
            for (int i = 1; i < 7; i++)
                slots.Add(new LauncherSlot { displayName = "Planned " + i, entryScene = "", status = "planned" });
            return slots;
        }

        [Test]
        public void NewModel_StartsAtFirstSlot()
        {
            var model = new HubMenuModel(SevenSlots());

            Assert.AreEqual(7, model.Count);
            Assert.AreEqual(0, model.SelectedIndex);
            Assert.AreEqual("Test Game", model.Selected.displayName);
        }

        [Test]
        public void MoveDown_AdvancesSelection()
        {
            var model = new HubMenuModel(SevenSlots());

            model.MoveDown();
            model.MoveDown();

            Assert.AreEqual(2, model.SelectedIndex);
        }

        [Test]
        public void MoveDown_FromLast_WrapsToFirst()
        {
            var model = new HubMenuModel(SevenSlots());

            for (int i = 0; i < 6; i++) model.MoveDown(); // now at index 6 (last)
            Assert.AreEqual(6, model.SelectedIndex);

            model.MoveDown();
            Assert.AreEqual(0, model.SelectedIndex, "down from the last row should wrap to the first");
        }

        [Test]
        public void MoveUp_FromFirst_WrapsToLast()
        {
            var model = new HubMenuModel(SevenSlots());

            model.MoveUp();

            Assert.AreEqual(6, model.SelectedIndex, "up from the first row should wrap to the last");
        }

        [Test]
        public void Selected_DistinguishesInstalledFromPlanned()
        {
            var model = new HubMenuModel(SevenSlots());

            Assert.IsTrue(model.Selected.IsInstalled, "slot 0 is the installed test game");

            model.MoveDown();
            Assert.IsFalse(model.Selected.IsInstalled, "slot 1 is a planned game");
        }

        [Test]
        public void EmptyModel_DoesNotCrashOnNavigation()
        {
            var model = new HubMenuModel(new List<LauncherSlot>());

            Assert.AreEqual(0, model.Count);
            Assert.IsNull(model.Selected);

            model.MoveDown();
            model.MoveUp();

            Assert.AreEqual(0, model.SelectedIndex);
            Assert.IsNull(model.Selected);
        }
    }
}
