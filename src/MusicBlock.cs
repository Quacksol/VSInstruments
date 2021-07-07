using Vintagestory.API.Common;
using Vintagestory.API.Server;
using System.Collections.Generic; // List
using System.Diagnostics;  // Debug todo remove

using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using System.IO;
using Vintagestory.API.Datastructures;

namespace instruments
{
    class MusicBlock : Block
    {

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            //return base.OnBlockInteractStart(world, byPlayer, blockSel);
            // Open the GUI to show current song/band, instrument to use, and press another button to choose song
            // Called on the client side. 

            // test - have hard coded song, band and instrument. Send start play thing innit. Should be the same interface.


            if(world.Api.Side == EnumAppSide.Client)
            {
                // GUI stuff
            }

            if(world.Api.Side == EnumAppSide.Server)
            {
                BEMusicBlock be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEMusicBlock;
                be.OnUse(byPlayer);                
            }
            return true;
        }
    }
    class BEMusicBlock : BlockEntity
    {
        int ID; // do we actually need ids?

        string blockName = "";
        string bandName = "test"; // Todo set to blank
        string songFile = "";
        
        InstrumentItem instrument = new TrumpetItem();
        bool isPlaying = false;
        public BEMusicBlock()
        {
            // Set up inventory here - I'm copying necessaries' mailbox, seems simple enough.
            
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side != EnumAppSide.Server)
                return;
            ID = MusicBlockManager.GetInstance().GetNewID(); // todo - clientIDs also start from 0. Need either an offset, or something to say it's a block
            blockName = "Music Block " + ID;
            Debug.WriteLine(blockName);
            // todo Select songFile
            string songFilename = "HGSS- viridian forest.abc"; // remove
            RecursiveFileProcessor.ReadFile(Definitions.GetInstance().ABCBasePath() + Path.DirectorySeparatorChar + songFilename, ref songFile);
        }
        
        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetString("name", blockName);
            tree.SetString("band", bandName);
            tree.SetString("file", songFile);
            //tree.SetString("instrument", blockName);
        }
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            blockName = tree.GetString("name");
            bandName = tree.GetString("band");
            songFile = tree.GetString("file");
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

            if(isPlaying)
            {
                ABCParser abcp = ABCParsers.GetInstance().FindByID(ID);
                ABCStopFromServer packet = new ABCStopFromServer(); // todo copied from main, make a function
                packet.fromClientID = ID;
                IServerNetworkChannel ch = (Api as ICoreServerAPI).Network.GetChannel("abc");
                ch.BroadcastPacket(packet);
                ABCParsers.GetInstance().Remove(abcp);
            }
            
        }
        public void OnUse(IPlayer byPlayer)
        {
            if (byPlayer.WorldData.EntityControls.Sneak)
            {
                // Shift held - do setup stuff
            }
            else
            {
                // Play the song using the current setup
                if (!isPlaying)
                {
                    // Make a new ABCPlayer!
                    ABCParser abcp = new ABCParser(Api as ICoreServerAPI, ID, Pos.ToVec3d(), blockName, songFile, instrument.instrument, bandName, 0);
                    ExitStatus parseOk = abcp.Start();
                    if (parseOk != ExitStatus.allGood)
                        Debug.WriteLine(":(");// BadABC(abcp.playerID, abcp.charIndex);
                    else
                        ABCParsers.GetInstance().Add(abcp);
                }
                else
                {
                    ABCParser abcp = ABCParsers.GetInstance().FindByID(ID);
                    ABCStopFromServer packet = new ABCStopFromServer(); // todo copied from main, make a function
                    packet.fromClientID = ID;
                    IServerNetworkChannel ch = (Api as ICoreServerAPI).Network.GetChannel("abc");
                    ch.BroadcastPacket(packet);
                    ABCParsers.GetInstance().Remove(abcp);
                }
                isPlaying = !isPlaying;
            }


            Debug.WriteLine(ID);
        }
        /*
        public void OnBlockInteract(IPlayer byPlayer)
        {
            if(Api.Side == EnumAppSide.Client)
            {
                // commented out gui stuff
            }
            else
            {
                byte[] data;

                using (MemoryStream ms = new MemoryStream())
                {
                    BinaryWriter writer = new BinaryWriter(ms);
                    TreeAttribute tree = new TreeAttribute();
                    inventory.ToTreeAttributes(tree);
                    tree.ToBytes(writer);
                    data = ms.ToArray();
                }

                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer,
                    Pos.X, Pos.Y, Pos.Z,
                    (int)EnumBlockStovePacket.OpenGUI,
                    data
                );
            }
        }
        */
        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            base.OnReceivedServerPacket(packetid, data);
            // The server saw a player tried to open the music box - it sent a packet, and here it is!
            // Open 
        }
    }
    public class MusicBlockManager // I've gone singleton crazy
    {
        public int nMusicBlocks = 0;
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
            int i = 0;
            for(int ID=0; ID<activeBlockIDs.Count; ID++)
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
}
