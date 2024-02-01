using System;               // Array.Find()
using System.Collections.Generic; // List
using System.Diagnostics;  // Debug todo remove
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools; // vec3D
using Vintagestory.API.Server;

/*
 * This file is for the server only.
 * It contains all the necessaries for playing an abc file, for a single player.
 * When a band plays, multiple parsers will exist, each with the unique player ID.
 */

namespace instruments
{
    public enum Accidental
    {
        natural = 0,
        sharp,
        flat,
        accNatural,
        accSharp,
        accFlat
    }
    public enum ExitStatus
    {
        allGood = 0,
        finished,
        error
    }
    public class ABCParser
    {
        // Key signatures. Starting from A. 1 means sharp, 2 means flat
        // Fixme it's possible that a sharp should modify the next key's flat, etc. habenara has some consecutive notes which should be up-down-up........
        int[] C = { 0, 0, 0, 0, 0, 0, 0 };      // None

        int[] G = { 0, 0, 0, 0, 0, 1, 0 };      // Fs
        int[] D = { 0, 0, 1, 0, 0, 1, 0 };      // Fs, Cs
        int[] A = { 0, 0, 1, 0, 0, 1, 1 };      // Fs, Cs, Gs
        int[] E = { 0, 0, 1, 1, 0, 1, 1 };      // Fs, Cs, Gs, Ds
        int[] B = { 1, 0, 1, 1, 0, 1, 1 };      // Fs, Cs, Gs, Ds, As
        int[] Fs = { 1, 0, 1, 1, 1, 1, 1 };     // Fs, Cs, Gs, Ds, As, Es
        int[] Cs = { 1, 1, 1, 1, 1, 1, 1 };     // Fs, Cs, Gs, Ds, As, Es, Bs

        int[] F = { 0, 2, 0, 0, 0, 0, 0 };      // Bf
        int[] Bf = { 0, 2, 0, 0, 2, 0, 0 };     // Bf, Ef
        int[] Ef = { 2, 2, 0, 0, 2, 0, 0 };     // Bf, Ef, Af
        int[] Af = { 2, 2, 0, 2, 2, 0, 0 };     // Bf, Ef, Af, Df
        int[] Df = { 2, 2, 0, 2, 2, 0, 2 };     // Bf, Ef, Af, Df, Gf
        int[] Gf = { 2, 2, 2, 2, 2, 0, 2 };     // Bf, Ef, Af, Df, Gf, Cf
        int[] Cf = { 2, 2, 2, 2, 2, 2, 2 };     // Bf, Ef, Af, Df, Gf, Cf, Ff

        int[] currentKeySig;

        private Chord nextChord;
        private List<Chord> chordBuffer;
        private string file;
        public int charIndex;
        public float currentTime;

        private Accidental[,] accidentals; // 8 octaves of 7 keys

        private bool inChord;
        bool LFound;                // L is default note length. It's important to know if it was defined, or the default length was used
        bool QFound;                // Q is tempo. If one isn't defined, we need to do some special biz
        float meterValue;
        float defaultNoteLength;    // The default note is a crotchet, minim, etc
        float meterDNL;             // Default note length, as determined by meter (used if no L is given)
        float beatsPerMinute;
        float bpmFactor;            // Tempo normally comes in x/y=z eg 1/4=120. bpmFactor is the x/y
        float defaultNoteDuration;  // Actual time in millis a note lasts, by default. Used as baseline for all note durations.
        bool tuplet;                // Is there a tuplet?
        int tupletDuration;         // Value of ( duration modifier
        //float barDuration;          // How long a bar is in millis
        float barTime;              // How far through the bar we are, used to reset accidentals
        float chordStartTime;
        bool endOfFile;
        int repeatStartIndex;       // Index in file of where a repeat should start
        List<int> doneRepeats;      // List of repeats that have already been processed, and should be ignored
        bool hornpipe = false;

        public int playerID;
        public string playerName; // May be the name of the block, not only players!
        public int playerOwnerID;   // The ID of the player that created the parser, not the same as playerID for blocks
        public bool isPlayer;
        public Vec3d position;
        public string bandName;
        private ICoreServerAPI serverAPI;
        private InstrumentType instrument;
        private bool startSync;

