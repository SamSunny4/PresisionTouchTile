using System.Diagnostics;

namespace TouchpadVisualizer.Game;

/// <summary>
/// Result of a hit attempt.
/// </summary>
public record HitResult(bool IsHit, TileState Quality, Tile? Tile);

/// <summary>
/// Core game engine for Piano Tiles.
/// Manages tile spawning, movement, hit detection, and scoring.
/// Supports configurable lane count (3–6), Easy Mode, and wrong-input penalties.
/// </summary>
public class PianoTilesGame
{
    // ─── Configurable Settings ────────────────────────────────────
    public const int MinLanes = 3;
    public const int MaxLanes = 6;
    public const int DefaultLaneCount = 4;

    /// <summary>Number of lanes for the current game session (3–6).</summary>
    public int LaneCount { get; private set; } = DefaultLaneCount;

    /// <summary>When true, tiles freeze at the hit zone and wait for player input.</summary>
    public bool EasyMode { get; set; }

    // ─── Constants ─────────────────────────────────────────────────
    public const double PerfectWindowMs = 80;     // ±80ms for "Perfect"
    public const double GoodWindowMs = 160;       // ±160ms for "Good"
    public const double MissWindowMs = 250;       // Past this, tile is missed
    public const double ScrollDurationMs = 2200;  // Time for tile to travel from top to hit zone
    public const double HitZoneY = 0.82;          // Hit zone position (0=top, 1=bottom)
    public const double TileHeight = 0.15;        // Tile height in normalized coordinates

    // ─── Wrong input penalty ──────────────────────────────────────
    public const int WrongHitPenalty = 25;

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

    // ─── Easy Mode State ──────────────────────────────────────────
    /// <summary>True when the game is frozen waiting for user input in Easy Mode.</summary>
    public bool IsWaitingForInput { get; private set; }
    /// <summary>The lane of the tile that is currently waiting for input in Easy Mode.</summary>
    public int WaitingLane { get; private set; } = -1;

    // ─── Scoring ───────────────────────────────────────────────────
    public int Score { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int PerfectCount { get; private set; }
    public int GoodCount { get; private set; }
    public int MissCount { get; private set; }
    public int WrongHitCount { get; private set; }
    public int TotalNotes => CurrentSong?.Events.Count ?? 0;

    // ─── Events ────────────────────────────────────────────────────
    public event Action<Tile, TileState>? OnTileHit;      // Tile was hit (Perfect/Good)
    public event Action<Tile>? OnTileMissed;              // Tile was missed
    public event Action<int>? OnComboChanged;             // Combo count changed
    public event Action<int>? OnCountdownTick;            // 3, 2, 1 countdown
    public event Action? OnGameOver;                      // Song finished
    public event Action? OnGameStarted;                   // Gameplay begins
    public event Action<int>? OnWrongHit;                 // Wrong lane tapped (lane index)

    // ─── Latest feedback for UI ────────────────────────────────────
    public TileState? LastHitQuality { get; private set; }
    public double LastHitTime { get; private set; }
    public int LastHitLane { get; private set; } = -1;

    // ─── Countdown ─────────────────────────────────────────────────
    private int _lastCountdownValue = 4;

    /// <summary>
    /// Sets the lane count for the next game. Clamped to [MinLanes, MaxLanes].
    /// </summary>
    public void SetLaneCount(int lanes)
    {
        LaneCount = Math.Clamp(lanes, MinLanes, MaxLanes);
    }

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
        WrongHitCount = 0;
        LastHitQuality = null;
        LastHitLane = -1;
        _lastCountdownValue = 4;
        IsWaitingForInput = false;
        WaitingLane = -1;

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
        // In Easy Mode, if we're waiting for input, don't advance anything
        if (EasyMode && IsWaitingForInput)
        {
            return;
        }

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

                    // ─── Easy Mode: freeze tile at hit zone ───────────────
                    if (EasyMode)
                    {
                        if (tile.YPosition >= HitZoneY)
                        {
                            tile.YPosition = HitZoneY; // clamp to hit zone
                            // Pause the timer and wait for input
                            _gameTimer.Stop();
                            IsWaitingForInput = true;
                            WaitingLane = tile.Lane;
                            return; // Stop processing — game is now frozen
                        }
                    }
                    else
                    {
                        // Normal mode: check if tile has passed the miss window
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
    /// In Normal mode, wrong taps (no tile in lane) apply a penalty.
    /// In Easy Mode, wrong taps apply a penalty but the game stays paused.
    /// </summary>
    public HitResult TryHit(int lane)
    {
        if (State != GameState.Playing)
            return new HitResult(false, TileState.Active, null);

        // ─── Easy Mode hit handling ───────────────────────────────
        if (EasyMode && IsWaitingForInput)
        {
            lock (_tileLock)
            {
                // Find the tile that is waiting at the hit zone
                Tile? waitingTile = null;
                foreach (var tile in ActiveTiles)
                {
                    if (tile.State == TileState.Active && tile.YPosition >= HitZoneY - 0.01)
                    {
                        waitingTile = tile;
                        break;
                    }
                }

                if (waitingTile == null)
                    return new HitResult(false, TileState.Active, null);

                if (lane == waitingTile.Lane)
                {
                    // Correct lane — always Perfect in Easy Mode
                    waitingTile.State = TileState.HitPerfect;
                    waitingTile.AnimProgress = 0;
                    PerfectCount++;

                    Combo++;
                    if (Combo > MaxCombo) MaxCombo = Combo;
                    OnComboChanged?.Invoke(Combo);

                    double comboMultiplier = 1.0 + (Combo / 10.0) * 0.5;
                    Score += (int)(100 * comboMultiplier);

                    LastHitQuality = TileState.HitPerfect;
                    LastHitTime = _gameTimer.ElapsedMilliseconds;
                    LastHitLane = lane;

                    OnTileHit?.Invoke(waitingTile, TileState.HitPerfect);

                    // Resume the game
                    IsWaitingForInput = false;
                    WaitingLane = -1;
                    _gameTimer.Start();

                    return new HitResult(true, TileState.HitPerfect, waitingTile);
                }
                else
                {
                    // Wrong lane in Easy Mode — penalty but stay paused
                    ApplyWrongHitPenalty(lane);
                    return new HitResult(false, TileState.Missed, null);
                }
            }
        }

        // ─── Normal Mode hit handling ─────────────────────────────
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
                ApplyWrongHitPenalty(lane);
                return new HitResult(false, TileState.Missed, null);
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
    /// Applies the wrong-hit penalty: negative score, combo break, event notification.
    /// </summary>
    private void ApplyWrongHitPenalty(int lane)
    {
        WrongHitCount++;
        Score = Math.Max(0, Score - WrongHitPenalty);
        Combo = 0;
        OnComboChanged?.Invoke(Combo);
        OnWrongHit?.Invoke(lane);

        LastHitQuality = TileState.Missed;
        LastHitTime = EasyMode ? 0 : _gameTimer.ElapsedMilliseconds;
        LastHitLane = lane;
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
            // In easy mode, only start timer if we're not waiting for input
            if (!(EasyMode && IsWaitingForInput))
            {
                _gameTimer.Start();
            }
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
    /// Dynamically divides into the current LaneCount.
    /// </summary>
    public int GetLaneFromTouchX(float normalizedX)
    {
        int lane = (int)(normalizedX * LaneCount);
        return Math.Clamp(lane, 0, LaneCount - 1);
    }
}
