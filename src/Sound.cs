using ProtoBuf;
using System; // Random
using System.Collections.Generic;
using System.Diagnostics;  // Debug todo remove
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools; // vec3D

namespace instruments
{
    public class Sound
    {
        public ILoadedSound sound;
        public int ID;
        public float endTime;

        public Sound(IClientWorldAccessor client, Vec3d pos, float pitchModifier, string assetLocation, int id, float volume, bool play = true)
        {


            SoundParams soundData = new SoundParams(new AssetLocation("instruments", assetLocation + ".ogg"));
            soundData.Volume = volume;
            sound = client.LoadSound(soundData);
            if (sound != null)
            {
                UpdateSound(pos, pitchModifier);
                ID = id;
                if (play)
                    sound.Start();
            }
            else
                Debug.WriteLine("Could not make sound!");
        }
        public void StartSound()
        {
            sound.Start();
        }
        public void StopSound()
        {
            if (sound.IsPlaying)
                sound.Dispose();
        }
        public void UpdatePosition(Vec3d position)
        {
            sound.SetPosition((float)position.X, (float)position.Y, (float)position.Z);
        }
        public float UpdateSound(Vec3d position, float pitch)
        {
            sound.SetPitch(pitch);
            UpdatePosition(position);
            return 0;
        }
    }

    internal class SoundManager
    {
        // An object for each client that is playing something. There is one SoundManager for each 'sound source'.
        // Each client will have a list of these
        // The server passes sounds into this, and each client updates the notes in it, per tick or so
        public int sourceID;                // The ID of the player who is playing the sounds for this manager
        public Vec3d sourcePosition;       // The position of the player of this manager. Will need to update as the player moves.
        private List<Chord> chordBuffer;    // The list of chords received from the server. As the tune progresses, chords from this list will be played.
        private List<Sound> soundsOngoing;  // The list of existing Sounds that exist currently.
        private IClientWorldAccessor client;
        private float nowTime = 0;
        private bool active = true;         // The manager should do Update(). False when playback should stop.
        private string instrumentFileLocation;
        private float volume;
        public InstrumentType instrument;

        private Dictionary<int, string> drumMap = new Dictionary<int, string>();
        private Dictionary<int, string> octaveMap = new Dictionary<int, string>();

        private float[,] frequencies = new float[7, 3] {
            {0.9441f, 1.0000f, 1.0595f },  // A
            {1.0595f, 1.1223f, 1.1891f },  // B
            {1.1223f, 1.1891f, 1.2600f },  // C
            {1.2600f, 1.3350f, 1.4141f },  // D
            {1.4141f, 1.4964f, 1.5873f },  // E
            {1.4964f, 1.5873f, 1.6818f },  // F
            {1.6818f, 1.7818f, 1.8877f },  // G
        };

        const int drumSamples = 64;

        public SoundManager(IClientWorldAccessor clientAcc, int sID, string location, InstrumentType inst, float startTime)
        {
            client = clientAcc;

            int i = 0;
            octaveMap.Add(i++, "a0");
            octaveMap.Add(i++, "a1");
            octaveMap.Add(i++, "a2");
            octaveMap.Add(i++, "a3");
            octaveMap.Add(i++, "a4");
            octaveMap.Add(i++, "a5");
            octaveMap.Add(i++, "a6");
            octaveMap.Add(i++, "a7");

            // Drum
            // D1 lowest
            // E6 highest
            for (i = 0; i < drumSamples - 1; i++)
            {
                int index = i + 26;
                string s = index + "";
                drumMap.Add(i, s);
            }
            drumMap.Add(i, "mute");

            chordBuffer = new List<Chord>();
            soundsOngoing = new List<Sound>();

            sourceID = sID;
            sourcePosition = new Vec3d(0, 0, 0); // Not sure if necessary
            instrumentFileLocation = location;
            instrument = inst;
            nowTime = startTime;

            // Music Blocks have an ID above 1000, to not clash with players. This is convinient, as I can use this to set the volume appropriately.
            volume = sID >= 1000 ? InstrumentModCommon.config.blockVolume : InstrumentModCommon.config.playerVolume;
        }

        public void AddChord(Vec3d pos, Chord chord)
        {
            sourcePosition = pos;   // Update the source position for the entire buffer, so that they are played at the most up to date position.
            chordBuffer.Add(chord);
        }

