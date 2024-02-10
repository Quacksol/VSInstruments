﻿using ProtoBuf;
using System;               // Array.Find()
using System.Collections.Generic; // List
using System.Diagnostics;  // Debug todo remove
using System.IO; // Open files
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config; // GlobalConstants
using Vintagestory.API.MathTools; // vec3D
using Vintagestory.API.Server;

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
        public static InstrumentSettings config;

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

            api.RegisterBlockClass("musicblock", typeof(MusicBlock));
            api.RegisterBlockEntityClass("musicblockentity", typeof(BEMusicBlock));

            // Load settings file
            try
            {
                config = api.LoadModConfig<InstrumentSettings>("instruments.json");
                if (config == null)
                {
                    config = new InstrumentSettings();
                    api.StoreModConfig(config, "instruments.json");
                }
            }
            catch (System.Exception)
            {
                api.Logger.Error("Could not load instruments config, using default values...");
                config = new InstrumentSettings();
            }
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
        bool clientSideEnable;
        bool clientSideReady = false;

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

            // Go through the list of all instruments (in Instrument.cs) and add a sound file location for each entry.
            // Make sure the folder name is exactly the same as in the enum!
            for (InstrumentType i = 0; i < InstrumentType.none; i++)
            {
                string s = "sounds/"+i.ToString();
                soundLocations.Add(i, s);
            }

            thisClientPlaying = false;
            MusicBlockManager.GetInstance().Reset(); // I think there's a manager for both Server and Client, so reset it I guess
            Definitions.GetInstance().Reset();

            clientApi.RegisterCommand("instruments", "instrument playback commands", "[enable|disable]", ParseClientCommand);
            clientSideEnable = true;
            clientSideReady = true;
        }

        public override void Dispose()
        {
            // We MIGHT need this when resetting worlds without restarting the game
            base.Dispose();
            if (listenerIDClient != -1)
            {
                clientApi.Event.UnregisterGameTickListener(listenerIDClient);
                listenerIDClient = 0;
            }
            //soundManagers.Clear(); //Already null!
            soundList.Clear();
            clientSideReady = false;
        }
        private void MakeNote(NoteStart note)
        {
            if (!clientSideReady) return;

            string noteString = "/a3";
            if (note.instrument == InstrumentType.drum)
            {
                float div = note.pitch * 2 - 1;
                const float step = 0.046875f;  // 3/64
                float currentStep = 0f;
                for (int i = 0; i <= 64; i++)
                {
                    if (div < currentStep + step)
                    {
                        noteString = "/" + (i + 24);
                        break;
                    }
                    currentStep += step;
                }
                note.pitch = 1; // Reset the pitch, we don't want any pitch bend for drum
            }
            else if (note.instrument == InstrumentType.mic)
            {
                Random rnd = new Random();
                int rNum = rnd.Next(0, 5); // A number between 0 and 4
                switch (rNum)
                {
                    case 0:
                        noteString += "ba";
                        break;
                    case 1:
                        noteString += "bo";
                        break;
                    case 2:
                        noteString += "da";
                        break;
                    case 3:
                        noteString += "do";
                        break;
                    case 4:
                        noteString += "la";
                        break;
                }
            }
            IClientWorldAccessor clientWorldAccessor = clientApi.World;
            Sound sound = new Sound(clientWorldAccessor, note.positon, note.pitch, soundLocations[note.instrument] + noteString, note.ID, config.playerVolume);
            if (sound.sound == null)
                Debug.WriteLine("Sound creation failed!");
            else
                soundList.Add(sound);
        }
        private void UpdateNote(NoteUpdate note)
        {
            if (!clientSideReady) return;
            //Sound sound = soundList.Find(x => x.ID.Contains(note.ID));
            Sound sound = soundList.Find(x => (x.ID == note.ID));
            if (sound == null)
                return;
            sound.UpdateSound(note.positon, note.pitch);
        }
        private void StopNote(NoteStop note)
        {
            if (!clientSideReady) return;
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

            if (!clientSideEnable)
                return;

            if (!clientSideReady) return;

            SoundManager sm = soundManagers.Find(x => (x.sourceID == serverPacket.fromClientID));
            if (sm == null)
            {
                // This was the first packet from the server with data from this client. Need to register a new SoundManager.
                float startTime = serverPacket.newChord.startTime;
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
            // Set the animation
            IPlayer otherPlayer = Array.Find(clientApi.World.AllOnlinePlayers, x => x.ClientId == sm.sourceID);
            if (otherPlayer != null && otherPlayer.Entity != null)  // Either a musicBlock, the player is no longer connected, or the player is out of render range. Or something weird
                otherPlayer.Entity.StartAnimation(Definitions.GetInstance().GetAnimation(serverPacket.instrument));
            sm.AddChord(serverPacket.positon, serverPacket.newChord);
        }
        private void StopSounds(ABCStopFromServer serverPacket)
        {
            if (!clientSideReady) return;
            IClientPlayer player = clientApi.World.Player; // If the client is still starting up, this will be null!
            if (player == null)
                return;

            SoundManager sm = soundManagers.Find(x => (x.sourceID == serverPacket.fromClientID));
            if (sm != null)
            {
                if (sm.sourceID == player.ClientId)
                {
                    thisClientPlaying = false;
                    Definitions.GetInstance().SetIsPlaying(false);
                }
                IPlayer otherPlayer = Array.Find(clientApi.World.AllOnlinePlayers, x => x.ClientId == sm.sourceID);
                if (otherPlayer != null && otherPlayer.Entity != null)  // Either a musicBlock, the player is no longer connected, or the player is out of render range. Or something weird
                    otherPlayer.Entity.StopAnimation(Definitions.GetInstance().GetAnimation(sm.instrument));
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

        private void ParseClientCommand(int groupId, CmdArgs args)
        {
            string command = args.PopWord();
            switch (command)
            {
                case "enable":
                    clientSideEnable = true;
                    clientApi.ShowChatMessage("ABC playback enabled!");
                    break;
                case "disable":
                    clientSideEnable = false;
                    clientApi.ShowChatMessage("ABC playback disabled!");
                    {
                        ABCStopFromServer dummy = new ABCStopFromServer();
                        dummy.fromClientID = clientApi.World.Player.ClientId;
                        StopSounds(dummy);
                    }
                    break;
                default:
                    clientApi.ShowChatMessage("Syntax: .instruments [enable|disable]");
                    break;
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

        private struct PlaybackData
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
            ABCParsers.GetInstance().SetAPI(serverAPI);

            abcBaseDir = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "abc_server";
            serverAPI.Event.PlayerJoin += SendSongs;
        }
        public override void Dispose()
        {
            // We MIGHT need this when resetting worlds without restarting the game
            base.Dispose();
            if (listenerID != -1)
            {
                serverAPI.Event.UnregisterGameTickListener(listenerID);
                listenerID = 0;
            }
            ABCParsers.GetInstance().Reset();
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
            foreach (string song in abcFiles)
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
            ABCParser abcp = ABCParsers.GetInstance().FindByID(fromPlayer.ClientId);
            if (abcp == null)
            {
                string abcSong = "";
                if (abcData.isServerFile)
                {
                    // The contained string is NOT a full song, but a link to it on the server.
                    // Find this file, load it, and make the abcParser in the same way
                    RecursiveFileProcessor.ReadFile(abcBaseDir + Path.DirectorySeparatorChar + abcData.abcData, ref abcSong);
                }
                else
                {
                    abcSong = abcData.abcData;
                }

                ABCParsers.GetInstance().MakeNewParser(serverAPI, fromPlayer, abcSong, abcData.bandName, abcData.instrument);
            }
            else
            {
                ABCParsers.GetInstance().Remove(serverAPI, fromPlayer, abcp);
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
            int clientID = fromPlayer.ClientId;
            ABCParser abcp = ABCParsers.GetInstance().FindByID(clientID);
            if (abcp != null)
            {
                ABCParsers.GetInstance().Remove(serverAPI, fromPlayer, abcp);
                ABCStopFromServer packet = new ABCStopFromServer();
                packet.fromClientID = clientID;
                IServerNetworkChannel ch = serverAPI.Network.GetChannel("abc");
                ch.BroadcastPacket(packet);
            }

            return;
        }
        private void OnServerGameTick(float dt)
        {
            ABCParsers.GetInstance().Update(serverAPI, dt);
        }
        #endregion
    }
}