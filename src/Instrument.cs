using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools; // vec3D

using System.IO; // Open files
using System.Diagnostics; // debug todo remove

namespace instruments
{
    public enum InstrumentType
    {
        trumpet = 0,
        sax,
        dulcimer,
        accordian,
        bass,
        violin,
        clarinet,
        flute,
        bagpipes,
        steelDrum,
        acousticGuitar,
        grandPiano,
        musicBox,
        harp,
        mic,
        drum
    }
    public class InstrumentItem : Item
    {
        const float PI = 3.14159f;
        private NoteFrequency currentNote;
        private ICoreClientAPI capi;
        bool holding = false;
        bool abcPlaying = false;
        public InstrumentType instrument;

        // fixme GUIs need to be client only
        //NoteGUI guiDialog;

        public override void OnLoaded(ICoreAPI api)
        {
            if (api.Side != EnumAppSide.Client)
                return;

            Startup();
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (!firstEvent)
                return;


            bool isClient;
            var client = GetClient(byEntity, out isClient);
            if (isClient)
            {
                if (byEntity.Controls.Sneak)
                {
                    ToggleGui();
                }
                else
                {
                    if (Definitions.GetInstance().GetPlayMode() != PlayMode.abc)
                    {
                        Vec3d pos = new Vec3d(byEntity.Pos.X, byEntity.Pos.Y, byEntity.Pos.Z);
                        NoteStart newNote = new NoteStart();
                        newNote.pitch = currentNote.pitch;
                        newNote.positon = pos;
                        newNote.instrument = instrument;
                        IClientNetworkChannel ch = capi.Network.GetChannel("noteTest");
                        ch.SendPacket(newNote);
                    }
                    else
                    {
                        if (abcPlaying)
                        {
                            ABCSendStop();
                        }
                        else
                        {
                            ABCSongSelect();
                        }
                    }
                }
            }
            handling = EnumHandHandling.PreventDefault;
        }
        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            bool isClient;
            var client = GetClient(byEntity, out isClient);

            if (isClient)
            {            
                Update(byEntity);
                // Additionally, update the sound packet
                if (Definitions.GetInstance().GetPlayMode() != PlayMode.abc)
                {
                    Vec3d pos = new Vec3d(byEntity.Pos.X, byEntity.Pos.Y, byEntity.Pos.Z);
                    NoteUpdate newNote = new NoteUpdate();
                    newNote.pitch = currentNote.pitch;
                    newNote.positon = pos;
                    IClientNetworkChannel ch = capi.Network.GetChannel("noteTest");
                    ch.SendPacket(newNote);
                }
            }

            return true;
        }
        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            bool isClient;
            var client = GetClient(byEntity, out isClient);

