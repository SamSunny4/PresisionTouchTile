using System.Diagnostics;

namespace TouchpadVisualizer.Game;

/// <summary>
/// Result of a hit attempt.
/// </summary>
public record HitResult(bool IsHit, TileState Quality, Tile? Tile);

/// <summary>
/// Core game engine for Piano Tiles.
/// Manages tile spawning, movement, hit detection, and scoring.
/// </summary>
public class PianoTilesGame
{
    // ─── Constants ─────────────────────────────────────────────────
    public const int LaneCount = 4;
    public const double PerfectWindowMs = 80;     // ±80ms for "Perfect"
    public const double GoodWindowMs = 160;       // ±160ms for "Good"
    public const double MissWindowMs = 250;       // Past this, tile is missed
    public const double ScrollDurationMs = 2200;  // Time for tile to travel from top to hit zone
    public const double HitZoneY = 0.82;          // Hit zone position (0=top, 1=bottom)
    public const double TileHeight = 0.15;        // Tile height in normalized coordinates

    // ─── Game State ────────────────────────────────────────────────
    public enum GameState { Menu, Countdown, Playing, Paused, GameOver }
    public GameState State { get; private set; } = GameState.Menu;

    // ─── Song & Tiles ──────────────────────────────────────────────
    public SongPattern? CurrentSong { get; private set; }
    private int _nextEventIndex;
    public List<Tile> ActiveTiles { get; } = new();
    private readonly object _tileLock = new();

    // ─── Timing ────────────────────────────────────────────────────
    private readonly Stopwatch _gameTimer = new();
    public double ElapsedMs => _gameTimer.ElapsedMilliseconds;
    private double _countdownStartMs;