        public ABCParser(ICoreServerAPI sAPI, int pID, string name, string f, InstrumentType inst, string bn, float masterTime)
        {
            // Make an ABC parser for a player. The player's position will be used to move the sound around.
            // For now, read the entire file and make all the chord objects at once
            serverAPI = sAPI;
            playerID = pID;
            playerOwnerID = pID;
            playerName = name;
            file = f;
            instrument = inst;
            bandName = bn;
            currentTime = masterTime;
            isPlayer = true;
            Reset();
        }
        public ABCParser(ICoreServerAPI sAPI, int owningPlayerID, int bID, Vec3d pos, string name, string f, InstrumentType inst, string bn, float masterTime)
        {
            // Make an ABC parser for a block. The position is not updated
            // For now, read the entire file and make all the chord objects at once
            serverAPI = sAPI;
            playerID = bID;
            playerOwnerID = owningPlayerID;
            playerName = name;
            file = f;
            instrument = inst;
            bandName = bn;
            currentTime = masterTime;
            isPlayer = false;
            position = pos;
            Reset();
        }

        public ExitStatus Start()
        {
            ExitStatus parseOk = ParseFile() ? ExitStatus.allGood : ExitStatus.error;
            return parseOk;
        }
        public ExitStatus Update(float dt)
        {
            // Call update function on each chord.
            // Each chord updates each note.
            // If a note's duration is over, deactivate it
            // If that note was the first note of the chord to deactivate, get the next chord
            ExitStatus allOk = ExitStatus.allGood;
            currentTime += (dt * 1000);

            if (chordBuffer.Count == 0)
                return ExitStatus.finished;

            // Check the first chord in the buffer. It will be the first to finish.
            while (chordBuffer[0].CheckShouldStop(currentTime))
            {
                chordBuffer.RemoveAt(0);
                allOk = ParseFile() ? ExitStatus.allGood : ExitStatus.error; // Get some new chords
                if (chordBuffer.Count == 0)
                    return ExitStatus.finished;
            }
            return allOk;
        }
        private bool ParseFile()
        {
            int timeout = 32;
            while (chordBuffer.Count < Definitions.GetInstance().GetBufferSize() && timeout > 0)
            {
                timeout--;
                if (charIndex > file.Length || endOfFile)
                    break;
                switch (file[charIndex])
                {
                    // Ignore all unsupported header lines
                    case 'H':
                    case 'I':
                    case 'J':
                    case 'N':
                    case 'O':
                    case 'P':
                    case 'R':
                    case 'S':
                    case 'T':
                    case 'U':
                    case 'V':
                    case 'W':
                    case 'X':
                    case 'Y':
                    case 'Z':
                    case '%':
                    case '\n':
                    case '\r':
                        List<char> checkList = new List<char> { '\n', '\r' };
                        SkipCharsUntil(file, ref charIndex, checkList); // Skip until newline
                        charIndex++;
                        timeout = 32;
                        break;

                    // Skip these characters
                    case '\t':  // Tab key
                    case '-':
                    case '/': // Shouldn't appear on its own. Probably 'syntactic grouping' crap that doesn't actually do anything
                    case '\\':
                    case ')': //  If you get a closing bracket, you probably had an opening one for a slur. Seeing as we ignore them, ignore this too 
                    case 'v': // A decoration
                    case 'u': // A decoration
                    case '.': // A decoration
                        charIndex++;
                        break;

                    case '\"': // I think this is a temporary kig sig. Although, it makes everythiong sound worse. No idea, just ignore it
                        //ParseKeySig(file, ref charIndex, true);  // True means put it in the tempKeySig
                        charIndex++;
                        checkList = new List<char> { '\"' };
                        SkipCharsUntil(file, ref charIndex, checkList); // Skip until end quote
                        charIndex++;
                        break;

                    case '+': // Decorations, ignore everything until the next + or newline
                        charIndex++;
                        checkList = new List<char> { '+', '\n' };
                        SkipCharsUntil(file, ref charIndex, checkList); // Skip until end quote
                        charIndex++;
                        break;
                    case '!': // More decorations, ignore everything until the next ! or newline
                        charIndex++;
                        checkList = new List<char> { '!', '\n' };
                        SkipCharsUntil(file, ref charIndex, checkList); // Skip until end quote
                        charIndex++;
                        break;

                    // Headers!
                    case 'K': // Key
                        ParseKeySig(file, ref charIndex);
                        charIndex++;
                        break;

                    case 'Q': // Tempo
                        ParseTempo(file, ref charIndex);
                        charIndex++;
                        break;

                    case 'L': // Default note duration
                        ParseDefaultNoteLength(file, ref charIndex);
                        charIndex++;
                        break;

                    case 'M': // Meter
                        ParseMeter(file, ref charIndex);
                        charIndex++;
                        break;

                    case 'C': // Composer, or a lot more likely, a C note. 
                        if (file[charIndex + 1] == ':')
                        {
                            ParseComposer(file, ref charIndex);
                        }
                        else
                            if (ParseNote(file, ref charIndex))
                            timeout = 32;
                        break;

                    case 'B': // Source, or a lot more likely, a B note.
                        if (file[charIndex + 1] == ':')
                        {
                            checkList = new List<char> { '\n', '\r' };
                            SkipCharsUntil(file, ref charIndex, checkList); // Skip until newline
                            charIndex++;
                        }
                        else
                            if (ParseNote(file, ref charIndex))
                            timeout = 32;
                        break;

                    case 'G': // Group, or a lot more likely, a G note. 
                        if (file[charIndex + 1] == ':')
                        {
                            ParseGroup(file, ref charIndex);
                        }
                        else
                            if (ParseNote(file, ref charIndex))
                            timeout = 32;
                        break;
                    // Note modifiers!
                    case '[': // Chord start
                        inChord = true;
                        charIndex++;
                        break;

                    case '|': // End of bar - reset all accidentals. Also cancel chord, it's a readibility thing idk
                        if (file[charIndex + 1] == ':')
                        {
                            charIndex += 2;
                            repeatStartIndex = charIndex;
                        }
                        else
                        {
                            inChord = false;
                            charIndex++;
                            SetKeySig(true);
                        }
                        break;

                    case ']': // Chard end
                        if (inChord)
                        {
                            // Chord is finished. Or is it?
                            // Check for duration, to see if the whole chord needs its duration modified
                            charIndex++;

                            float durationMod = ParseDuration(file, ref charIndex); // charIndex++ is done in here!
                            durationMod /= defaultNoteDuration;
                            if (durationMod != 1)
                            {
                                foreach (Note n in nextChord.notes)
                                    n.duration = (long)(n.duration * durationMod);
                                nextChord.duration = (long)(nextChord.duration * durationMod);
                            }

                            ChordDone();
                            inChord = false;
                        }
                        else
                        {
                            // If following a |, it's a thin-thick double bar line - end of tune.
                            if (file[charIndex - 1] == '|')
                                endOfFile = true;
                            else
                                charIndex++; // If not, weird. Skip it
                        }
                        break;

                    case '(': // Tuplets! A number will follow, meaning triplets, etc
                        charIndex++;
                        tupletDuration = GetIntFromStream(file, ref charIndex); // Should be <10 - add a check?
                        if (tupletDuration == 0)
                            ;  // actually just do nothing. It's a slur, we don't care about them. Sticks and stones or something idk
                        break;

                    case ' ':
                        // Spaces are usually ignored, except for a few things.
                        tuplet = false;
                        charIndex++;
                        break;

                    case ':':
                        // Probably a repeat thing
                        if (file[charIndex + 1] == '|')
                        {
                            if (doneRepeats.Contains(charIndex))
                            {
                                // ignore it, we've been here before!
                                charIndex += 2;
                            }
                            else
                            {
                                doneRepeats.Add(charIndex);
                                charIndex = repeatStartIndex;
                            }
                        }
                        else if (file[charIndex + 1] == ':')
                        {
                            if (doneRepeats.Contains(charIndex))
                            {
                                // ignore it, we've been here before!
                                charIndex += 2;
                            }
                            else
                            {
                                int nextResetIndex = charIndex;
                                doneRepeats.Add(charIndex);
                                charIndex = repeatStartIndex;
                                repeatStartIndex = nextResetIndex;
                            }
                        }
                        else
                        {
                            // ignore it I guess
                            charIndex++;
                        }
                        break;

                    // Finally, if none of the above, it's a note! Or a number. Let's see:
                    default:
                        if (ParseNote(file, ref charIndex))
                            timeout = 32;
                        break;
                }
            }
            if (timeout <= 0)
            {
                return false;
            }
            return true;
        }

