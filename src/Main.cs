using Vintagestory.API.MathTools; // vec3D
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config; // GlobalConstants
using Vintagestory.API.Server;
using ProtoBuf;
using System;               // Array.Find()
using System.Collections.Generic; // List
using System.Diagnostics;  // Debug todo remove

using System.IO; // Open files

//using System.IO; // Open files
//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;

namespace instruments
{
    public enum PlayMode
    {
        lockedTone = 0, // Player y angle floored to nearest tone
        lockedSemiTone, // Player y angle floored to nearest semi-tone
        fluid,      // Player y angle directly correlates to pitch
        abc         // Playing an abc file
    }

    public struct NoteFrequency
    {
        public string ID;
        public float pitch;
        public NoteFrequency(string id, float p)
        {
            this.ID = id;
            this.pitch = p;
        }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class NoteStart
    {
        public float pitch;
        public Vec3d positon;
        public int ID;
        public InstrumentType instrument;
    }
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class NoteUpdate // Same as NoteStart, any better way to do this?
    {
        public float pitch;
        public Vec3d positon;
        public int ID;
    }
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class NoteStop
    {
        public int ID;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ABCStartFromClient
    {
        public string abcData;
        public string bandName;
        public InstrumentType instrument;
        public bool isServerFile;
    }
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ABCStopFromClient
    {
        public bool dummy;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ABCUpdateFromServer
    {
        public Vec3d positon;
        public Chord newChord;
        public int fromClientID;
        public InstrumentType instrument;
    }
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ABCStopFromServer
    {
        public int fromClientID;
    }
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ABCSendSongFromServer
    {
        public string abcFilename;
    }

    public class InstrumentModCommon : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterItemClass("acousticguitar", typeof(AcousticGuitarItem));
            api.RegisterItemClass("accordion", typeof(AccordionItem));
            api.RegisterItemClass("clarinet", typeof(ClarinetItem));
            api.RegisterItemClass("dulcimer", typeof(DulcimerItem));
            api.RegisterItemClass("drum", typeof(DrumItem));
            api.RegisterItemClass("grandpiano", typeof(GrandPianoItem));
            api.RegisterItemClass("harp", typeof(HarpItem));
            api.RegisterItemClass("mic", typeof(MicItem));
            api.RegisterItemClass("musicbox", typeof(MusicBoxItem));
            api.RegisterItemClass("sax", typeof(SaxItem));
            api.RegisterItemClass("steeldrum", typeof(SteelDrumItem));
            api.RegisterItemClass("trumpet", typeof(TrumpetItem));
            api.RegisterItemClass("violin", typeof(ViolinItem));

            //api.RegisterBlockClass("musicblock", typeof(MusicBlock));
            //api.RegisterBlockEntityClass("musicblockentity", typeof(BEMusicBlock));
        }
    }

    public class InstrumentModClient : InstrumentModCommon
    {
        public override bool ShouldLoad(EnumAppSide side) // Enabling this will kill the sounds, cos the sounds are made server side smh. Might need to separate the ui stuff with sounds.
        {
            return side == EnumAppSide.Client;
        }

        #region CLIENT
        IClientNetworkChannel clientChannelNote;
        IClientNetworkChannel clientChannelABC;
        ICoreClientAPI clientApi;
        string playerHeldItem; // The item the player is holding. If this changes, stop playback.
        bool thisClientPlaying; // Is this client currently playing, or is some other client playing?
        List<Sound> soundList = new List<Sound>(); // For playing single notes sent by players, non-abc style
        List<SoundManager> soundManagers;

        private Dictionary<InstrumentType, string> soundLocations = new Dictionary<InstrumentType, string>();


