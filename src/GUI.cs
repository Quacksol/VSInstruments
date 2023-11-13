using System;                   // Action<>
using System.Collections.Generic; // Lists
using System.IO;                // Binary writer n that
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;  // Lang stuff
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;     // GUIHandbook

namespace instruments
{
    public class NoteGUI : HudElement
    {
        public override string ToggleKeyCombinationCode => null;

        public NoteGUI(ICoreClientAPI capi) : base(capi)
        {
            SetupDialog();
        }

        private void SetupDialog()
        {
            // Auto-sized dialog at the center of the screen
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterBottom);
            dialogBounds.fixedY -= 70;
            ElementBounds textBounds = ElementBounds.Fixed(0, 20, 30, 20);

            // Background boundaries. Again, just make it fit it's child elements, then add the text as a child element
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(textBounds);
            // Lastly, create the dialog
            SingleComposer = capi.Gui.CreateCompo("NoteDialog", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDynamicText("No note selected!", CairoFont.WhiteDetailText(), textBounds, "note")
                .Compose();
        }

        public void UpdateText(string newText)
        {
            // Called when the text needs to change. Update the SingleComposer's Dynamic text field.
            SingleComposer.GetDynamicText("note").SetNewText(newText);
        }
    }

    public class SongSelectGUI : GuiDialog
    {
        public override string ToggleKeyCombinationCode => null;
        event Action<string> bandNameChange;
        //Func<string, int> PlaySong;
        Vintagestory.API.Common.Func<string, int> PlaySong;

        int listHeight = 500;
        int listWidth = 700;

        private string filter = "";

        private List<IFlatListItem> allStackListItems = new List<IFlatListItem>();
        private List<IFlatListItem> shownStackListItems = new List<IFlatListItem>();

        public SongSelectGUI(ICoreClientAPI capi, Vintagestory.API.Common.Func<string, int> playSong, List<string> files, Action<string> bandChange = null, string bandName = "") : base(capi)
        {
            bandNameChange = bandChange;
            SetupDialog(files, bandName);
            PlaySong = playSong;

        }
        public override void OnGuiOpened()
        {
            FilterSongs();
            SingleComposer.GetTextInput("search").SetValue("");
            base.OnGuiOpened();
        }
        private void SetupDialog(List<string> files, string bandName)
        {
            // https://github.com/anegostudios/vsessentialsmod/blob/master/Gui/GuiDialogHandbook.cs

            ElementBounds searchFieldBounds = ElementBounds.Fixed(GuiStyle.ElementToDialogPadding - 2, 48, 300, 32);    // little bar at the top
            ElementBounds stackListBounds = ElementBounds.Fixed(0, 0, listWidth, listHeight).FixedUnder(searchFieldBounds, 32);
            ElementBounds insetBounds = stackListBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);                // Not sure lol
            ElementBounds clipBounds = stackListBounds.ForkBoundingParent();                                            // not sure
            ElementBounds scrollbarBounds = stackListBounds.CopyOffsetedSibling(3 + stackListBounds.fixedWidth + 7).WithFixedWidth(20);

            ElementBounds searchTextBounds = ElementBounds.Fixed(GuiStyle.ElementToDialogPadding - 2, 24, 256, 32); // No idea why the y has to be different here, too afraid to ask
            ElementBounds bandBoxBounds = ElementBounds.Fixed(GuiStyle.ElementToDialogPadding + 348, 68, 128, 32);
            ElementBounds bandStringNewBounds = ElementBounds.Fixed(GuiStyle.ElementToDialogPadding + 348, 45, 128, 32);
            ElementBounds bandStringBounds = ElementBounds.Fixed(GuiStyle.ElementToDialogPadding + 540, 68, 128, 128);

