namespace TouchpadVisualizer.Game;

/// <summary>
/// A single note event in a song pattern — defines when a tile appears, in which lane, and what note to play.
/// </summary>
public record TileEvent(double TimeMs, int Lane, byte MidiNote);

/// <summary>
/// Defines a complete song as a sequence of tile events with metadata.
/// Contains factory methods for pre-composed songs.
/// </summary>
public class SongPattern
{
    public string Name { get; init; } = "";
    public string Artist { get; init; } = "";
    public int Bpm { get; init; } = 120;
    public string Difficulty { get; init; } = "Normal";
    public List<TileEvent> Events { get; init; } = new();

    /// <summary>Duration of the song in milliseconds.</summary>
    public double DurationMs => Events.Count > 0 ? Events[^1].TimeMs + 2000 : 0;

    /// <summary>The lane count this song was composed for (default 4).</summary>
    public int OriginalLaneCount { get; init; } = 4;

    /// <summary>
    /// Returns a new SongPattern with all lane assignments remapped to fit the target lane count.
    /// Notes are spread proportionally based on their original lane positions.
    /// </summary>
    public SongPattern RemapToLanes(int targetLaneCount)
    {
        if (targetLaneCount == OriginalLaneCount)
            return this;

        var remapped = new List<TileEvent>(Events.Count);
        foreach (var evt in Events)
        {
            // Map from [0, OriginalLaneCount) to [0, targetLaneCount)
            // Use proportional mapping: preserve relative position
            double normalizedPos = (evt.Lane + 0.5) / OriginalLaneCount;
            int newLane = (int)(normalizedPos * targetLaneCount);
            newLane = Math.Clamp(newLane, 0, targetLaneCount - 1);
            remapped.Add(new TileEvent(evt.TimeMs, newLane, evt.MidiNote));
        }

        return new SongPattern
        {
            Name = Name,
            Artist = Artist,
            Bpm = Bpm,
            Difficulty = Difficulty,
            Events = remapped,
            OriginalLaneCount = OriginalLaneCount,
        };
    }

    /// <summary>Returns all available pre-composed songs.</summary>
    public static List<SongPattern> GetAllSongs() =>
    [
        TwinkleTwinkle(),
        OdeToJoy(),
        CanonInD(),
        FurElise(),
        MoonlightSonataTheme(),
    ];

    // ─── Helper ─────────────────────────────────────────────────────
    private static double Beat(int beatIndex, int bpm) => beatIndex * (60000.0 / bpm);
    private static double HalfBeat(int beatIndex, int bpm) => beatIndex * (30000.0 / bpm);

    // ─── SONG 1: Twinkle Twinkle Little Star ───────────────────────