        long listenerIDClient = -1;
        public override void StartClientSide(ICoreClientAPI api)
        {
            clientApi = api;
            //base.StartClientSide(api);
            clientChannelNote =
                api.Network.RegisterChannel("noteTest")
                .RegisterMessageType(typeof(NoteStart))
                .RegisterMessageType(typeof(NoteUpdate))
                .RegisterMessageType(typeof(NoteStop))
                .SetMessageHandler<NoteStart>(MakeNote)
                .SetMessageHandler<NoteUpdate>(UpdateNote)
                .SetMessageHandler<NoteStop>(StopNote)
                ;
            clientChannelABC =
                api.Network.RegisterChannel("abc")
                .RegisterMessageType(typeof(ABCStartFromClient))    // This needs to be here, even if there's no Message Handler
                .RegisterMessageType(typeof(ABCStopFromClient))     // I guess it's in order for the client to send stuff up to server, and below stuff is for receiving
                .RegisterMessageType(typeof(ABCUpdateFromServer))
                .RegisterMessageType(typeof(ABCStopFromServer))
                .RegisterMessageType(typeof(ABCSendSongFromServer))
                .SetMessageHandler<ABCUpdateFromServer>(ParseServerPacket)
                .SetMessageHandler<ABCStopFromServer>(StopSounds)
                .SetMessageHandler<ABCSendSongFromServer>(SongFromServer)
                ;

            soundManagers = new List<SoundManager>();

            soundLocations.Add(InstrumentType.accordian, "sounds/accordion");
            soundLocations.Add(InstrumentType.acousticGuitar, "sounds/acousticguitar");
            soundLocations.Add(InstrumentType.clarinet, "sounds/clarinet");
            soundLocations.Add(InstrumentType.dulcimer, "sounds/dulcimer");
            soundLocations.Add(InstrumentType.drum, "sounds/drum");
            soundLocations.Add(InstrumentType.grandPiano, "sounds/grandpiano");
            soundLocations.Add(InstrumentType.harp, "sounds/harp");
            soundLocations.Add(InstrumentType.mic, "sounds/mic");
            soundLocations.Add(InstrumentType.musicBox, "sounds/musicbox");
            soundLocations.Add(InstrumentType.trumpet, "sounds/trumpet");
            soundLocations.Add(InstrumentType.sax, "sounds/sax");
            soundLocations.Add(InstrumentType.steelDrum, "sounds/steeldrum");
            soundLocations.Add(InstrumentType.violin, "sounds/violin");

            thisClientPlaying = false;
        }
        private void MakeNote(NoteStart note)
        {
            if (note.instrument == InstrumentType.drum || note.instrument == InstrumentType.mic) // todo
                return;
            IClientWorldAccessor clientWorldAccessor = clientApi.World;
            Sound sound = new Sound(clientWorldAccessor, note.positon, note.pitch, soundLocations[note.instrument] + "/a3.ogg", note.ID);
            soundList.Add(sound);
        }
        private void UpdateNote(NoteUpdate note)
        {
            //Sound sound = soundList.Find(x => x.ID.Contains(note.ID));
            Sound sound = soundList.Find(x => (x.ID == note.ID));
            if (sound == null)
                return;
            sound.UpdateSound(note.positon, note.pitch);
        }
        private void StopNote(NoteStop note)
        {
            //Sound sound = soundList.Find(x => x.ID.Contains(note.ID));
            Sound sound = soundList.Find(x => (x.ID == note.ID));
            if (sound == null)
                return;
            sound.StopSound();
            soundList.Remove(sound);
        }

        private void ParseServerPacket(ABCUpdateFromServer serverPacket)
        {
            IClientPlayer player = clientApi.World.Player; // If the client is still starting up, this will be null!
            if (player == null)
                return;

            SoundManager sm = soundManagers.Find(x => (x.sourceID == serverPacket.fromClientID));
            if (sm == null)
            {
                // This was the first packet from the server with data from this client. Need to register a new SoundManager.
                long startTime = serverPacket.newChord.startTime;
                sm = new SoundManager(clientApi.World, serverPacket.fromClientID, soundLocations[serverPacket.instrument], serverPacket.instrument, startTime);
                soundManagers.Add(sm);
            }
            if (listenerIDClient == -1)
            {
                // This was the first abc packet from the server ever - need to register the tick listener.
                listenerIDClient = clientApi.Event.RegisterGameTickListener(OnClientGameTick, 1);
                if (serverPacket.fromClientID == player.ClientId)
                {
                    thisClientPlaying = true;
                    playerHeldItem = clientApi.World.Player.Entity.RightHandItemSlot.GetStackName();
                }
            }
            sm.AddChord(serverPacket.positon, serverPacket.newChord);
        }
        private void StopSounds(ABCStopFromServer serverPacket)
        {
            IClientPlayer player = clientApi.World.Player; // If the client is still starting up, this will be null!
            if (player == null)
                return;

            SoundManager sm = soundManagers.Find(x => (x.sourceID == serverPacket.fromClientID));
            if (sm != null)
            {
                if (sm.sourceID == player.ClientId)
                    thisClientPlaying = false;
                sm.Kill();
                soundManagers.Remove(sm);
                CheckSoundManagersEmpty();
            }
        }
        private void SongFromServer(ABCSendSongFromServer serverPacket)
        {
            Definitions.GetInstance().AddToServerSongList(serverPacket.abcFilename);
        }