            // Background boundaries. Again, just make it fit it's child elements, then add the text as a child element
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(insetBounds, stackListBounds, scrollbarBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            foreach (string file in files)
            {
                GuiHandbookTextPage page = new GuiHandbookTextPage();
                page.Title = file;
                allStackListItems.Add(page);
            }

            // Lastly, create the dialog
            Action<string> ub = UpdateBand;
            SingleComposer = capi.Gui.CreateCompo("SongSelectDialog", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Song select", Close)
                .BeginChildElements(bgBounds)
                .AddTextInput(searchFieldBounds, (string newText) =>
                {
                    filter = newText;
                    FilterSongs();
                },
                CairoFont.WhiteSmallishText(), "search")
                .AddStaticText("Song Filter:", CairoFont.WhiteDetailText(), EnumTextOrientation.Left, searchTextBounds)
                .BeginClip(clipBounds)
                    .AddInset(insetBounds, 3)
                    .AddFlatList(stackListBounds, ButtonPressed, shownStackListItems, "stacklist")
                //.AddInteractiveElement(new GuiElementHandbookList(capi, stackListBounds, (int index) => {ButtonPressed(index);}, shownStackListItems), "stacklist")

                .EndClip()
                .AddVerticalScrollbar((float value) =>
                {
                    GuiElementFlatList stacklist = SingleComposer.GetFlatList("stacklist");
                    stacklist.insideBounds.fixedY = 3 - value;
                    stacklist.insideBounds.CalcWorldBounds();
                }, scrollbarBounds, "scrollbar")
                .EndChildElements()
            ;
            if (bandNameChange != null)
            {
                SingleComposer
                .AddDynamicText("", CairoFont.WhiteDetailText(), bandStringBounds, "Band name")
                .AddStaticText("Set Band Name:", CairoFont.WhiteDetailText(), EnumTextOrientation.Center, bandStringNewBounds)
                .AddTextInput(bandBoxBounds, ub)
                ;
            }
            SingleComposer.Compose();

            if (bandNameChange != null)
                UpdateBand(bandName);

        }

        private void OnNewScrollbarValue()
        {
            // Max val of value will depend on how many songs are in the folder
            GuiElementScrollbar scrollbar = SingleComposer.GetScrollbar("scrollbar");
            GuiElementFlatList stacklist = SingleComposer.GetFlatList("stacklist");
            scrollbar.SetHeights(
                (float)listHeight,
                (float)stacklist.insideBounds.fixedHeight
                );
        }

        private void ButtonPressed(int index)
        {
            GuiHandbookTextPage page = (GuiHandbookTextPage)shownStackListItems[index];
            PlaySong(page.Title);
            TryClose();
        }
        private void FilterSongs()
        {
            shownStackListItems.Clear();
            foreach (GuiHandbookTextPage song in allStackListItems)
            {
                if (filter != "")
                {
                    string lowerCase = song.Title.ToLower();
                    if (!lowerCase.Contains(filter.ToLower()))
                        continue;
                }
                shownStackListItems.Add(song);
            }
            GuiElementFlatList stacklist = SingleComposer.GetFlatList("stacklist");
            stacklist.CalcTotalHeight();
            OnNewScrollbarValue();
        }
        public void UpdateBand(string bandName)
        {
            // Called when the text needs to change. Update the SingleComposer's Dynamic text field.
            string newText;
            if (bandName != "")
                newText = "Band Name: \n\"" + bandName + "\"";
            else
                newText = "No Band";
            SingleComposer.GetDynamicText("Band name").SetNewText(newText);
            bandNameChange(bandName);
        }
        private void Close()
        {
            TryClose();
        }
    }
    public class MusicBlockGUI : GuiDialogBlockEntity
    {

        public MusicBlockGUI(string title, InventoryBase inventory, BlockPos bePos, ICoreClientAPI capi, string blockName, string bandName, string songName) : base(title, inventory, bePos, capi)
        {
            if (IsDuplicate)
                return;
            capi.World.Player.InventoryManager.OpenInventory(Inventory);
            SetupDialog(blockName, bandName, songName);
        }
        private void SetupDialog(string name, string bandName, string songName)
        {
            ItemSlot hoveredSlot = capi.World.Player.InventoryManager.CurrentHoveredSlot;
            if (hoveredSlot != null && hoveredSlot.Inventory == Inventory)
            {
                capi.Input.TriggerOnMouseLeaveSlot(hoveredSlot);
            }
            else
            {
                hoveredSlot = null;
            }

            ElementBounds mainBounds = ElementBounds.Fixed(0, 0, 300, 150);

            ElementBounds nameBounds = ElementBounds.Fixed(0, 30, 300, 30);

            ElementBounds nameInputBounds = ElementBounds.Fixed(0, 60, 300, 30);

            ElementBounds bandnameBounds = ElementBounds.Fixed(0, 100, 300, 30);

            ElementBounds bandnameInputBounds = ElementBounds.Fixed(0, 130, 300, 30);

            ElementBounds instrumentTextBounds = ElementBounds.Fixed(0, 180, 300, 30);

            ElementBounds instrumentSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 10, 210, 4, 1);

            ElementBounds songNameBounds = ElementBounds.Fixed(100, 180, 200, 90);

            ElementBounds mediaSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 210, 1, 1);