            if (isClient)
            {
                if (Definitions.GetInstance().GetPlayMode() != PlayMode.abc)
                {
                    NoteStop newNote = new NoteStop();
                    IClientNetworkChannel ch = capi.Network.GetChannel("noteTest");
                    ch.SendPacket(newNote);
                }
            }
            return true;
        }

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
            // While doing nothing, and while playing, get the angle and show the note to play on HUD.
            if (byEntity.World is IClientWorldAccessor)
            {
                Update(byEntity);
            }
        }

        public int SetMode(PlayMode newMode)
        {
            //fixme as this is called from a gui, the server does not see this. Need to send a packet to tell the server this. Or, does server even need to run this code?
            Definitions.GetInstance().SetPlayMode(newMode);
            return 1;
        }
        public void SetBand(string bn)
        {
            Definitions.GetInstance().SetBandName(bn);
        }

        public int PlaySong(string filePath)
        {
            //if(index < abcFiles.Count)
            {
                string abcData = "";
                bool abcOK = RecursiveFileProcessor.ReadFile(Definitions.GetInstance().ABCBasePath() + Path.DirectorySeparatorChar + filePath, ref abcData); // Todo don't send the whole thing
                if (abcOK)
                {
                    ABCSendStart(abcData, false);
                }
                else
                {
                    // Either the file was deleted since opening the GUI, something weird happened, or the file exists on the server.
                    // Whatever happened, let the server worry about it
                    ABCSendStart(filePath, true);
                }
            }
            return 1;
        }
        public int PlaySong(int index)
        {
            string filePath = Definitions.GetInstance().GetSongList()[index];
            string abcData = "";
            bool abcOK = RecursiveFileProcessor.ReadFile(Definitions.GetInstance().ABCBasePath() + Path.DirectorySeparatorChar + filePath, ref abcData); // Todo don't send the whole thing
            if (abcOK)
            {
                ABCSendStart(abcData, false);
            }
            else
            {
                // Either the file was deleted since opening the GUI, something weird happened, or the file exists on the server.
                // Whatever happened, let the server worry about it
                ABCSendStart(filePath, true);
            }
            return 1;
        }
        private void Update(EntityAgent byEntity)
        {
            if(!holding)
            {
                holding = true;
                capi.Event.AfterActiveSlotChanged += ChangeFromInstrument; // Todo what happens if the player drops the instrument?
            }
            switch (Definitions.GetInstance().GetPlayMode())
            {
                case PlayMode.lockedTone:
                    AngleToPitchLockedTone(byEntity.Pos.Pitch);
                    break;
                case PlayMode.lockedSemiTone:
                    AngleToPitchLockedSemiTone(byEntity.Pos.Pitch);
                    break;
                case PlayMode.fluid:
                    AngleToPitch(byEntity.Pos.Pitch);
                    break;
                case PlayMode.abc:
                    // No logic for this mode; all handled by the server.
                    break;
                default:
                    break;
            }


                //UpdateGUI(byEntity);
        }
        /*private void UpdateGUI(EntityAgent byEntity)
        {
            if (guiDialog == null)
            {
                Debug.WriteLine("NO GUI!");
            }
            else
            {
                if (guiDialog.IsOpened())
                    guiDialog.UpdateText(currentNote.ID);
                else
                    guiDialog.TryOpen();
            }
        }*/
        private void AngleToPitch(float angle)
        {
            // entity.Pos.Pitch goes from 90 to 270
            // yMin = 4.697, yMax = 1.5858
            // In bottom half of screen, pitch needs to go up from 1 to 2
            // In top half, it needs to go from 2 to 4
            // To get a y between 1 and 3, use y = 4 - 0.6364x
            // In English, 4 - 2/PI

            const float halfwayPoint = 3.1112f;
            // Unfortunately, pitch seems to only work up to * 3, so we can't go up to 4 :'(
            // Instead, shift down 1 octave in the lower half, and up 1 in the upper
            float pitch;
            if (angle > halfwayPoint) // bottom half, remember it's inverted!
                pitch = (2 - angle * (1 / PI));
            else
                pitch = (3 - angle * (2 / PI));

            currentNote.pitch = pitch;
            currentNote.ID = "Fluid";
        }
        private void AngleToPitchLockedSemiTone(float angle)
        {
            // Instead of translating an angle straight into a pitch, go up in steps per semi-tone.
            // Middle note: A3, 220Hz
            // Bottom note: A2, 110Hz
            // Top note:    A4, 440Hz
            // TODO: For each semi-tone step is non-linear. Probably best to hard-code it instead of trying to be smart.
            // Each angle increment IS linear - for each angle (floored), return a unique pitch
            // Ok, we don't return the actual pitch, but the pitch multiplier offset, as before

            const float step = 0.13215f;  // 1.5858 / 12
            float currentStep = 1.55858f;
            for (int i = 24; i >= 0; i--)
            {
                // For each step, check if the angle is in the step's range
                // Yeah I know it's really shit, but idgaf
                // TODO if I can be bothered, floor the pitch and map directly to dict instead of using i
                if (angle < currentStep + step)
                {
                    currentNote = Definitions.GetInstance().GetFrequency(i);
                    break;
                }
                currentStep += step;
            }
        }
        private void AngleToPitchLockedTone(float angle)
        {
            // Like the above function, but if the current i of the noteMap is a sharp, skip it
            // Also, the step is /8 instead of /12, as steps are shorter innit
            // TODO add keys. Set the key somehow, maybe have a map per key?

            const float step = 0.198225f;  // 1.5858 / 8
            float currentStep = 1.55858f;
            for (int i = 24; i >= 0; i--)
            {
                NoteFrequency nf = currentNote = Definitions.GetInstance().GetFrequency(i);
                if (nf.ID.IndexOf("^") > 0)
                {
                    continue;
                }
                if (angle < currentStep + step)
                {
                    currentNote = nf;
                    break;
                }
                currentStep += step;
            }
        }

        private void Startup()
        {
            // Initialise gui stuff. Has to be done here as OnLoaded has a different API
            capi = api as ICoreClientAPI;
            //guiDialog = new NoteGUI(capi);
        }
        private bool ToggleGui()
        {
            Action<string> sb = SetBand;
            ModeSelectGUI modeDialog = new ModeSelectGUI(capi, SetMode, sb, Definitions.GetInstance().GetBandName());
            modeDialog.TryOpen();

            return true;
        }

        private IClientWorldAccessor GetClient(EntityAgent entity, out bool isClient)
        {
            isClient = entity.World.Side == EnumAppSide.Client;
            return isClient ? entity.World as IClientWorldAccessor : null;
        }
        private void ChangeFromInstrument(ActiveSlotChangeEventArgs args)
        {
            capi.Event.AfterActiveSlotChanged -= ChangeFromInstrument;
            holding = false;
            //guiDialog?.TryClose();
            if(abcPlaying)
            {
                abcPlaying = false;
                ABCSendStop();
            }
        }
        private void ABCSendStart(string fileData, bool isServerOwned)
        {
            ABCStartFromClient newABC = new ABCStartFromClient();
            newABC.abcData = fileData;
            newABC.instrument = instrument;
            newABC.bandName = Definitions.GetInstance().GetBandName();
            newABC.isServerFile = isServerOwned;
            IClientNetworkChannel ch = capi.Network.GetChannel("abc");
            ch.SendPacket(newABC);
            abcPlaying = true;
        }
        private void ABCSendStop()
        {
            ABCStopFromClient newABC = new ABCStopFromClient();
            IClientNetworkChannel ch = capi.Network.GetChannel("abc");
            ch.SendPacket(newABC);
            abcPlaying = false;
        }

        private void ABCSongSelect()
        {
            // Load abc folder
            if (RecursiveFileProcessor.DirectoryExists(Definitions.GetInstance().ABCBasePath()))
            {
                if(Definitions.GetInstance().UpdateSongList(Definitions.GetInstance().ABCBasePath()))
                {
                    SongSelectGUI songGui = new SongSelectGUI(capi, PlaySong, Definitions.GetInstance().GetSongList());
                    songGui.TryOpen();
                }
                else
                { 
                    capi.ShowChatMessage("ABC error: No ABC files found within \"" + Definitions.GetInstance().ABCBasePath() + "\"");
                }
            }
            else
            {
                // ABC folder not found, log a message to tell the player
                capi.ShowChatMessage("ABC error: Could not find folder at \""+ Definitions.GetInstance().ABCBasePath() + "\"");
            }
        }
    }


    public class TrumpetItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.trumpet;
            base.OnLoaded(api);
        }
    }
    public class ClarinetItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.clarinet;
            base.OnLoaded(api);
        }
    }
    public class AccordionItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.accordian;
            base.OnLoaded(api);
        }
    }
    public class SaxItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.sax;
            base.OnLoaded(api);
        }
    }
    public class ViolinItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.violin;
            base.OnLoaded(api);
        }
    }
    public class DulcimerItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.dulcimer;
            base.OnLoaded(api);
        }
    }
    public class SteelDrumItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.steelDrum;
            base.OnLoaded(api);
        }
    }
    public class AcousticGuitarItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.acousticGuitar;
            base.OnLoaded(api);
        }
    }
    public class GrandPianoItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.grandPiano;
            base.OnLoaded(api);
        }
    }
    public class MusicBoxItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.musicBox;
            base.OnLoaded(api);
        }
    }
    public class HarpItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.harp;
            base.OnLoaded(api);
        }
    }
    public class MicItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.mic;
            base.OnLoaded(api);
        }
    }
    public class DrumItem : InstrumentItem
    {
        public override void OnLoaded(ICoreAPI api)
        {
            instrument = InstrumentType.drum;
            base.OnLoaded(api);
        }
    }

    public class Definitions
    {
        private string bandName = "";
        private PlayMode mode = PlayMode.abc;
        private static Definitions _instance;
        private Dictionary<int, NoteFrequency> noteMap = new Dictionary<int, NoteFrequency>();
        private const int bufferSize = 32;
        private List<string> abcFiles = new List<string>();
        private List<string> serverAbcFiles = new List<string>();

        string abcBaseDirectory = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "abc";

        private Definitions()
        {
            // Populate the dict
            int i = 0;
            noteMap.Add(i++, new NoteFrequency("a3", 0.5000f));
            noteMap.Add(i++, new NoteFrequency("a^3", 0.5295f));
            noteMap.Add(i++, new NoteFrequency("b3", 0.5614f));
            noteMap.Add(i++, new NoteFrequency("c3", 0.5945f));
            noteMap.Add(i++, new NoteFrequency("c^3", 0.6300f));
            noteMap.Add(i++, new NoteFrequency("d3", 0.6672f));
            noteMap.Add(i++, new NoteFrequency("d^3", 0.7073f));
            noteMap.Add(i++, new NoteFrequency("e3", 0.7491f));
            noteMap.Add(i++, new NoteFrequency("f3", 0.7936f));
            noteMap.Add(i++, new NoteFrequency("f^3", 0.8409f));
            noteMap.Add(i++, new NoteFrequency("g3", 0.8909f));
            noteMap.Add(i++, new NoteFrequency("g^3", 0.9441f));
            noteMap.Add(i++, new NoteFrequency("a4", 1.0000f));
            noteMap.Add(i++, new NoteFrequency("a^4", 1.0595f));
            noteMap.Add(i++, new NoteFrequency("b4", 1.1223f));
            noteMap.Add(i++, new NoteFrequency("c3", 1.1891f));
            noteMap.Add(i++, new NoteFrequency("c^4", 1.2600f));
            noteMap.Add(i++, new NoteFrequency("d4", 1.335f));
            noteMap.Add(i++, new NoteFrequency("d^4", 1.4141f));
            noteMap.Add(i++, new NoteFrequency("e4", 1.4964f));
            noteMap.Add(i++, new NoteFrequency("f4", 1.5873f));
            noteMap.Add(i++, new NoteFrequency("f^4", 1.6818f));
            noteMap.Add(i++, new NoteFrequency("g4", 1.7818f));
            noteMap.Add(i++, new NoteFrequency("g^4", 1.8877f));
            noteMap.Add(i++, new NoteFrequency("a5", 2.0000f));
        }
        public static Definitions GetInstance()
        {
            if (_instance != null)
                return _instance;
            return _instance = new Definitions();
        }

        public void SetBandName(string bn)
        {
            bandName = bn;
        }
        public string GetBandName()
        {
            return bandName;
        }
        public void SetPlayMode(PlayMode newMode)
        {
            mode = newMode;
        }
        public PlayMode GetPlayMode()
        {
            return mode;
        }
        public NoteFrequency GetFrequency(int index)
        {
            return noteMap[index];
        }
        public int GetBufferSize()
        {
            return bufferSize;
        }
        public List<string> GetSongList()
        {
            return abcFiles;
        }
        public bool UpdateSongList(string abcBaseDirectory)
        {
            // The abc folder was found! Now find all the files in it.
            abcFiles.Clear();
            RecursiveFileProcessor.ProcessDirectory(abcBaseDirectory, abcBaseDirectory + Path.DirectorySeparatorChar, ref abcFiles);
            //Debug.WriteLine("Finished search for ABC files");
            foreach (string song in serverAbcFiles)
                abcFiles.Add(song);

            if (abcFiles.Count == 0)
            {
                return false;
            }
            else
            {
                return true;
            }
        }
        public void AddToServerSongList(string songFileName)
        {
            serverAbcFiles.Add(songFileName);
        }
        public string ABCBasePath()
        {
            return abcBaseDirectory;
        }
    }
}