        private void OnClientGameTick(float dt)
        {
            int smCount = soundManagers.Count;
            for (int i = 0; i < smCount; i++)
            {
                if (soundManagers[i].Update(dt))
                    ;
                else
                {
                    if (soundManagers[i].sourceID == clientApi.World.Player.ClientId)
                        thisClientPlaying = false;
                    soundManagers.RemoveAt(i);
                    smCount--;
                    i--;
                }
            }
            CheckSoundManagersEmpty();
            if (thisClientPlaying)
            {
                string currentPlayerItem = clientApi.World.Player.Entity.RightHandItemSlot.GetStackName();
                if (currentPlayerItem != playerHeldItem) // Check that the player is still holding an instrument
                {
                    // TODO copied from in instrument. Make into a single function pls
                    ABCStopFromClient newABC = new ABCStopFromClient();
                    IClientNetworkChannel ch = clientApi.Network.GetChannel("abc");
                    ch.SendPacket(newABC);
                    thisClientPlaying = false;
                }
            }
        }
        private void CheckSoundManagersEmpty()
        {
            if (soundManagers.Count == 0)
            {
                clientApi.Event.UnregisterGameTickListener(listenerIDClient);
                listenerIDClient = -1;
                thisClientPlaying = false;
            }
        }

        #endregion
    }

    public class InstrumentModServer : InstrumentModCommon
    {
        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }
        #region SERVER
        private ICoreServerAPI serverAPI;
        IServerNetworkChannel serverChannelNote;
        IServerNetworkChannel serverChannelABC;

        long listenerID = -1;
        string abcBaseDir;

        struct PlaybackData
        {
            public int ClientID;
            public string abcData;
            public ABCParser parser;
            public int index;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            serverAPI = api;
            base.StartServerSide(api);
            serverChannelNote =
                api.Network.RegisterChannel("noteTest")
                .RegisterMessageType(typeof(NoteStart))
                .RegisterMessageType(typeof(NoteUpdate))
                .RegisterMessageType(typeof(NoteStop))
                .SetMessageHandler<NoteStart>(RelayMakeNote)
                .SetMessageHandler<NoteUpdate>(RelayUpdateNote)
                .SetMessageHandler<NoteStop>(RelayStopNote)
                ;
            serverChannelABC =
                api.Network.RegisterChannel("abc")
                .RegisterMessageType(typeof(ABCStartFromClient))
                .RegisterMessageType(typeof(ABCStopFromClient))
                .RegisterMessageType(typeof(ABCUpdateFromServer))
                .RegisterMessageType(typeof(ABCStopFromServer))
                .RegisterMessageType(typeof(ABCSendSongFromServer))
                .SetMessageHandler<ABCStartFromClient>(StartABC)
                .SetMessageHandler<ABCStopFromClient>(StopABC)
                .SetMessageHandler<ABCStopFromServer>(null)
                .SetMessageHandler<ABCUpdateFromServer>(null)
                ;

                serverAPI.Event.RegisterGameTickListener(OnServerGameTick, 1); // arg1 is millisecond Interval
            MusicBlockManager.GetInstance().Reset();