        public bool Update(float dt)
        {
            if (!active)
                return false;

            // Check if the sounds have a player, so we can update their positions
            bool playerExists = false;
            {
                IPlayer player = Array.Find(client.AllOnlinePlayers, x => x.ClientId == sourceID);
                if (player != null && player.Entity != null)  // 1. No player if it's a block 2. Players don't have entities if too far from client
                {
                    sourcePosition = new Vec3d(player.Entity.Pos.X, player.Entity.Pos.Y, player.Entity.Pos.Z);
                    playerExists = true;
                }
            }

            // Check if a chord in the buffer should play.
            nowTime += (dt * 1000);
            int chordCount = chordBuffer.Count;
            if (chordCount == 0)
                ;
            else
                for (int i = 0; i < chordCount; i++)
                {
                    if (chordBuffer[i].CheckShouldStart(nowTime))
                    {
                        foreach (Note note in chordBuffer[i].notes)
                        {
                            bool play = true;
                            string assetLocation;
                            float pitch;
                            if (instrument == InstrumentType.drum)
                            {
                                int index = KeyToIndex(note);

                                if (index < 0)
                                {
                                    play = false;
                                    index = drumSamples - 1;
                                }
                                assetLocation = instrumentFileLocation + "/" + drumMap[index];
                                pitch = 1;
                                note.duration = Math.Max(note.duration, 1000);
                            }
                            else
                            {
                                pitch = KeyToPitch(note.key, note.accidental);
                                if (pitch < 0)
                                {
                                    pitch = 1;  // Too scared to pass in <0 to Sound()
                                    play = false;
                                }
                                assetLocation = instrumentFileLocation + "/" + octaveMap[note.octave];
                                if (instrument == InstrumentType.mic)
                                {
                                    Random rnd = new Random();
                                    int rNum = rnd.Next(0, 5); // A number between 0 and 4
                                    switch (rNum)
                                    {
                                        case 0:
                                            assetLocation += "ba";
                                            break;
                                        case 1:
                                            assetLocation += "bo";
                                            break;
                                        case 2:
                                            assetLocation += "da";
                                            break;
                                        case 3:
                                            assetLocation += "do";
                                            break;
                                        case 4:
                                            assetLocation += "la";
                                            break;
                                    }
                                }
                            }
                            Sound newSound = new Sound(client, sourcePosition, pitch, assetLocation, -1, volume, play);
                            newSound.endTime = nowTime + note.duration;
                            if (newSound.sound == null)
                                Debug.WriteLine("Sound creation failed (abc)!");
                            else
                                soundsOngoing.Add(newSound);
                        }
                        chordBuffer.RemoveAt(i);
                        chordCount--;
                        i--;
                    }
                }

            // Now, check if the currently playing sounds need to stop.
            int soundCount = soundsOngoing.Count;
            if (soundCount == 0)
            {
                // If the soundsOngoing is empty, either our buffer ran dry prematurely or the song ended.
                // Lets hope the former never happens.
                active = false;
                return false;
            }
            else
                for (int i = 0; i < soundCount; i++)
                {
                    if (soundsOngoing[i].endTime < nowTime)
                    {
                        soundsOngoing[i].StopSound();
                        soundsOngoing.RemoveAt(i);
                        soundCount--;
                        i--;
                    }
                    else
                    {
                        // Find the source owner, and update the position. Might be too intensive, test
                        if (playerExists)
                            soundsOngoing[i].UpdatePosition(sourcePosition);
                    }
                }
            return true;
        }

        public void Kill()
        {
            // The server has sent a command to kill this manager, and stop all sounds in the buffer from playing.
            chordBuffer.Clear();
            foreach (Sound s in soundsOngoing)
                s.StopSound();
            soundsOngoing.Clear();
        }
        private float KeyToPitch(char key, Accidental acc)
        {
            float pitchMod;
            int noteIndex;
            int accMod = 1;

            if (acc == Accidental.sharp || acc == Accidental.accSharp)
                accMod = 2;
            if (acc == Accidental.flat || acc == Accidental.accFlat)
                accMod = 0;

            switch (key)
            {
                case 'a':
                case 'b':
                case 'c':
                case 'd':
                case 'e':
                case 'f':
                case 'g':
                    noteIndex = key - 'a';
                    break;
                case 'A':
                case 'B':
                case 'C':
                case 'D':
                case 'E':
                case 'F':
                case 'G':
                    noteIndex = key - 'A';
                    break;
                default:
                    // z means rest.
                    return -1;
            }

            pitchMod = frequencies[noteIndex, accMod];

            return pitchMod;
        }
        private int KeyToIndex(Note note)
        {
            int index = -5; // The first 5 notes aren't available (samples start at D1, not A0), so offset by that much
            index += 12 * note.octave; // 12 notes per octave
            switch (note.key)
            {
                case 'a':
                    index += 0;
                    break;
                case 'b':
                    index += 2;
                    break;
                case 'c':
                    index += 3;
                    break;
                case 'd':
                    index += 5;
                    break;
                case 'e':
                    index += 7;
                    break;
                case 'f':
                    index += 8;
                    break;
                case 'g':
                    index += 10;
                    break;
                case 'z':
                    index = -1;
                    break;
            }
            if (note.accidental == Accidental.accSharp || note.accidental == Accidental.sharp)
                index++;
            if (note.accidental == Accidental.accFlat || note.accidental == Accidental.flat)
                index--;

            if (index < 0 || index >= drumSamples)
                index = -1;

            return index;
        }

    }
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class Chord
    {
        public List<Note> notes;
        public float duration; // The duration of a chord is the same as the duration of the shortest note (including rests)
        public float startTime;

        public Chord()
        {
            startTime = 0;
            duration = 65535;
            notes = new List<Note>();
        }

        public void AddNote(Note newNote, float minimumDuration)
        {
            if (newNote.duration < duration)
                duration = newNote.duration;

            /*
            if (newNote.duration < minimumDuration)
                newNote.duration = minimumDuration;
            */
            notes.Add(newNote);
        }

        public bool CheckShouldStart(float currentTime)
        {
            if (currentTime >= startTime)
                return true;
            return false;
        }

        public bool CheckShouldStop(float currentTime)
        {
            if (currentTime > (startTime + duration))
                return true;
            return false;
        }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class Note
    {
        public char key;
        public int octave;
        public float duration;
        public Accidental accidental;
    }
}