    public static SongPattern TwinkleTwinkle()
    {
        const int bpm = 110;
        // C4=60, D4=62, E4=64, F4=65, G4=67, A4=69
        // Melody: C C G G A A G | F F E E D D C | G G F F E E D | G G F F E E D | C C G G A A G | F F E E D D C
        // Lane mapping: C→0, D→1, E→2, F→1, G→3, A→2
        var events = new List<TileEvent>();
        int b = 0;

        // Verse 1: "Twinkle twinkle little star"
        // C C G G A A G-
        events.Add(new(Beat(b++, bpm), 0, 60));  // C
        events.Add(new(Beat(b++, bpm), 0, 60));  // C
        events.Add(new(Beat(b++, bpm), 3, 67));  // G
        events.Add(new(Beat(b++, bpm), 3, 67));  // G
        events.Add(new(Beat(b++, bpm), 2, 69));  // A
        events.Add(new(Beat(b++, bpm), 2, 69));  // A
        events.Add(new(Beat(b++, bpm), 3, 67));  // G (held)
        b++; // rest

        // "How I wonder what you are"
        // F F E E D D C-
        events.Add(new(Beat(b++, bpm), 1, 65));  // F
        events.Add(new(Beat(b++, bpm), 1, 65));  // F
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 1, 62));  // D
        events.Add(new(Beat(b++, bpm), 1, 62));  // D
        events.Add(new(Beat(b++, bpm), 0, 60));  // C (held)
        b++; // rest

        // "Up above the world so high"
        // G G F F E E D-
        events.Add(new(Beat(b++, bpm), 3, 67));  // G
        events.Add(new(Beat(b++, bpm), 3, 67));  // G
        events.Add(new(Beat(b++, bpm), 1, 65));  // F
        events.Add(new(Beat(b++, bpm), 1, 65));  // F
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 1, 62));  // D (held)
        b++; // rest

        // "Like a diamond in the sky"
        // G G F F E E D-
        events.Add(new(Beat(b++, bpm), 3, 67));  // G
        events.Add(new(Beat(b++, bpm), 3, 67));  // G
        events.Add(new(Beat(b++, bpm), 1, 65));  // F
        events.Add(new(Beat(b++, bpm), 1, 65));  // F
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 1, 62));  // D (held)
        b++; // rest

        // Repeat verse 1
        events.Add(new(Beat(b++, bpm), 0, 60));
        events.Add(new(Beat(b++, bpm), 0, 60));
        events.Add(new(Beat(b++, bpm), 3, 67));
        events.Add(new(Beat(b++, bpm), 3, 67));
        events.Add(new(Beat(b++, bpm), 2, 69));
        events.Add(new(Beat(b++, bpm), 2, 69));
        events.Add(new(Beat(b++, bpm), 3, 67));
        b++;

        events.Add(new(Beat(b++, bpm), 1, 65));
        events.Add(new(Beat(b++, bpm), 1, 65));
        events.Add(new(Beat(b++, bpm), 2, 64));
        events.Add(new(Beat(b++, bpm), 2, 64));
        events.Add(new(Beat(b++, bpm), 1, 62));
        events.Add(new(Beat(b++, bpm), 1, 62));
        events.Add(new(Beat(b++, bpm), 0, 60));

        return new SongPattern
        {
            Name = "Twinkle Twinkle Little Star",
            Artist = "Traditional",
            Bpm = bpm,
            Difficulty = "Easy",
            Events = events
        };
    }

    // ─── SONG 2: Ode to Joy (Beethoven) ────────────────────────────

    public static SongPattern OdeToJoy()
    {
        const int bpm = 120;
        // E E F G | G F E D | C C D E | E- D D-
        // E E F G | G F E D | C C D E | D- C C-
        // D D E C | D EF E C | D EF E D | C D G-
        // E4=64, F4=65, G4=67, D4=62, C4=60
        // Lane mapping: C→0, D→1, E→2, F→1, G→3
        var events = new List<TileEvent>();
        int b = 0;

        // Line 1: E E F G G F E D
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 1, 65));  // F
        events.Add(new(Beat(b++, bpm), 3, 67));  // G
        events.Add(new(Beat(b++, bpm), 3, 67));  // G
        events.Add(new(Beat(b++, bpm), 1, 65));  // F
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 1, 62));  // D

        // Line 2: C C D E E- D D-
        events.Add(new(Beat(b++, bpm), 0, 60));  // C
        events.Add(new(Beat(b++, bpm), 0, 60));  // C
        events.Add(new(Beat(b++, bpm), 1, 62));  // D
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 2, 64));  // E (dotted)
        b++; // hold
        events.Add(new(Beat(b++, bpm), 1, 62));  // D
        events.Add(new(Beat(b++, bpm), 1, 62));  // D (held)
        b++; // rest

        // Line 3: E E F G G F E D
        events.Add(new(Beat(b++, bpm), 2, 64));
        events.Add(new(Beat(b++, bpm), 2, 64));
        events.Add(new(Beat(b++, bpm), 1, 65));
        events.Add(new(Beat(b++, bpm), 3, 67));
        events.Add(new(Beat(b++, bpm), 3, 67));
        events.Add(new(Beat(b++, bpm), 1, 65));
        events.Add(new(Beat(b++, bpm), 2, 64));
        events.Add(new(Beat(b++, bpm), 1, 62));

        // Line 4: C C D E D- C C-
        events.Add(new(Beat(b++, bpm), 0, 60));
        events.Add(new(Beat(b++, bpm), 0, 60));
        events.Add(new(Beat(b++, bpm), 1, 62));
        events.Add(new(Beat(b++, bpm), 2, 64));
        events.Add(new(Beat(b++, bpm), 1, 62));
        b++;
        events.Add(new(Beat(b++, bpm), 0, 60));
        events.Add(new(Beat(b++, bpm), 0, 60));
        b++; // rest

        // Bridge: D D E C D E-F E C D E-F E D C D G
        events.Add(new(Beat(b++, bpm), 1, 62));  // D
        events.Add(new(Beat(b++, bpm), 1, 62));  // D
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 0, 60));  // C
        events.Add(new(Beat(b++, bpm), 1, 62));  // D
        events.Add(new(HalfBeat(b * 2, bpm), 2, 64));     // E (eighth)
        events.Add(new(HalfBeat(b * 2 + 1, bpm), 1, 65)); // F (eighth)
        b++;
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 0, 60));  // C

        events.Add(new(Beat(b++, bpm), 1, 62));  // D
        events.Add(new(HalfBeat(b * 2, bpm), 2, 64));     // E
        events.Add(new(HalfBeat(b * 2 + 1, bpm), 1, 65)); // F
        b++;
        events.Add(new(Beat(b++, bpm), 2, 64));  // E
        events.Add(new(Beat(b++, bpm), 1, 62));  // D
        events.Add(new(Beat(b++, bpm), 0, 60));  // C
        events.Add(new(Beat(b++, bpm), 1, 62));  // D
        events.Add(new(Beat(b++, bpm), 3, 55));  // G3 (low)
        b++;

        // Final reprise
        events.Add(new(Beat(b++, bpm), 2, 64));
        events.Add(new(Beat(b++, bpm), 2, 64));
        events.Add(new(Beat(b++, bpm), 1, 65));
        events.Add(new(Beat(b++, bpm), 3, 67));
        events.Add(new(Beat(b++, bpm), 3, 67));
        events.Add(new(Beat(b++, bpm), 1, 65));
        events.Add(new(Beat(b++, bpm), 2, 64));
        events.Add(new(Beat(b++, bpm), 1, 62));
        events.Add(new(Beat(b++, bpm), 0, 60));
        events.Add(new(Beat(b++, bpm), 0, 60));
        events.Add(new(Beat(b++, bpm), 1, 62));
        events.Add(new(Beat(b++, bpm), 2, 64));
        events.Add(new(Beat(b++, bpm), 1, 62));
        b++;
        events.Add(new(Beat(b++, bpm), 0, 60));
        events.Add(new(Beat(b++, bpm), 0, 60));

        return new SongPattern
        {
            Name = "Ode to Joy",
            Artist = "Beethoven",
            Bpm = bpm,
            Difficulty = "Medium",
            Events = events
        };
    }

    // ─── SONG 3: Canon in D (Pachelbel) ────────────────────────────

    public static SongPattern CanonInD()
    {
        const int bpm = 100;
        // The famous 8-chord progression repeated with melody
        // D-A-Bm-F#m-G-D-G-A
        // Simplified melody over the chords using 4 lanes
        // F#4=66, G4=67, A4=69, B4=71, C#5=73, D5=74, E5=76

        var events = new List<TileEvent>();
        int b = 0;

        // Phrase 1: F# G A F# G A (ascending arpeggios)
        void AddPhrase1(ref int beat)
        {
            events.Add(new(Beat(beat++, bpm), 1, 66));  // F#
            events.Add(new(Beat(beat++, bpm), 2, 69));  // A
            events.Add(new(Beat(beat++, bpm), 3, 71));  // B
            events.Add(new(Beat(beat++, bpm), 2, 69));  // A
            events.Add(new(Beat(beat++, bpm), 1, 67));  // G
            events.Add(new(Beat(beat++, bpm), 0, 62));  // D
            events.Add(new(Beat(beat++, bpm), 1, 67));  // G
            events.Add(new(Beat(beat++, bpm), 2, 69));  // A
        }

        // Phrase 2: Higher melody
        void AddPhrase2(ref int beat)
        {
            events.Add(new(Beat(beat++, bpm), 2, 69));  // A
            events.Add(new(Beat(beat++, bpm), 3, 74));  // D5
            events.Add(new(Beat(beat++, bpm), 2, 73));  // C#5
            events.Add(new(Beat(beat++, bpm), 3, 74));  // D5
            events.Add(new(Beat(beat++, bpm), 2, 69));  // A
            events.Add(new(Beat(beat++, bpm), 1, 67));  // G
            events.Add(new(Beat(beat++, bpm), 0, 66));  // F#
            events.Add(new(Beat(beat++, bpm), 1, 67));  // G
        }

        // Phrase 3: Descending pattern
        void AddPhrase3(ref int beat)
        {
            events.Add(new(Beat(beat++, bpm), 3, 74));  // D5
            events.Add(new(Beat(beat++, bpm), 2, 73));  // C#5
            events.Add(new(Beat(beat++, bpm), 1, 71));  // B
            events.Add(new(Beat(beat++, bpm), 2, 73));  // C#5
            events.Add(new(Beat(beat++, bpm), 3, 74));  // D5
            events.Add(new(Beat(beat++, bpm), 2, 69));  // A
            events.Add(new(Beat(beat++, bpm), 1, 67));  // G
            events.Add(new(Beat(beat++, bpm), 2, 69));  // A
        }

        // Phrase 4: Resolution
        void AddPhrase4(ref int beat)
        {
            events.Add(new(Beat(beat++, bpm), 1, 71));  // B
            events.Add(new(Beat(beat++, bpm), 2, 69));  // A
            events.Add(new(Beat(beat++, bpm), 1, 67));  // G
            events.Add(new(Beat(beat++, bpm), 0, 66));  // F#
            events.Add(new(Beat(beat++, bpm), 1, 67));  // G
            events.Add(new(Beat(beat++, bpm), 2, 69));  // A
            events.Add(new(Beat(beat++, bpm), 0, 62));  // D
            events.Add(new(Beat(beat++, bpm), 1, 66));  // F#
        }

        // Play through 3 full cycles with increasing complexity
        AddPhrase1(ref b);
        AddPhrase2(ref b);
        AddPhrase1(ref b);
        AddPhrase3(ref b);
        AddPhrase2(ref b);
        AddPhrase4(ref b);

        // Faster section - eighth notes
        for (int i = 0; i < 2; i++)
        {
            events.Add(new(HalfBeat(b * 2, bpm), 0, 62));
            events.Add(new(HalfBeat(b * 2 + 1, bpm), 1, 66));
            b++;
            events.Add(new(HalfBeat(b * 2, bpm), 2, 69));
            events.Add(new(HalfBeat(b * 2 + 1, bpm), 3, 74));
            b++;
            events.Add(new(HalfBeat(b * 2, bpm), 2, 73));
            events.Add(new(HalfBeat(b * 2 + 1, bpm), 3, 74));
            b++;
            events.Add(new(HalfBeat(b * 2, bpm), 2, 69));
            events.Add(new(HalfBeat(b * 2 + 1, bpm), 1, 67));
            b++;
        }

        // Final resolution
        events.Add(new(Beat(b++, bpm), 1, 67));
        events.Add(new(Beat(b++, bpm), 0, 66));
        events.Add(new(Beat(b++, bpm), 1, 67));
        events.Add(new(Beat(b++, bpm), 2, 69));
        events.Add(new(Beat(b++, bpm), 0, 62));  // D final

        return new SongPattern
        {
            Name = "Canon in D",
            Artist = "Pachelbel",
            Bpm = bpm,
            Difficulty = "Medium",
            Events = events
        };
    }

    // ─── SONG 4: Für Elise (Beethoven) ─────────────────────────────

    public static SongPattern FurElise()
    {
        const int bpm = 140;
        // The iconic opening: E5 D#5 E5 D#5 E5 B4 D5 C5 A4
        // Then: C4 E4 A4 B4 | E4 G#4 B4 C5
        // E5=76, D#5=75, B4=71, D5=74, C5=72, A4=69, C4=60, E4=64, G#4=68

        var events = new List<TileEvent>();
        int b = 0;

        // Main motif (play 3 times with variations)
        void AddMotif(ref int beat)
        {
            events.Add(new(HalfBeat(beat * 2, bpm), 3, 76));      // E5
            events.Add(new(HalfBeat(beat * 2 + 1, bpm), 2, 75));  // D#5
            beat++;
            events.Add(new(HalfBeat(beat * 2, bpm), 3, 76));      // E5
            events.Add(new(HalfBeat(beat * 2 + 1, bpm), 2, 75));  // D#5
            beat++;
            events.Add(new(HalfBeat(beat * 2, bpm), 3, 76));      // E5
            events.Add(new(HalfBeat(beat * 2 + 1, bpm), 1, 71));  // B4
            beat++;
            events.Add(new(HalfBeat(beat * 2, bpm), 2, 74));      // D5
            events.Add(new(HalfBeat(beat * 2 + 1, bpm), 1, 72));  // C5
            beat++;
            events.Add(new(Beat(beat++, bpm), 0, 69));             // A4 (held)
        }

        // Response phrase
        void AddResponse(ref int beat)
        {
            events.Add(new(HalfBeat(beat * 2, bpm), 0, 60));      // C4
            events.Add(new(HalfBeat(beat * 2 + 1, bpm), 1, 64));  // E4
            beat++;
            events.Add(new(HalfBeat(beat * 2, bpm), 2, 69));      // A4
            events.Add(new(HalfBeat(beat * 2 + 1, bpm), 3, 71));  // B4
            beat++;
            beat++; // rest

            events.Add(new(HalfBeat(beat * 2, bpm), 1, 64));      // E4
            events.Add(new(HalfBeat(beat * 2 + 1, bpm), 2, 68));  // G#4
            beat++;
            events.Add(new(HalfBeat(beat * 2, bpm), 3, 71));      // B4
            events.Add(new(HalfBeat(beat * 2 + 1, bpm), 2, 72));  // C5
            beat++;
            beat++; // rest
        }

        // Build the song structure
        AddMotif(ref b);
        b++; // rest
        AddResponse(ref b);
        AddMotif(ref b);

        // Ending phrase
        events.Add(new(HalfBeat(b * 2, bpm), 0, 60));
        events.Add(new(HalfBeat(b * 2 + 1, bpm), 1, 64));
        b++;
        events.Add(new(Beat(b++, bpm), 2, 69));  // A4

        b++; // rest

        // Second motif cycle
        AddMotif(ref b);
        b++;
        AddResponse(ref b);
        AddMotif(ref b);

        // Final resolution
        events.Add(new(HalfBeat(b * 2, bpm), 0, 60));
        events.Add(new(HalfBeat(b * 2 + 1, bpm), 1, 64));
        b++;
        events.Add(new(Beat(b++, bpm), 0, 69));  // A4 final

        return new SongPattern
        {
            Name = "Für Elise",
            Artist = "Beethoven",
            Bpm = bpm,
            Difficulty = "Hard",
            Events = events
        };
    }

    // ─── SONG 5: Moonlight Sonata Theme ────────────────────────────

    public static SongPattern MoonlightSonataTheme()
    {
        const int bpm = 72;
        // C#m arpeggio pattern: C# E G# repeated in triplets
        // Melody on top: G#4 A4 B4 etc.
        // C#4=61, E4=64, G#4=68, A4=69, B4=71, C#5=73

        var events = new List<TileEvent>();
        double t = 0;
        double triplet = 60000.0 / bpm / 3.0; // triplet eighth note duration

        // Moonlight Sonata has a repeating triplet arpeggio pattern
        // Each "beat" has 3 notes: bass-mid-high arpeggiated
        void AddTripletBar(ref double time, byte n1, byte n2, byte n3, int reps = 4)
        {
            for (int i = 0; i < reps; i++)
            {
                events.Add(new(time, 0, n1)); time += triplet;
                events.Add(new(time, 1, n2)); time += triplet;
                events.Add(new(time, 2, n3)); time += triplet;
            }
        }

        // Measure 1-2: C#m (C#-E-G#)
        AddTripletBar(ref t, 61, 64, 68);
        AddTripletBar(ref t, 61, 64, 68);

        // Measure 3-4: A (A-C#-E) with melody G# moving to A
        AddTripletBar(ref t, 57, 61, 64);
        // Add melody note on top
        events.Add(new(t, 3, 68)); // G#4 melody
        AddTripletBar(ref t, 57, 61, 64);

        // Measure 5-6: F#m (F#-A-C#)
        AddTripletBar(ref t, 54, 57, 61);
        events.Add(new(t, 3, 69)); // A4 melody
        AddTripletBar(ref t, 54, 57, 61);

        // Measure 7-8: G# → C#m (G#-B-E → C#-E-G#)
        AddTripletBar(ref t, 56, 59, 64);
        events.Add(new(t, 3, 71)); // B4 melody
        AddTripletBar(ref t, 61, 64, 68);
        events.Add(new(t, 3, 73)); // C#5 melody

        // Measure 9-12: Repeat with higher melody
        AddTripletBar(ref t, 61, 64, 68);
        events.Add(new(t, 3, 73)); // C#5
        AddTripletBar(ref t, 61, 64, 68);
        events.Add(new(t, 3, 71)); // B4

        AddTripletBar(ref t, 57, 61, 64);
        events.Add(new(t, 3, 69)); // A4
        AddTripletBar(ref t, 54, 57, 61);
        events.Add(new(t, 3, 68)); // G#4

        // Final measures: resolution
        AddTripletBar(ref t, 56, 59, 64);
        AddTripletBar(ref t, 61, 64, 68, 2);
        events.Add(new(t, 0, 49)); // C#3 final bass
        events.Add(new(t + triplet * 2, 2, 68)); // G#4 final

        return new SongPattern
        {
            Name = "Moonlight Sonata",
            Artist = "Beethoven",
            Bpm = bpm,
            Difficulty = "Hard",
            Events = events
        };
    }
}