    // ─── Scoring ───────────────────────────────────────────────────
    public int Score { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int PerfectCount { get; private set; }
    public int GoodCount { get; private set; }
    public int MissCount { get; private set; }
    public int TotalNotes => CurrentSong?.Events.Count ?? 0;

    // ─── Events ────────────────────────────────────────────────────
    public event Action<Tile, TileState>? OnTileHit;      // Tile was hit (Perfect/Good)
    public event Action<Tile>? OnTileMissed;              // Tile was missed
    public event Action<int>? OnComboChanged;             // Combo count changed
    public event Action<int>? OnCountdownTick;            // 3, 2, 1 countdown
    public event Action? OnGameOver;                      // Song finished
    public event Action? OnGameStarted;                   // Gameplay begins

    // ─── Latest feedback for UI ────────────────────────────────────
    public TileState? LastHitQuality { get; private set; }
    public double LastHitTime { get; private set; }
    public int LastHitLane { get; private set; } = -1;

    // ─── Countdown ─────────────────────────────────────────────────
    private int _lastCountdownValue = 4;

    /// <summary>
    /// Starts a song with a 3-second countdown.
    /// </summary>
    public void StartSong(SongPattern song)
    {
        CurrentSong = song;
        _nextEventIndex = 0;
        ActiveTiles.Clear();

        Score = 0;
        Combo = 0;
        MaxCombo = 0;
        PerfectCount = 0;
        GoodCount = 0;
        MissCount = 0;
        LastHitQuality = null;
        LastHitLane = -1;
        _lastCountdownValue = 4;

        State = GameState.Countdown;
        _gameTimer.Restart();
        _countdownStartMs = 0;
    }

    /// <summary>
    /// Updates the game state. Call this every frame.
    /// Returns the current countdown number (3, 2, 1) during countdown, or -1 during play.
    /// </summary>
    public int Update(double deltaMs)
    {
        int countdownValue = -1;

        switch (State)
        {
            case GameState.Countdown:
                double countdownElapsed = _gameTimer.ElapsedMilliseconds - _countdownStartMs;
                int currentCount = 3 - (int)(countdownElapsed / 1000);

                if (currentCount != _lastCountdownValue && currentCount >= 0)
                {
                    _lastCountdownValue = currentCount;
                    OnCountdownTick?.Invoke(currentCount);
                }

                countdownValue = Math.Max(0, currentCount);

                if (countdownElapsed >= 3000)
                {
                    State = GameState.Playing;
                    _gameTimer.Restart(); // Reset timer so song time starts at 0
                    OnGameStarted?.Invoke();
                }
                break;

            case GameState.Playing:
                UpdatePlaying();
                break;
        }

        return countdownValue;
    }

    private void UpdatePlaying()
    {
        double currentTime = _gameTimer.ElapsedMilliseconds;

        lock (_tileLock)
        {
            // Spawn new tiles that should now be visible
            while (_nextEventIndex < CurrentSong!.Events.Count)
            {
                var evt = CurrentSong.Events[_nextEventIndex];
                // Spawn tile when it should appear at the top of the screen
                double spawnTime = evt.TimeMs - ScrollDurationMs * HitZoneY;

                if (currentTime >= spawnTime)
                {
                    ActiveTiles.Add(new Tile
                    {
                        Lane = evt.Lane,
                        TargetTimeMs = evt.TimeMs,
                        MidiNote = evt.MidiNote,
                        State = TileState.Active,
                        YPosition = 0
                    });
                    _nextEventIndex++;
                }
                else
                {
                    break;
                }
            }

            // Update tile positions
            foreach (var tile in ActiveTiles)
            {
                if (tile.State == TileState.Active)
                {
                    // Y position: how far along the scroll this tile is
                    double timeSinceSpawn = currentTime - (tile.TargetTimeMs - ScrollDurationMs * HitZoneY);
                    tile.YPosition = timeSinceSpawn / ScrollDurationMs;

                    // Check if tile has passed the miss window
                    double timePastHitZone = currentTime - tile.TargetTimeMs;
                    if (timePastHitZone > MissWindowMs)
                    {
                        tile.State = TileState.Missed;
                        tile.AnimProgress = 0;
                        MissCount++;
                        Combo = 0;
                        OnComboChanged?.Invoke(Combo);
                        OnTileMissed?.Invoke(tile);

                        LastHitQuality = TileState.Missed;
                        LastHitTime = currentTime;
                        LastHitLane = tile.Lane;
                    }
                }
                else
                {
                    // Animate hit/miss effect
                    tile.AnimProgress = Math.Min(1.0, tile.AnimProgress + 0.05);
                }
            }

            // Remove finished tiles (fully animated out)
            ActiveTiles.RemoveAll(t => t.IsFinished);

            // Check for game over (all events spawned and all tiles processed)
            if (_nextEventIndex >= CurrentSong.Events.Count && ActiveTiles.Count == 0)
            {
                State = GameState.GameOver;
                _gameTimer.Stop();
                OnGameOver?.Invoke();
            }
        }
    }

    /// <summary>
    /// Attempts to hit a tile in the given lane at the current time.
    /// Returns the result of the hit attempt.
    /// </summary>
    public HitResult TryHit(int lane)
    {
        if (State != GameState.Playing)
            return new HitResult(false, TileState.Active, null);

        double currentTime = _gameTimer.ElapsedMilliseconds;

        lock (_tileLock)
        {
            // Find the closest active tile in this lane within the hit window
            Tile? bestTile = null;
            double bestDiff = double.MaxValue;

            foreach (var tile in ActiveTiles)
            {
                if (tile.State != TileState.Active || tile.Lane != lane)
                    continue;

                double diff = Math.Abs(currentTime - tile.TargetTimeMs);
                if (diff < bestDiff && diff <= GoodWindowMs)
                {
                    bestDiff = diff;
                    bestTile = tile;
                }
            }

            if (bestTile == null)
            {
                // Wrong tap — no tile in this lane near the hit zone
                // Don't break combo for empty taps, just ignore
                return new HitResult(false, TileState.Active, null);
            }

            // Determine hit quality
            TileState quality;
            int points;

            if (bestDiff <= PerfectWindowMs)
            {
                quality = TileState.HitPerfect;
                points = 100;
                PerfectCount++;
            }
            else
            {
                quality = TileState.HitGood;
                points = 50;
                GoodCount++;
            }

            // Apply hit
            bestTile.State = quality;
            bestTile.AnimProgress = 0;

            // Update combo
            Combo++;
            if (Combo > MaxCombo) MaxCombo = Combo;
            OnComboChanged?.Invoke(Combo);

            // Score with combo multiplier
            double comboMultiplier = 1.0 + (Combo / 10.0) * 0.5;
            Score += (int)(points * comboMultiplier);

            // UI feedback
            LastHitQuality = quality;
            LastHitTime = currentTime;
            LastHitLane = lane;

            OnTileHit?.Invoke(bestTile, quality);

            return new HitResult(true, quality, bestTile);
        }
    }

    /// <summary>
    /// Pauses the game.
    /// </summary>
    public void Pause()
    {
        if (State == GameState.Playing)
        {
            State = GameState.Paused;
            _gameTimer.Stop();
        }
    }

    /// <summary>
    /// Resumes a paused game.
    /// </summary>
    public void Resume()
    {
        if (State == GameState.Paused)
        {
            State = GameState.Playing;
            _gameTimer.Start();
        }
    }

    /// <summary>
    /// Gets a snapshot of all active tiles for rendering. Thread-safe.
    /// </summary>
    public IReadOnlyList<Tile> GetTileSnapshot()
    {
        return ActiveTiles;
    }

    /// <summary>
    /// Determines which lane a normalized X touchpad position maps to.
    /// </summary>
    public static int GetLaneFromTouchX(float normalizedX)
    {
        return normalizedX switch
        {
            < 0.25f => 0,
            < 0.50f => 1,
            < 0.75f => 2,
            _ => 3
        };
    }
}
