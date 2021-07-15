using Vintagestory.API.Client;
using Vintagestory.API.Common;
using System.Collections.Generic; // Lists
using System.Diagnostics; // debug todo remove
using Vintagestory.API.MathTools;

using System.IO;                // Binary writer n that
using Vintagestory.API.Config;  // Lang stuff

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
                .AddDynamicText("No note selected!", CairoFont.WhiteDetailText(), EnumTextOrientation.Center, textBounds, "note")
                .Compose();
        }

        public void UpdateText(string newText)
        {
            // Called when the text needs to change. Update the SingleComposer's Dynamic text field.
            SingleComposer.GetDynamicText("note").SetNewText(newText);
        }
    }

    public class ModeSelectGUI : GuiDialog
    {
        Func<PlayMode, int> ChangeInstrumentMode;
        event Action<string> bandNameChange;
        public override string ToggleKeyCombinationCode => null;
        public ModeSelectGUI(ICoreClientAPI capi, Func<PlayMode, int> ChangeMode, Action<string> bandChange, string bandName) : base(capi)
        {
            ChangeInstrumentMode = ChangeMode;
            bandNameChange = bandChange;
            SetupDialog(bandName);
        }

        private void SetupDialog(string bandName)
        {
            // Auto-sized dialog at the center of the screen
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds textBounds = ElementBounds.Fixed(0, 130, 300, 60);

            // Background boundaries. Again, just make it fit it's child elements, then add the text as a child element
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(textBounds);

            ElementBounds box1Bounds = ElementBounds.Fixed(16, 40, 32, 32);
            ElementBounds box2Bounds = ElementBounds.Fixed(16, 88, 32, 32);
            ElementBounds box3Bounds = ElementBounds.Fixed(16, 136, 32, 32);
            ElementBounds box4Bounds = ElementBounds.Fixed(16, 184, 32, 32);
            ElementBounds bandStringBounds = ElementBounds.Fixed(120, 152, 220, 32);
            ElementBounds bandStringNewBounds = ElementBounds.Fixed(108, 186, 64, 32);
            ElementBounds bandBoxBounds = ElementBounds.Fixed(180, 185, 128, 32);
            // Lastly, create the dialog
            Action<string> ub = UpdateBand;

            SingleComposer = capi.Gui.CreateCompo("ModeSelectDialog", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Mode select", Close)
                .AddButton("  SemiTone Step  ", () => { ChangeInstrumentMode(PlayMode.lockedSemiTone); TryClose(); return true; }, box1Bounds)
                .AddButton("  Tone Step  ", () => { ChangeInstrumentMode(PlayMode.lockedTone); TryClose(); return true; }, box2Bounds)
                .AddButton("  Fluid  ", () => { ChangeInstrumentMode(PlayMode.fluid); TryClose(); return true; }, box3Bounds)
                .AddButton("  ABC  ", () => { ChangeInstrumentMode(PlayMode.abc); TryClose(); return true; }, box4Bounds)
                .AddDynamicText("", CairoFont.WhiteDetailText(), EnumTextOrientation.Left, bandStringBounds, "Band name")
                .AddStaticText("Set Band Name:", CairoFont.WhiteDetailText(), EnumTextOrientation.Left, bandStringNewBounds)
                .AddTextInput(bandBoxBounds, ub)
                .Compose();
            UpdateBand(bandName);
        }
        private void Close()
        {
            TryClose();
        }
        public void UpdateBand(string bandName)
        {
            // Called when the text needs to change. Update the SingleComposer's Dynamic text field.
            string newText;
            if (bandName != "")
                newText = "Band Name: \"" + bandName + "\"";
            else
                newText = "No Band";
            SingleComposer.GetDynamicText("Band name").SetNewText(newText);
            bandNameChange(bandName);
        }
    }

    public class SongSelectGUI : GuiDialog
    {
        public override string ToggleKeyCombinationCode => null;
        //Func<string, int> PlaySong;
        Func<string, int> PlaySong;

        int listHeight = 500;
        int listWidth = 700;

        private string filter = "";

        private List<GuiHandbookPage> allStackListItems = new List<GuiHandbookPage>();
        private List<GuiHandbookPage> shownStackListItems = new List<GuiHandbookPage>();

        public SongSelectGUI(ICoreClientAPI capi, Func<string, int> playSong, List<string> files) : base(capi)
        {
            SetupDialog(files);
            PlaySong = playSong;
        }
        public override void OnGuiOpened()
        {
            FilterSongs();
            SingleComposer.GetTextInput("search").SetValue("");
            base.OnGuiOpened();
        }
        private void SetupDialog(List<string> files)
        {
            // https://github.com/anegostudios/vsessentialsmod/blob/master/Gui/GuiDialogHandbook.cs

            ElementBounds searchFieldBounds = ElementBounds.Fixed(GuiStyle.ElementToDialogPadding - 2, 32, 300, 32);    // little bar at the top
            ElementBounds stackListBounds = ElementBounds.Fixed(0, 0, listWidth, listHeight).FixedUnder(searchFieldBounds, 32);
            ElementBounds insetBounds = stackListBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);                // Not sure lol
            ElementBounds clipBounds = stackListBounds.ForkBoundingParent();                                            // not sure
            ElementBounds scrollbarBounds = stackListBounds.CopyOffsetedSibling(3 + stackListBounds.fixedWidth + 7).WithFixedWidth(20);

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
            SingleComposer = capi.Gui.CreateCompo("SongSelectDialog", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Song select", Close)
                // Add search bar here?
                .BeginChildElements(bgBounds)
                .AddTextInput(searchFieldBounds, (string newText) =>
                {
                    filter = newText;
                    FilterSongs();
                },
                CairoFont.WhiteSmallishText(), "search")
                .BeginClip(clipBounds)
                    .AddInset(insetBounds,3)
                    .AddInteractiveElement(new GuiElementHandbookList(capi, stackListBounds, (int index) => {
                        ButtonPressed(index);
                    },
                    shownStackListItems),
                    "stacklist"
                    )

                .EndClip()
                .AddVerticalScrollbar((float value) =>
                {
                    GuiElementHandbookList stacklist = SingleComposer.GetHandbookStackList("stacklist");
                    stacklist.insideBounds.fixedY = 3 - value;
                    stacklist.insideBounds.CalcWorldBounds();
                }, scrollbarBounds, "scrollbar")
                .EndChildElements()
                .Compose()
            ;

        }

        private void OnNewScrollbarValue()
        {
            // Max val of value will depend on how many songs are in the folder
            GuiElementScrollbar scrollbar = SingleComposer.GetScrollbar("scrollbar");
            GuiElementHandbookList stacklist = SingleComposer.GetHandbookStackList("stacklist");
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
                if(filter != "")
                {
                    string lowerCase = song.Title.ToLower();
                    if (!lowerCase.Contains(filter.ToLower()))
                        continue;
                }
                shownStackListItems.Add(song);
            }
            GuiElementHandbookList stacklist = SingleComposer.GetHandbookStackList("stacklist");
            stacklist.CalcTotalHeight();
            OnNewScrollbarValue();
        }
        private void Close()
        {
            TryClose();
        }
    }
    public class MusicBlockGUI : GuiDialogBlockEntity
    {

        public MusicBlockGUI(string title, InventoryBase inventory, BlockPos bePos, ICoreClientAPI capi, string blockName, string bandName) : base(title, inventory, bePos, capi)
        {
            if (IsDuplicate)
                return;
            capi.World.Player.InventoryManager.OpenInventory(Inventory);
            SetupDialog(blockName, bandName);
        }
        private void SetupDialog(string name, string bandName)
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

            ElementBounds instrumentSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 210, 4, 1);

            ElementBounds mediaSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 250, 1, 1);

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
                    .AddDynamicText(Lang.Get("Name: \"" + name + "\""), CairoFont.WhiteSmallText(), EnumTextOrientation.Left, nameBounds, "name")
                    .AddTextInput(nameInputBounds, OnNameChange)
                    .AddDynamicText(Lang.Get("Band Name: \"" + bandName + "\""), CairoFont.WhiteSmallText(), EnumTextOrientation.Left, bandnameBounds, "bandName")
                    .AddTextInput(bandnameInputBounds, OnBandNameChange)
                    .AddItemSlotGrid(Inventory, SendInvPacket, 1, new int[] { 0 }, instrumentSlotBounds)
                    .AddStaticText(Lang.Get("Instrument"), CairoFont.WhiteSmallText(), instrumentTextBounds)
                //.AddSmallButton(Lang.Get("necessaries:mailbox-send"), OnButtonSend, sendButtonBounds, EnumButtonStyle.Normal, EnumTextOrientation.Center, "sendBtn")
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
                newText = "No Band";
            SingleComposer.GetDynamicText("name").SetNewText(newText);

            byte[] data;

            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(ms);
                writer.Write(newName);
                data = ms.ToArray();
            }

            capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, BlockEntityPosition.Z, 1004, data);
        }
        private void OnBandNameChange(string newBand)
        {
            // Called when the text needs to change. Update the SingleComposer's Dynamic text field.
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
