using System;
using System.Collections.Generic; // List
using System.Diagnostics;  // Debug todo remove
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;


namespace instruments
{
    internal class MusicBlock : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            //return base.OnBlockInteractStart(world, byPlayer, blockSel);
            // Open the GUI to show current song/band, instrument to use, and press another button to choose song
            // Called on the client side. 

            if (world.Api.Side == EnumAppSide.Client)
            {
                // GUI stuff

            }

            if (world.Api.Side == EnumAppSide.Server)
            {
                //if (!byPlayer.WorldData.EntityControls.Sneak)
                {
                    BEMusicBlock be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEMusicBlock;
                    if (be != null)
                        be.OnUse(byPlayer);
                }
            }
            return true;
        }
    }

    internal class BEMusicBlock : BlockEntityContainer
    {
        int ID;

        string blockName = "Music Block";
        string bandName = "";
        string songData = "";
        string songName = "No abc selected!";   // Only used to show the current song, not for anything smart

        internal MusicBlockInventory inventory;
        MusicBlockGUI musicBlockGUI;

        InstrumentType instrumentType = InstrumentType.none;
        public bool isPlaying = false;
        public BEMusicBlock()
        {
            // Set up inventory here - I'm copying necessaries' mailbox, seems simple enough.
            inventory = new MusicBlockInventory(null, null);
            inventory.SlotModified += OnSlotModified;
        }
        public override InventoryBase Inventory
        {
            get { return inventory; }
        }
        public override string InventoryClassName
        {
            get { return "musicblock"; }
        }
        public virtual string DialogTitle
        {
            get { return Lang.Get("Music Block"); }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side != EnumAppSide.Server)
                return;
            ID = MusicBlockManager.GetInstance().GetNewID();
            OnSlotModified(0); // Parses the item in the inventory slot
        }
        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetString("name", blockName);
            tree.SetString("band", bandName);
            tree.SetString("file", songData);
            tree.SetString("songname", songName);
        }
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            blockName = tree.GetString("name");
            bandName = tree.GetString("band");
            songData = tree.GetString("file");
            songName = tree.GetString("songname");
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
        }
        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            if (Api.Side != EnumAppSide.Server)
                return;
            MusicBlockManager.GetInstance().RemoveID(ID);

            if (isPlaying)
            {
                ABCParser abcp = ABCParsers.GetInstance().FindByID(ID);
                ABCStopFromServer packet = new ABCStopFromServer(); // todo copied from main, make a function
                packet.fromClientID = ID;
                IServerNetworkChannel ch = (Api as ICoreServerAPI).Network.GetChannel("abc");
                ch.BroadcastPacket(packet);
                ABCParsers.GetInstance().Remove((Api as ICoreServerAPI), null, abcp);
            }
        }
        public void OnUse(IPlayer byPlayer)
        {
            if (!byPlayer.WorldData.EntityControls.Sneak)
            {
                // Play the song using the current setup
                if (!isPlaying)
                {
                    // Make a new ABCPlayer!
                    if (blockName != "" && songName != "" && instrumentType != InstrumentType.none)
                    {
                        if (songData == "") // If there is no songData, the file is probably a server file. Read it from the abc_server folder
                        {
                            string abcServerBaseDir = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "abc_server"; // EXTREME todo this is copied from main, make into one thing

                            RecursiveFileProcessor.ReadFile(abcServerBaseDir + Path.DirectorySeparatorChar + songName, ref songData);
                            if (songData == "")  // If songData is still empty, then the song wasn't found (or one wasn't selected)
                                return;

                            ABCParsers.GetInstance().MakeNewParser(Api as ICoreServerAPI, byPlayer,
                                songData, ID, blockName, bandName, Pos.ToVec3d(), instrumentType);
                        }
                        else
                        {
                            ABCParsers.GetInstance().MakeNewParser(Api as ICoreServerAPI, byPlayer,
                                songData, ID, blockName, bandName, Pos.ToVec3d(), instrumentType);
                        }
                    }
                    else
                        return;
                }
                else
                {
                    ABCParser abcp = ABCParsers.GetInstance().FindByID(ID);
                    ABCStopFromServer packet = new ABCStopFromServer(); // todo copied from main, make a function
                    packet.fromClientID = ID;
                    IServerNetworkChannel ch = (Api as ICoreServerAPI).Network.GetChannel("abc");
                    ch.BroadcastPacket(packet);
                    ABCParsers.GetInstance().Remove((Api as ICoreServerAPI), byPlayer, abcp);
                }
                isPlaying = !isPlaying;
            }
            else
            {
                byte[] data;

                using (MemoryStream ms = new MemoryStream())
                {
                    BinaryWriter writer = new BinaryWriter(ms);
                    writer.Write(byPlayer.PlayerName);
                    writer.Write(bandName);
                    writer.Write(songData);
                    TreeAttribute tree = new TreeAttribute();
                    inventory.ToTreeAttributes(tree);
                    tree.ToBytes(writer);
                    data = ms.ToArray();
                }

                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer,
                    Pos.X, Pos.Y, Pos.Z,
                    69,
                    data
                );
                byPlayer.InventoryManager.OpenInventory(inventory);
            }
        }
        public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
        {
            if (packetid <= 1000)   // Called when the client changes the inventory slot
            {
                inventory.InvNetworkUtil.HandleClientPacket(fromPlayer, packetid, data);
            }

            if (packetid == 1004) // Name change
            {
                if (data != null)
                {
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        BinaryReader reader = new BinaryReader(ms);
                        blockName = reader.ReadString();
                        if (blockName == null)
                            blockName = "";
                    }
                    MarkDirty();
                }
                if (fromPlayer.InventoryManager != null)
                {
                    fromPlayer.InventoryManager.CloseInventory(Inventory);
                }
            }

            if (packetid == 1005) // Band change
            {
                if (data != null)
                {
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        BinaryReader reader = new BinaryReader(ms);
                        bandName = reader.ReadString();
                        if (bandName == null)
                            bandName = "";
                    }
                    MarkDirty();
                }
                if (fromPlayer.InventoryManager != null)
                {
                    fromPlayer.InventoryManager.CloseInventory(Inventory);
                }
            }

            if (packetid == 1006) // Song select
            {
                if (data != null)
                {
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        BinaryReader reader = new BinaryReader(ms);
                        songName = reader.ReadString();
                        songData = reader.ReadString();
                        if (songData == null)
                            songData = "";
                    }
                    MarkDirty();
                }
                if (fromPlayer.InventoryManager != null)
                {
                    fromPlayer.InventoryManager.CloseInventory(Inventory);
                }
            }
        }
        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            // The server saw a player tried to open the music box - it sent a packet, and here it is!
            // Open the gui.
            base.OnReceivedServerPacket(packetid, data);
            if (packetid == 69)
            {
                using (MemoryStream ms = new MemoryStream(data))
                {
                    BinaryReader reader = new BinaryReader(ms);
                    string playerName = reader.ReadString();
                    bandName = reader.ReadString();
                    songData = reader.ReadString();
                    TreeAttribute tree = new TreeAttribute();
                    tree.FromBytes(reader);
                    Inventory.FromTreeAttributes(tree);
                    Inventory.ResolveBlocksOrItems();

                    IClientWorldAccessor clientWorld = (IClientWorldAccessor)Api.World;

                    if (!Definitions.GetInstance().UpdateSongList(Api as ICoreClientAPI))
                        return;

                    if (musicBlockGUI == null)
                    {
                        musicBlockGUI = new MusicBlockGUI(DialogTitle, Inventory, Pos, Api as ICoreClientAPI, blockName, bandName, songName);
                        musicBlockGUI.OnClosed += () =>
                        {
                            musicBlockGUI = null;
                        };
                    }

                    musicBlockGUI.TryOpen();
                }
            }
        }
        private void OnSlotModified(int slotid)
        {
            // set the instrument type here?
            ItemStack item = inventory[slotid].Itemstack;
            if (item != null)
            {
                InstrumentItem dummy = (item.Item as InstrumentItem);
                if (dummy != null)
                    instrumentType = dummy.instrument;
            }
            else
                instrumentType = InstrumentType.none;
        }
    }
    public class MusicBlockManager // I've gone singleton crazy
    {
        const int IDOffset = 1000;
        public List<int> activeBlockIDs;

        private static MusicBlockManager _instance;
        private MusicBlockManager()
        {
            activeBlockIDs = new List<int>();
        }
        public static MusicBlockManager GetInstance()
        {
            if (_instance != null)
                return _instance;
            return _instance = new MusicBlockManager();
        }
        public void Reset()
        {
            activeBlockIDs.Clear();
        }
        public int GetNewID()
        {
            // Search the list for a free ID
            int i = IDOffset;
            for (int ID = 0; ID < activeBlockIDs.Count; ID++)
            {
                if (activeBlockIDs.Contains(i))
                {
                    i++;
                    continue;
                }
                else
                    break;
            }
            activeBlockIDs.Add(i);
            return i;
        }
        public void RemoveID(int id)
        {
            activeBlockIDs.Remove(id);
        }
    }

    internal class MusicBlockInventory : InventoryBase, ISlotProvider
    {
        // 'Borrowed' from necessaries' mailbox. Thanks Zig <3
        ItemSlot[] slots;
        public ItemSlot[] Slots { get { return slots; } }
        public override int Count
        {
            get { return slots.Length; }
        }

        public MusicBlockInventory(string inventoryID, ICoreAPI api) : base(inventoryID, api)
        {
            // Only 1 slot, for the instrument
            slots = GenEmptySlots(1);
        }

        public MusicBlockInventory(string className, string instanceID, ICoreAPI api) : base(className, instanceID, api)
        {
            slots = GenEmptySlots(1);
        }

        public override ItemSlot this[int slotId]
        {
            get
            {
                if (slotId < 0 || slotId >= Count) return null;
                return slots[slotId];
            }
            set
            {
                if (slotId < 0 || slotId >= Count) throw new ArgumentOutOfRangeException(nameof(slotId));
                if (value == null) throw new ArgumentNullException(nameof(value));
                slots[slotId] = value;
            }
        }


        public override void FromTreeAttributes(ITreeAttribute tree)
        {
            slots = SlotsFromTreeAttributes(tree);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            SlotsToTreeAttributes(slots, tree);
        }
    }
}