        private void ChordDone()
        {
            // First, check that the new chord's endTime is after the parser's startTime.
            // This will be false for late starters in band play.

            float nextChordDuration = nextChord.duration; // Need to remember it before it is changed
            if (currentTime > (chordStartTime + nextChordDuration))
                ;
            else
            {
                if (startSync)
                {
                    // This is the first chord that finishes after currentTime.
                    // Make a dummy chord in order to sync with the master.
                    startSync = false;
                    if (currentTime == chordStartTime)
                    {
                        // The time exactly matches, might as well just play the note - no need for a dummy.
                        // Do nothing - use the nextChord parsed from the parser
                    }
                    else
                    {
                        float duration = (chordStartTime + nextChordDuration) - currentTime;
                        nextChord = new Chord();
                        nextChord.startTime = currentTime;
                        nextChord.duration = duration;
                        Note n = new Note();
                        n.duration = duration;
                        n.key = 'z';
                        nextChord.notes.Add(n);
                    }
                }
                chordBuffer.Add(nextChord);

                if (isPlayer)
                {
                    IPlayer player = Array.Find(serverAPI.World.AllOnlinePlayers, x => x.ClientId == playerID);
                    position = new Vec3d(player.Entity.Pos.X, player.Entity.Pos.Y, player.Entity.Pos.Z);
                }

                ABCUpdateFromServer packet = new ABCUpdateFromServer();
                packet.newChord = nextChord;
                packet.positon = position;
                packet.fromClientID = playerID;
                packet.instrument = instrument;
                IServerNetworkChannel ch = serverAPI.Network.GetChannel("abc");
                ch.BroadcastPacket(packet);
            }
            chordStartTime += nextChordDuration;
            barTime += nextChordDuration;
            /* if (barTime > barDuration)
             {
                 barTime -= barDuration;
                 //SetKeySig(); // This is not supposed to happen, makes other songs go all fucky
             }*/

            nextChord = new Chord();
            nextChord.startTime = chordStartTime;
        }
        private bool ParseNote(string inString, ref int i)
        {
            Note newNote = new Note();
            int noteIndex = -1;
            int octaveIndex = 3; // 4 is middle, but 2 sounds MUCH better - start there
            bool sharpDetected = false;
            bool flatDetected = false;
            bool naturalDetected = false;

            if (repeatStartIndex < 0)
                repeatStartIndex = i;

            while (CharAvailable(inString, i) && inString[i] == '^')
            {
                sharpDetected = true;
                i++;
            }
            while (CharAvailable(inString, i) && inString[i] == '_')
            {
                flatDetected = true;
                i++;
            }
            while (CharAvailable(inString, i) && inString[i] == '=')
            {
                naturalDetected = true;
                i++;
            }

            switch (inString[i])
            {
                case 'a':
                case 'b':
                    // As (most) songs have C as the middle note, we need to have any a or b keys an octave higher.
                    noteIndex = inString[i] - 'a';
                    newNote.key = inString[i++];
                    octaveIndex += 2; // lower case letters are an octave higher
                    break;
                case 'c':
                case 'd':
                case 'e':
                case 'f':
                case 'g':
                    noteIndex = inString[i] - 'a';
                    newNote.key = inString[i++];
                    octaveIndex += 1; // lower case letters are an octave higher
                    break;
                case 'A':
                case 'B':
                    noteIndex = inString[i] - 'A';
                    newNote.key = inString[i++];
                    octaveIndex += 1; // lower case letters are an octave higher
                    break;
                case 'C':
                case 'D':
                case 'E':
                case 'F':
                case 'G':
                    noteIndex = inString[i] - 'A';
                    newNote.key = inString[i++];
                    break;
                case 'Z':
                case 'z':
                case 'x':
                    // z means rest. x does too
                    i++;
                    newNote.key = 'z';
                    newNote.octave = 0;
                    newNote.accidental = Accidental.natural;
                    // don't look for octave or do accs
                    break;
                default:
                    // For some reason, the char wasn't a-g, as expected... weird
                    return false;
            }

            // For some reason, the char wasn't a-g, as expected... weird. Unless it's a rest.
            if (noteIndex == -1)
                ;
            else
            {
                // Check octave modifiers
                while (CharAvailable(inString, i) && inString[i] == '\'')
                {
                    // Apostrophe ' puts the note up an octave
                    octaveIndex += 1;
                    i++;
                }
                while (CharAvailable(inString, i) && inString[i] == ',')
                {
                    // Comma , puts the note down an octave
                    octaveIndex -= 1;
                    i++;
                }
                octaveIndex = GameMath.Min(7, octaveIndex);
                octaveIndex = GameMath.Max(0, octaveIndex);

                // Now that we have the octave, check the accidental - if there is one, modify the master accidental array
                if (sharpDetected)
                {
                    accidentals[noteIndex, octaveIndex] = Accidental.accSharp;
                    //newNote.accidental = Accidental.sharp;
                }
                if (flatDetected)
                {
                    accidentals[noteIndex, octaveIndex] = Accidental.accFlat;
                    //newNote.accidental = Accidental.flat;
                }
                if (naturalDetected)
                {
                    accidentals[noteIndex, octaveIndex] = Accidental.accNatural;
                    //newNote.accidental = Accidental.natural;
                }
                newNote.accidental = accidentals[noteIndex, octaveIndex];    // Fetch from the accidentals array, which might have been set proviously

                newNote.octave = octaveIndex;
                newNote.key = (Char.ToString(newNote.key)).ToLower()[0]; // Force to lowercase. Yeah, it couldn't be more convoluted.
            }

            newNote.duration = ParseDuration(file, ref charIndex); // charIndex++ is done in here!

            nextChord.AddNote(newNote, defaultNoteDuration / 2);

            if (!inChord)
            {
                // If not in a chord, the chord is now finished (it has its one note).
                // So, add it to the ChordsOngoing, and make a new chord.
                ChordDone();
            }
            return true;
        }
        private float ParseDuration(string inString, ref int i)
        {
            float duration = defaultNoteDuration;
            int modifier = GetIntFromStream(inString, ref i);
            if (modifier > 0)
                duration *= (float)modifier;
            // Ok now for some weird business, have fun
            while (CharAvailable(inString, i) && inString[i] == '/')
            {
                // If a / is found, it means we divide our duration by the next number
                // If no number is found, the default is /2
                // If multiple / are found, keep dividing by 2, hence the above while()
                i++;
                modifier = GetIntFromStream(inString, ref i);
                if (modifier > 0)
                    duration /= (float)modifier;
                else
                    duration /= 2f;
            }

            if (hornpipe)
            {
                duration /= 2;
                hornpipe = false;
            }

            // Still not over yet!
            while (CharAvailable(inString, i) && inString[i] == '>')
            {
                // A > (aka a hornpipe) represents a 'dotted' note
                // (therefore adds half its current duration to itself)
                duration += duration / 2f;
                hornpipe = true;
                i++;
            }

            // Last step! Check for the durationModifier, one of those (num things.
            if (tuplet)
            {
                duration *= 2f / (float)tupletDuration;
                tuplet = false;
            }

            return duration;
        }
        private void ParseMeter(string inString, ref int i)
        {
            // Parse meter, which may be used intead of L (default note length)
            List<char> checkList = new List<char> { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'C' };
            SkipCharsUntil(inString, ref i, checkList);

            if (inString[i] == 'C')
            {
                meterValue = 1;
            }
            else
            {
                // It's a number, parse as normal
                meterValue = GetIntFromStream(inString, ref i);
                if (inString[i++] == '/')
                {
                    // Get the value for the meter from the fractional form (ex: 4/4 or 6/8)
                    int den = GetIntFromStream(inString, ref i);
                    meterValue /= den;
                    meterDNL = den;
                    bpmFactor = 1.0f / den; // Just assume a beat is the same length as the length meter uses?
                }
                else
                {
                    // Something weird happened, set to free meter
                    meterValue = 1;
                }
            }

            // Calculate the default note length based on the Meter, if it has not already been found
            if (!LFound)
                defaultNoteLength = meterValue < 0.75f ? 0.0625f : 0.125f;
            if (!QFound)
                beatsPerMinute = 120 * (0.25f / bpmFactor);
            // Calculate the default note duration based off of our (potentially new) note length
            CalculateNoteDuration();
        }
        private void ParseKeySig(string inString, ref int i)
        {
            char key;
            bool minor = false;
            Accidental acc = Accidental.natural;
            List<char> checkList = new List<char> { 'A', 'B', 'C', 'D', 'E', 'F', 'G', '\n' };
            SkipCharsUntil(inString, ref i, checkList);
            key = inString[i];

            while (true)
            {
                checkList = new List<char> { '#', 'b', 'm', ' ', '\n', '\"' };
                SkipCharsUntil(inString, ref i, checkList);
                if (inString[i] == '#') // Sharp. There may be a minor, so repeat this section
                {
                    i++;
                    acc = Accidental.sharp;
                }
                else if (inString[i] == 'b') // Flat. There may be a minor, so repeat this section
                {
                    i++;
                    acc = Accidental.flat;
                }
                else if (inString[i] == ' ') // Space - will probably have a maj/min after it, stay in the loop
                {
                    i++;
                }
                else if (inString[i] == 'm') // Major/Minor. Always comes at the end, so break afterwards
                {
                    i++;
                    if (inString[i] == 'a' && inString[i + 1] == 'j')
                    {
                        // Major; ignore it, major is assumed
                    }
                    else
                    {
                        // Every other case - if we received just an m, or it says min/minor, it's a minor.
                        minor = true;
                    }
                    checkList = new List<char> { '\n' }; // Ignore anything else, go to newline
                    SkipCharsUntil(inString, ref i, checkList);
                    break;
                }
                else                    // No extras left, escape
                    break;
            }

            switch (key) // TODO Use a map!!!
            {
                case 'C':
                    if (acc == Accidental.sharp)
                    {
                        if (minor)
                            currentKeySig = E;
                        else
                            currentKeySig = Cs;
                    }
                    else if (acc == Accidental.flat)
                    {
                        // No minor
                        currentKeySig = Cf;
                    }
                    else
                    {
                        if (minor)
                            currentKeySig = Bf;
                        else
                            currentKeySig = C;
                    }
                    break;
                case 'F':
                    if (acc == Accidental.sharp)
                    {
                        if (minor)
                            currentKeySig = A;
                        else
                            currentKeySig = Fs;
                    }
                    else if (acc == Accidental.flat)
                    {
                        // No minor
                        currentKeySig = F; // Does not exist
                    }
                    else
                    {
                        if (minor)
                            currentKeySig = Af;
                        else
                            currentKeySig = F;
                    }
                    break;
                case 'A':
                    if (acc == Accidental.sharp)
                    {
                        if (minor)
                            currentKeySig = Cs;
                        else
                            currentKeySig = A; // Does not exist
                    }
                    else if (acc == Accidental.flat)
                    {
                        if (minor)
                            currentKeySig = Cf;
                        else
                            currentKeySig = Af;
                    }
                    else
                    {
                        if (minor)
                            currentKeySig = C;
                        else
                            currentKeySig = A;
                    }
                    break;
                case 'B':
                    if (acc == Accidental.sharp)
                    {
                        // No minor
                        currentKeySig = B; // Does not exist
                    }
                    else if (acc == Accidental.flat)
                    {
                        if (minor)
                            currentKeySig = Df;
                        else
                            currentKeySig = Bf;
                    }
                    else
                    {
                        if (minor)
                            currentKeySig = D;
                        else
                            currentKeySig = B;
                    }
                    break;
                case 'E':
                    if (acc == Accidental.sharp)
                    {
                        // No minor
                        currentKeySig = E; // Does not exist
                    }
                    else if (acc == Accidental.flat)
                    {
                        if (minor)
                            currentKeySig = Gf;
                        else
                            currentKeySig = Ef;
                    }
                    else
                    {
                        if (minor)
                            currentKeySig = G;
                        else
                            currentKeySig = E;
                    }
                    break;
                case 'G':
                    if (acc == Accidental.sharp)
                    {
                        if (minor)
                            currentKeySig = B;
                        else
                            currentKeySig = G; // Does not exist
                    }
                    else if (acc == Accidental.flat)
                    {
                        // No minor
                        currentKeySig = Gf;
                    }
                    else
                    {
                        if (minor)
                            currentKeySig = Bf;
                        else
                            currentKeySig = G;
                    }
                    break;
                case 'D':
                    if (acc == Accidental.sharp)
                    {
                        if (minor)
                            currentKeySig = Fs;
                        else
                            currentKeySig = D; // Does not exist
                    }
                    else if (acc == Accidental.flat)
                    {
                        // No minor
                        currentKeySig = Df;
                    }
                    else
                    {
                        if (minor)
                            currentKeySig = F;
                        else
                            currentKeySig = D;
                    }
                    break;

                case '\n': // If the line was escaped for some reason, assume C - but also log it?
                default:
                    currentKeySig = C;
                    break;
            }

            SetKeySig(false);
            //Debug.WriteLine("New key: " + key);
        }
        /*
        void CheckKeySigAccidentals(int nts, int shrps, int flts)
        {
            if (inputChar == 's')
            {
                setKeySig(shrps);
            }
            else if (inputChar == 'b')
            {
                setKeySig(flts);
            }
            else
            {
                setKeySig(nts);
            }
        }
        */

