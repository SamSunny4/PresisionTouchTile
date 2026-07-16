using System;
using System.Linq;
using TouchpadVisualizer.Game;

class Program {
    static void Main() {
        var songs = SongPattern.GetAllSongs();
        Console.WriteLine("=== Testing current lane mapping ===");
        foreach (var song in songs) {
            Console.WriteLine($"Song: {song.Name} (Original Lanes: {song.OriginalLaneCount})");
            for (int lanes = 3; lanes <= 6; lanes++) {
                var remapped = song.RemapToLanes(lanes);
                var usedLanes = remapped.Events.Select(e => e.Lane).Distinct().OrderBy(l => l).ToList();
                Console.WriteLine($"  Remapped to {lanes} lanes: Used lanes = [{string.Join(", ", usedLanes)}]");
            }
        }
    }
}