            abcBaseDir = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "abc_server";
            serverAPI.Event.PlayerJoin += SendSongs;
        }
        public void SendSongs(IServerPlayer byPlayer)
        {
            if (!RecursiveFileProcessor.DirectoryExists(abcBaseDir))
                return; // Server has no abcs, do nothing

            List<string> abcFiles = new List<string>();
            RecursiveFileProcessor.ProcessDirectory(abcBaseDir, abcBaseDir + Path.DirectorySeparatorChar, ref abcFiles);
            if (abcFiles.Count == 0)
            {
                return; // No files in the folder
            }
            foreach(string song in abcFiles)
            {
                ABCSendSongFromServer packet = new ABCSendSongFromServer();
                packet.abcFilename = song;
                serverChannelABC.SendPacket(packet, byPlayer);
            }
        }
        private void RelayMakeNote(IPlayer fromPlayer, NoteStart note)
        {
            // Send A packet to all clients (or clients within the area?) to start a note
            note.ID = fromPlayer.ClientId;
            serverChannelNote.BroadcastPacket(note);
        }
        private void RelayUpdateNote(IPlayer fromPlayer, NoteUpdate note)
        {
            // Send A packet to all clients (or clients within the area?) to start a note
            note.ID = fromPlayer.ClientId;
            serverChannelNote.BroadcastPacket(note);
        }
        private void RelayStopNote(IPlayer fromPlayer, NoteStop note)
        {
            // Send A packet to all clients (or clients within the area?) to start a note
            note.ID = fromPlayer.ClientId;
            serverChannelNote.BroadcastPacket(note);
        }
        private void StartABC(IPlayer fromPlayer, ABCStartFromClient abcData)
        {
            // TEST
            /*
            ABCParser abcps = abcParsers.Find(x => x.playerID == 20);
            if (abcps == null)
            {
                string filey = "";
                RecursiveFileProcessor.ReadFile("C:\\Program Files\\Vintagestory\\abc\\zelda\\Dark World.abc", ref filey);
                abcps = new ABCParser(serverAPI, 20, filey, InstrumentType.clarinet, "s", 0);
                abcps.Start();
                abcParsers.Add(abcps);
            }*/
            /*
            ABCParser abcps2 = abcParsers.Find(x => x.playerID == 21);
            if (abcps2 == null)
            {
                string filey = "";
                RecursiveFileProcessor.ReadFile("C:\\Program Files\\Vintagestory\\abc\\pokemon theme\\ocarina.abc", ref filey);
                abcps2 = new ABCParser(serverAPI, 20, filey, InstrumentType.clarinet, "s", 0);
                abcps2.Start();
                abcParsers.Add(abcps2);
            }*/
            /*
            ABCParser abcps3 = abcParsers.Find(x => x.playerID == 22);
            if (abcps3 == null)
            {
                string filey = "";
                RecursiveFileProcessor.ReadFile("C:\\Program Files\\Vintagestory\\abc\\pokemon theme\\perc.abc", ref filey);
                abcps3 = new ABCParser(serverAPI, 20, filey, InstrumentType.drum, "s", 0);
                abcps3.Start();
                abcParsers.Add(abcps3);
            }
            */

            ABCParser abcp = ABCParsers.GetInstance().FindByID(fromPlayer.ClientId);
            if (abcp == null)
            {
                int masterTime = 0; // If not part of a band, startTime is 0
                if (abcData.bandName != "")
                {
                    bool bandFound = false;
                    List<string> bandPlayerNames = new List<string>();
                    foreach (ABCParser p in ABCParsers.GetInstance().Get())
                    {
                        if (p.bandName == abcData.bandName)
                        {
                            masterTime = p.currentTime; // Otherwise, copy the master's time
                            bandFound = true;
                            IPlayer player = Array.Find(serverAPI.World.AllOnlinePlayers, x => x.ClientId == p.playerID); // Get the player that owns the abcParser
                            if (player != null) // Player might have left, or I'm doing weird test stuff
                                bandPlayerNames.Add(player.PlayerName);
                            else
                                bandPlayerNames.Add("ERROR");
                            break;
                        }
                    }
                    if (bandFound)
                    {
                        string message = "Joining band \"" + abcData.bandName + "\" with players";
                        foreach (string name in bandPlayerNames)
                            message += " " + name + ",";
                        MessageToClient(fromPlayer.ClientId, message.Substring(0, message.Length - 1)); // substring stuff to remove the last comma
                    }
                    else
                    {
                        masterTime = -3000; // Start 3 seconds late, so that other players have time to start
                        MessageToClient(fromPlayer.ClientId, "Starting new band \"" + abcData.bandName + "\"!");
                    }
                }
                else
                    MessageToClient(fromPlayer.ClientId, "Starting solo abc playback!");

                string abcSong = "";
                if(abcData.isServerFile)
                {
                    // The contained string is NOT a full song, but a link to it on the server.
                    // Find this file, load it, and make the abcParser in the same way
                    RecursiveFileProcessor.ReadFile(abcBaseDir + Path.DirectorySeparatorChar + abcData.abcData, ref abcSong);
                }
                else
                {
                    abcSong = abcData.abcData;
                }

                abcp = new ABCParser(serverAPI, fromPlayer.ClientId, abcSong, abcData.instrument, abcData.bandName, masterTime);
                ExitStatus parseOk = abcp.Start();
                if (parseOk != ExitStatus.allGood)
                    BadABC(abcp.playerID, abcp.charIndex);
                else
                    ABCParsers.GetInstance().Add(abcp);
            }
            else
            {
                ABCParsers.GetInstance().Remove(abcp);
            }
            /*
            if (listenerID == -1)
            {
                listenerID = serverAPI.Event.RegisterGameTickListener(OnServerGameTick, 1); // arg1 is millisecond Interval
            }
            */
        }
        private void StopABC(IPlayer fromPlayer, ABCStopFromClient abcData)
        {
            // Kill the tickListener
            int clientID = fromPlayer.ClientId;
            ABCParser abcp = ABCParsers.GetInstance().FindByID(clientID);
            if (abcp != null)
            {
                ABCParsers.GetInstance().Remove(abcp);
                ABCStopFromServer packet = new ABCStopFromServer();
                packet.fromClientID = clientID;
                IServerNetworkChannel ch = serverAPI.Network.GetChannel("abc");
                ch.BroadcastPacket(packet);
                if (ABCParsers.GetInstance().Get().Count == 0)
                {
                    /*
                    serverAPI.Event.UnregisterGameTickListener(listenerID);
                    listenerID = -1;
                    */
                }
                MessageToClient(fromPlayer.ClientId, "Stopping abc playback!");
            }

            return;
        }
        private void OnServerGameTick(float dt)
        {
            int count = ABCParsers.GetInstance().Get().Count;
            //int index = 0;
            for (int i=0; i<count; i++)
            {
                ABCParser abcp = ABCParsers.GetInstance().Get()[i];
                ExitStatus parseStatus = abcp.Update(dt);
                //serverAPI.BroadcastMessageToAllGroups("Part " + index++ + " time " + abcp.currentTime, EnumChatType.Notification);
                if (parseStatus == ExitStatus.finished || parseStatus == ExitStatus.error)
                {
                    if (parseStatus == ExitStatus.finished)
                        MessageToClient(abcp.playerID, "abc playback finished!");
                    else
                        BadABC(abcp.playerID, abcp.charIndex);
                    // This is my attempt at gracefully removing something from a list                
                    //abcParsers.RemoveAt(i);
                    ABCParsers.GetInstance().Remove(abcp);
                    count--;
                    i--;
                }
            }
            return;
        }

        private void BadABC(int playerID, int charIndex)
        {
            MessageToClient(playerID, "Error parsing file, char index " + charIndex);
        }
        private void MessageToClient(int playerID, string message)
        {
            IPlayer player = Array.Find(serverAPI.World.AllOnlinePlayers, x => x.ClientId == playerID);
            if(player != null) // The player might have left!
                serverAPI.SendMessage(player, 0, message, EnumChatType.Notification);
        }
        #endregion
    }
    public class ABCParsers
    {
        private static ABCParsers _instance;
        List<ABCParser> list;
        private ABCParsers()
        {
            list = new List<ABCParser>();
        }
        public static ABCParsers GetInstance()
        {
            if (_instance != null)
                return _instance;
            return _instance = new ABCParsers();
        }
        public List<ABCParser> Get()
        {
            return list;
        }
        public void Add(ABCParser abcp)
        {
            list.Add(abcp);
        }

        public void Remove(ABCParser abcp)
        {
            list.Remove(abcp);
        }

        public ABCParser FindByID(int ID)
        {
            return list.Find(x => x.playerID == ID);
        }
    }
}