        void SetKeySig(bool wipeAccs = false)
        {
            for (int j = 0; j < 8; j++)
            {
                for (int i = 0; i < 7; i++)
                {
                    Accidental acc = accidentals[i, j];
                    if (acc == Accidental.accNatural || acc == Accidental.accSharp || acc == Accidental.accFlat)  // If an accidental has been applied to this note...
                        if (!wipeAccs)  // And we are not supposed to overwrite accidentals...
                            continue;   // Do not modify this accidental, skip it

                    accidentals[i, j] = (Accidental)currentKeySig[i];
                }
            }
        }
        private void ParseTempo(string inString, ref int i)
        {
            // Parse the tempo line to find the beatsPerMinute.
            // Possible forms:
            // Q: 90        - There are 90 beats per minute
            // Q: 1/4=90    - There are 90 1/4 notes per minute
            List<char> checkList = new List<char> { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
            SkipCharsUntil(inString, ref i, checkList);
            int noteDuration = GetIntFromStream(inString, ref i);
            // if a '/' is present, it means that they have specified which note to assign the tempo to, and we must make calculations based on this
            if (inString[i++] == '/')
            {

                bpmFactor = (float)noteDuration / (float)GetIntFromStream(inString, ref i);

                while (CharAvailable(inString, i) && inString[i] != '=')
                    i++;
                i++;
                while (CharAvailable(inString, i) && inString[i] == ' ')
                    i++;
                // the next int from the str will be our bpm for the given note length
                beatsPerMinute = GetIntFromStream(inString, ref i);
                //beatsPerMinute *= (bpmFactor / defaultNoteLength);
                // Calculate the default note duration based off of our (potentially new) bpm
                CalculateNoteDuration();
            }
            else
            {
                // If '/' was not present, they just gave us our tempo
                beatsPerMinute = noteDuration;
                if (LFound)
                    bpmFactor = defaultNoteLength;
                else
                    bpmFactor = 1 / meterDNL;

                // Calculate the default note duration based off of our bpm
                CalculateNoteDuration();
                i--; // Yes, terrible fix, fight me
            }
            QFound = true;
        }
        private void ParseDefaultNoteLength(string inString, ref int i)
        {
            // Parse the default note length, to find the default note length. 
            // Remove the expected ':' character
            List<char> checkList = new List<char> { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
            SkipCharsUntil(inString, ref i, checkList);
            // Get the value for the length from the fractional form (ex: 4/4)
            defaultNoteLength = GetIntFromStream(inString, ref i);
            if (inString[i++] == '/')
            {
                defaultNoteLength /= GetIntFromStream(inString, ref i);
            }
            // Calculate the default note duration based off of our (potentially new) note length
            CalculateNoteDuration();
            LFound = true;
        }
        private void ParseComposer(string inString, ref int i)
        {
            List<char> checkList = new List<char> { '\n' };
            SkipCharsUntil(inString, ref i, checkList);
        }
        private void ParseGroup(string inString, ref int i)
        {
            List<char> checkList = new List<char> { '\n' };
            SkipCharsUntil(inString, ref i, checkList);
        }
        private void Reset()
        {
            LFound = false;
            inChord = false;
            meterValue = 4 / 4;
            defaultNoteLength = 0.125f;
            meterDNL = 0.25f;
            beatsPerMinute = 120;
            defaultNoteDuration = 1;
            tupletDuration = 1;
            chordStartTime = 0;
            tuplet = false;
            endOfFile = false;

            nextChord = new Chord();
            chordBuffer = new List<Chord>();
            startSync = true;
            charIndex = 0;
            repeatStartIndex = -1;
            doneRepeats = new List<int>();

            accidentals = new Accidental[7, 8];
            currentKeySig = C;
            SetKeySig(true);
        }
        private void CalculateNoteDuration()
        {
            defaultNoteDuration = ((defaultNoteLength / bpmFactor) / beatsPerMinute) * 1000 * 60;
            //defaultNoteDuration = (1 / beatsPerMinute) * 1000 * 60;
            //barDuration = (beatsPerMinute * 1000 / 60) / meterValue;

        }
        private void SkipCharsUntil(string inString, ref int i, List<char> checkList)
        {
            while (CharAvailable(inString, i))
            {
                if (checkList.Contains(inString[i]))
                    break;
                i++;
            }
        }
        private int GetIntFromStream(string inString, ref int i)
        {
            int num = 0;
            while (CharAvailable(inString, i) && '0' <= inString[i] && inString[i] <= '9')
            {
                num = (num * 10) + (int)char.GetNumericValue(inString[i++]);
            }
            return num;
        }
        private bool CharAvailable(string inString, int i)
        {
            if (++i < inString.Length)
            {
                return true;
            }
            else
            {
                endOfFile = true;
                return false;
            }
        }
    }
    public class ABCParsers
    {
        private static ABCParsers _instance;
        List<ABCParser> list;
        ICoreServerAPI api;
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
        public void SetAPI(ICoreServerAPI newApi)
        {
            api = newApi;
        }
        public void Update(ICoreServerAPI sapi, float dt)
        {
            int count = list.Count;
            for (int i = 0; i < count; i++)
            {
                ABCParser abcp = list[i];
                ExitStatus parseStatus = abcp.Update(dt);
                if (parseStatus == ExitStatus.finished || parseStatus == ExitStatus.error)
                {
                    // TODO
                    IPlayer player = Array.Find(sapi.World.AllOnlinePlayers, x => x.ClientId == abcp.playerOwnerID);
                    if (player != null)
                    {
                        if (parseStatus == ExitStatus.finished)
                        {
                            MessageToClient(sapi, player, "abc playback finished!");                           
                        }
                        else
                            BadABC(sapi, player, abcp.charIndex);
                    }
                    // TODO copied from in instrument. Make into a single function pls
                    ABCStopFromServer packet = new ABCStopFromServer(); // todo copied from main, make a function
                    packet.fromClientID = abcp.playerID;
                    IServerNetworkChannel ch = api.Network.GetChannel("abc");
                    ch.BroadcastPacket(packet);

                    if (!abcp.isPlayer)
                    {
                        BlockPos bp = new BlockPos((int)abcp.position.X, (int)abcp.position.Y, (int)abcp.position.Z);
                        BlockEntity block = sapi.World.BlockAccessor.GetBlockEntity(bp);
                        if (block != null)
                            ((BEMusicBlock)block).isPlaying = false;
                    }
                    
                    // This is my attempt at gracefully removing something from a list                
                    list.Remove(abcp);
                    count--;
                    i--;
                }
            }
        }
        public List<ABCParser> Get()
        {
            return list;
        }
        public void MakeNewParser(ICoreServerAPI sapi, IPlayer byPlayer, string songData, string bandName, InstrumentType instrument)
        {
            // Does some band related checks before creating the parser
            if (bandName == "")
            {
                // Just a bog standard parser
                ABCParser abcp = new ABCParser(sapi, byPlayer.ClientId, byPlayer.PlayerName, songData, instrument, bandName, 0);
                ExitStatus parseOk = abcp.Start();
                if (parseOk != ExitStatus.allGood)
                    BadABC(sapi, byPlayer, abcp.charIndex);
                else
                {
                    list.Add(abcp);
                    MessageToClient(sapi, byPlayer, "Starting solo abc playback!");
                }
            }
            else
            {
                float masterTime = CheckBand(sapi, byPlayer, bandName);

                ABCParser abcp = new ABCParser(sapi, byPlayer.ClientId, byPlayer.PlayerName, songData, instrument, bandName, masterTime);
                ExitStatus parseOk = abcp.Start();
                if (parseOk != ExitStatus.allGood)
                    BadABC(sapi, byPlayer, abcp.charIndex);
                else
                    list.Add(abcp);
            }
        }
        public void MakeNewParser(ICoreServerAPI sapi, IPlayer byPlayer, string songData, int ownerID, string ownerName, string bandName, Vec3d pos, InstrumentType instrument)
        {
            // Used by blocks, where the byPlayer isn't the owner!
            if (bandName == "")
            {
                // Just a bog standard parser
                ABCParser abcp = new ABCParser(sapi, byPlayer.ClientId, ownerID, pos, ownerName, songData, instrument, bandName, 0);
                ExitStatus parseOk = abcp.Start();
                if (parseOk != ExitStatus.allGood)
                    BadABC(sapi, byPlayer, abcp.charIndex);
                else
                {
                    list.Add(abcp);
                    MessageToClient(sapi, byPlayer, "Starting solo abc playback!");
                }
            }
            else
            {
                float masterTime = CheckBand(sapi, byPlayer, bandName);

                ABCParser abcp = new ABCParser(sapi, byPlayer.ClientId, ownerID, pos, ownerName, songData, instrument, bandName, masterTime);
                ExitStatus parseOk = abcp.Start();
                if (parseOk != ExitStatus.allGood) // TODO check that the thing is actually destroyed
                    BadABC(sapi, byPlayer, abcp.charIndex);
                else
                    list.Add(abcp);
            }
        }
        public void Reset()
        {
            list.Clear();
        }

        public void Remove(ICoreServerAPI sapi, IPlayer byPlayer, ABCParser abcp)
        {
            if (byPlayer != null)
            {
                MessageToClient(sapi, byPlayer, "Stopping abc playback!");
            }
            list.Remove(abcp);
        }
        public ABCParser FindByID(int ID)
        {
            return list.Find(x => x.playerID == ID);
        }
        private float CheckBand(ICoreServerAPI sapi, IPlayer byPlayer, string bandName)
        {
            bool bandFound = false;
            List<string> bandPlayerNames = new List<string>();
            float masterTime = 0;
            foreach (ABCParser p in list)
            {
                if (p.bandName == bandName)
                {
                    masterTime = p.currentTime; // Otherwise, copy the master's time
                    bandPlayerNames.Add(p.playerName);
                    bandFound = true;
                }
            }
            if (bandFound)
            {
                string message = "Joining band \"" + bandName + "\" with players";
                foreach (string name in bandPlayerNames)
                    message += " " + name + ",";
                MessageToClient(sapi, byPlayer, message.Substring(0, message.Length - 1)); // substring stuff to remove the last comma
            }
            else
            {
                masterTime = -3000; // Start 3 seconds late, so that other players have time to start
                MessageToClient(sapi, byPlayer, "Starting new band \"" + bandName + "\"!");
            }

            return masterTime;
        }

        private void BadABC(ICoreServerAPI sapi, IPlayer byPlayer, int charIndex)
        {
            MessageToClient(sapi, byPlayer, "Error parsing file, char index " + charIndex);
        }
        private void MessageToClient(ICoreServerAPI sapi, IPlayer byPlayer, string message)
        {
            if (byPlayer != null) // The player might have left!
                sapi.SendMessage(byPlayer, 0, message, EnumChatType.Notification);
        }
    }
}