            ElementBounds sendButtonBounds = ElementBounds.FixedSize(0, 0).FixedUnder(mediaSlotBounds, 2 * 5).WithAlignment(EnumDialogArea.CenterFixed).WithFixedPadding(10, 2);

            // 2. Around all that is 10 pixel padding
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(mainBounds);

            // 3. Finally Dialog
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);


            ClearComposers();
            SingleComposer = capi.Gui
                .CreateCompo("blockentitymusicblock" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(DialogTitle, OnTitleBarClose)
                .BeginChildElements(bgBounds)
                    .AddDynamicText(Lang.Get("Name: \"" + name + "\""), CairoFont.WhiteSmallText(), nameBounds, "name")
                    .AddTextInput(nameInputBounds, OnNameChange)
                    .AddDynamicText(Lang.Get("Band Name: \"" + bandName + "\""), CairoFont.WhiteSmallText(), bandnameBounds, "bandName")
                    .AddTextInput(bandnameInputBounds, OnBandNameChange)
                    .AddItemSlotGrid(Inventory, SendInvPacket, 1, new int[] { 0 }, instrumentSlotBounds)
                    .AddStaticText(Lang.Get("Instrument"), CairoFont.WhiteSmallText(), instrumentTextBounds)
                    .AddDynamicText(Lang.Get("Song File: \n\"" + songName + "\""), CairoFont.WhiteSmallText(), songNameBounds, "songName")
                .AddSmallButton(Lang.Get("Song Select"), OnSongSelect, sendButtonBounds, EnumButtonStyle.Normal, "songSelectButton")
                .EndChildElements()
                .Compose()
            ;

            if (hoveredSlot != null)
            {
                SingleComposer.OnMouseMove(new MouseEvent(capi.Input.MouseX, capi.Input.MouseY));
            }
        }
        private void OnNameChange(string newName)
        {
            string newText;
            if (newName != "")
                newText = "Name: \"" + newName + "\"";
            else
                newText = "Please give me a name!";
            SingleComposer.GetDynamicText("name").SetNewText(newText);

            if (newName != "")
            {
                byte[] data;
                using (MemoryStream ms = new MemoryStream())
                {
                    BinaryWriter writer = new BinaryWriter(ms);
                    writer.Write(newName);
                    data = ms.ToArray();
                }

                capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, BlockEntityPosition.Z, 1004, data);
            }
        }
        private void OnBandNameChange(string newBand)
        {
            // Called when the band name needs to change. Update the SingleComposer's Dynamic text field.
            string newText;
            if (newBand != "")
                newText = "Band Name: \"" + newBand + "\"";
            else
                newText = "No Band";
            SingleComposer.GetDynamicText("bandName").SetNewText(newText);

            byte[] data;

            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(ms);
                writer.Write(newBand);
                data = ms.ToArray();
            }

            capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, BlockEntityPosition.Z, 1005, data);
        }
        private bool OnSongSelect()
        {
            SongSelectGUI songGui = new SongSelectGUI(capi, SetSong, Definitions.GetInstance().GetSongList());
            songGui.TryOpen();
            return true;
        }
        private int SetSong(string filePath)
        {
            // Read the selected file, and send the contents to the server
            string songData = "";
            // Try to read the file. If it failed, it's propably a server file, so we should send the filename when starting playback, just as with handheld +.
            RecursiveFileProcessor.ReadFile(Definitions.GetInstance().ABCBasePath() + Path.DirectorySeparatorChar + filePath, ref songData);

            SingleComposer.GetDynamicText("songName").SetNewText("Song File: \n\"" + filePath + "\"");

            byte[] data;

            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(ms);
                writer.Write(filePath);
                writer.Write(songData);
                data = ms.ToArray();
            }

            capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, BlockEntityPosition.Z, 1006, data);
            return 1;
        }
        private void SendInvPacket(object p)
        {
            capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, BlockEntityPosition.Z, p);
        }

        private void OnTitleBarClose()
        {
            TryClose();
        }
        private void OnInventorySlotModified(int slotid)
        {
            if (slotid == 0)
            {
                //if (Inventory[0].Itemstack?.Collectible.GetType())// FirstCodePart().Equals("parcel") == true)
                ;   // Allow playback? Only allow it if the item is in the slot (and disallow other items)
            }
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            Inventory.SlotModified += OnInventorySlotModified;
        }

        public override bool OnEscapePressed()
        {
            base.OnEscapePressed();
            OnTitleBarClose();
            return TryClose();
        }
    }
}
