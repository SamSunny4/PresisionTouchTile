namespace TouchpadVisualizer.Game;

/// <summary>
/// Represents the current state of a tile in the game.
/// </summary>
public enum TileState
{
    /// <summary>Tile is actively falling toward the hit zone.</summary>
    Active,
    /// <summary>Tile was tapped with perfect timing.</summary>
    HitPerfect,
    /// <summary>Tile was tapped with good timing.</summary>
    HitGood,
    /// <summary>Tile passed the hit zone without being tapped.</summary>
    Missed,
    /// <summary>Tile is currently being held correctly.</summary>
    Holding,
}

/// <summary>
/// Represents a single tile in the Piano Tiles game.
/// Tiles fall from the top of the screen toward the hit zone.
/// </summary>
public class Tile
{
    /// <summary>Lane index (0 to LaneCount-1, left to right).</summary>
    public int Lane { get; set; }

    /// <summary>The time (in milliseconds from song start) when this tile should reach the hit zone.</summary>
    public double TargetTimeMs { get; set; }

    /// <summary>MIDI note number to play on hit (e.g., 60 = C4).</summary>
    public byte MidiNote { get; set; }

    /// <summary>Current state of this tile.</summary>
    public TileState State { get; set; } = TileState.Active;

    /// <summary>
    /// Normalized Y position for rendering. 
    /// 0.0 = at the top spawn point, 1.0 = at the hit zone, >1.0 = past the hit zone.
    /// </summary>
    public double YPosition { get; set; }

    /// <summary>Animation progress for hit/miss effects (0.0 to 1.0).</summary>
    public double AnimProgress { get; set; }

    /// <summary>Duration of the hold note in milliseconds (0 for normal tap tiles).</summary>
    public double DurationMs { get; set; }

    /// <summary>Whether this is a hold tile.</summary>
    public bool IsHoldNote => DurationMs > 0;

    /// <summary>Whether this tile has been processed (hit or missed) and can be removed.</summary>
    public bool IsFinished => State != TileState.Active && State != TileState.Holding && AnimProgress >= 1.0;
}
