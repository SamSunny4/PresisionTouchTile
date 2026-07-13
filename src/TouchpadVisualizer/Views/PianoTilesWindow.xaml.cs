using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using TouchpadVisualizer.Game;
using TouchpadVisualizer.Input;
using TouchpadVisualizer.Models;

namespace TouchpadVisualizer.Views;

/// <summary>
/// Piano Tiles game window. Fullscreen WPF game with touchpad + keyboard input,
/// MIDI sound, and neon visual effects on a pitch-black background.
/// </summary>
public partial class PianoTilesWindow : Window
{
    // ─── Core Systems ──────────────────────────────────────────────
    private readonly PianoTilesGame _game = new();
    private readonly MidiPlayer _midi = new();
    private readonly TouchpadInputManager? _touchInput;
    private readonly List<SongPattern> _songs;
    private SongPattern? _selectedSong;

    // ─── Visual Element Pools ──────────────────────────────────────
    private readonly List<Border> _tileVisuals = new();
    private readonly List<Ellipse> _particles = new();
    private readonly List<ParticleState> _particleStates = new();
    private readonly Border[] _laneFlashes = new Border[4];
    private readonly List<Ellipse> _bgParticles = new();
    private readonly List<BgParticleState> _bgParticleStates = new();

    // ─── Rendering ─────────────────────────────────────────────────
    private double _screenWidth;
    private double _screenHeight;
    private double _laneWidth;
    private bool _isRunning;
    private readonly Stopwatch _renderTimer = Stopwatch.StartNew();
    private double _lastFrameTime;

    // ─── Hit feedback animation ────────────────────────────────────
    private double _hitFeedbackTimer;
    private string _hitFeedbackMessage = "";
    private Color _hitFeedbackColor;

    // ─── Random for particles ──────────────────────────────────────
    private readonly Random _rng = new();

    // ─── Neon colors for lanes ─────────────────────────────────────
    private static readonly Color[] LaneColors =
    [
        Color.FromRgb(0, 245, 255),   // Cyan
        Color.FromRgb(132, 94, 247),  // Purple
        Color.FromRgb(255, 107, 157), // Pink
        Color.FromRgb(57, 255, 20),   // Green
    ];

    // ─── Particle helper structs ───────────────────────────────────
    private record struct ParticleState(double X, double Y, double VX, double VY, double Life, double MaxLife, Color C);
    private record struct BgParticleState(double X, double Y, double VY, double Size, double Opacity);

    // ─── Track key states to prevent repeat ────────────────────────
    private readonly HashSet<Key> _heldKeys = new();

    public PianoTilesWindow(TouchpadInputManager? touchInput)
    {
        InitializeComponent();
        _touchInput = touchInput;
        _songs = SongPattern.GetAllSongs();
    }

    // ═══════════════════════════════════════════════════════════════
    //  INITIALIZATION
    // ═══════════════════════════════════════════════════════════════

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _screenWidth = ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth;
        _screenHeight = ActualHeight > 0 ? ActualHeight : SystemParameters.PrimaryScreenHeight;
        _laneWidth = _screenWidth / PianoTilesGame.LaneCount;

        // Open MIDI
        _midi.Open();

        // Setup lane separators
        SetupLaneSeparators();

        // Setup hit zone
        SetupHitZone();

        // Setup lane flash effects
        SetupLaneFlashes();

        // Setup background particles
        SetupBackgroundParticles();

        // Build song list menu
        BuildSongMenu();

        // Wire touchpad input
        if (_touchInput != null)
        {
            _touchInput.TouchDown += OnTouchDown;
        }

        // Wire game events
        _game.OnTileHit += OnTileHit;
        _game.OnTileMissed += OnTileMissed;
        _game.OnGameOver += OnGameOver;
        _game.OnCountdownTick += OnCountdownTick;
        _game.OnGameStarted += OnGameStarted;

        // Start render loop
        _isRunning = true;
        CompositionTarget.Rendering += OnRender;

        // Start hit zone pulse animation
        if (Resources["HitZonePulse"] is Storyboard pulse)
        {
            pulse.Begin();
        }
    }

    private void SetupLaneSeparators()
    {
        for (int i = 1; i < PianoTilesGame.LaneCount; i++)
        {
            var line = new Rectangle
            {
                Width = 1,
                Height = _screenHeight,
                Fill = new SolidColorBrush(Color.FromArgb(15, 132, 94, 247)),
            };
            Canvas.SetLeft(line, i * _laneWidth);
            Canvas.SetTop(line, 0);
            LaneSeparatorCanvas.Children.Add(line);
        }
    }

    private void SetupHitZone()
    {
        double hitY = _screenHeight * PianoTilesGame.HitZoneY;

        HitZoneGlow.Width = _screenWidth;
        Canvas.SetTop(HitZoneGlow, hitY);

        HitZoneArea.Width = _screenWidth;
        Canvas.SetTop(HitZoneArea, hitY - 80);
    }

    private void SetupLaneFlashes()
    {
        for (int i = 0; i < 4; i++)
        {
            var flash = new Border
            {
                Width = _laneWidth,
                Height = _screenHeight,
                Opacity = 0,
                Background = new LinearGradientBrush(
                    Color.FromArgb(0, LaneColors[i].R, LaneColors[i].G, LaneColors[i].B),
                    Color.FromArgb(30, LaneColors[i].R, LaneColors[i].G, LaneColors[i].B),
                    new Point(0, 0), new Point(0, 1)),
            };
            Canvas.SetLeft(flash, i * _laneWidth);
            Canvas.SetTop(flash, 0);
            LaneFlashCanvas.Children.Add(flash);
            _laneFlashes[i] = flash;
        }
    }

    private void SetupBackgroundParticles()
    {
        int count = 60;
        for (int i = 0; i < count; i++)
        {
            var size = _rng.NextDouble() * 3 + 1;
            var opacity = _rng.NextDouble() * 0.15 + 0.03;
            var particle = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 132, 94, 247)),
            };
            double x = _rng.NextDouble() * _screenWidth;
            double y = _rng.NextDouble() * _screenHeight;
            Canvas.SetLeft(particle, x);
            Canvas.SetTop(particle, y);
            BackgroundCanvas.Children.Add(particle);
            _bgParticles.Add(particle);
            _bgParticleStates.Add(new BgParticleState(x, y, -(_rng.NextDouble() * 20 + 5), size, opacity));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  SONG MENU
    // ═══════════════════════════════════════════════════════════════

    private void BuildSongMenu()
    {
        SongListPanel.Children.Clear();

        foreach (var song in _songs)
        {
            var btn = new Border
            {
                Padding = new Thickness(20, 14, 20, 14),
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(8, 132, 94, 247)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                IsHitTestVisible = true,
            };

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameStack = new StackPanel();
            nameStack.Children.Add(new TextBlock
            {
                Text = song.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 255)),
                FontFamily = (FontFamily)Resources["GameFont"],
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
            });
            nameStack.Children.Add(new TextBlock
            {
                Text = $"{song.Artist}  •  {song.Events.Count} notes",
                Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                FontFamily = (FontFamily)Resources["GameFont"],
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
            });
            Grid.SetColumn(nameStack, 0);
            content.Children.Add(nameStack);

            var diffColor = song.Difficulty switch
            {
                "Easy" => Color.FromRgb(57, 255, 20),
                "Medium" => Color.FromRgb(255, 200, 0),
                "Hard" => Color.FromRgb(255, 23, 68),
                _ => Color.FromRgb(150, 150, 150),
            };
            var diffText = new TextBlock
            {
                Text = song.Difficulty.ToUpper(),
                Foreground = new SolidColorBrush(diffColor),
                FontFamily = (FontFamily)Resources["GameFont"],
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            diffText.Effect = new DropShadowEffect
            {
                Color = diffColor,
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.4,
            };
            Grid.SetColumn(diffText, 1);
            content.Children.Add(diffText);

            btn.Child = content;

            // Hover effects
            var capturedSong = song;
            btn.MouseEnter += (s, e) =>
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(25, 132, 94, 247));
                btn.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 132, 94, 247));
            };
            btn.MouseLeave += (s, e) =>
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));
                btn.BorderBrush = new SolidColorBrush(Color.FromArgb(8, 132, 94, 247));
            };
            btn.MouseLeftButtonDown += (s, e) => StartGame(capturedSong);

            SongListPanel.Children.Add(btn);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  GAME CONTROL
    // ═══════════════════════════════════════════════════════════════

    private void StartGame(SongPattern song)
    {
        _selectedSong = song;
        _game.StartSong(song);

        // Update UI
        MenuOverlay.Visibility = Visibility.Collapsed;
        GameOverOverlay.Visibility = Visibility.Collapsed;
        PauseOverlay.Visibility = Visibility.Collapsed;
        HudGrid.Visibility = Visibility.Visible;

        SongNameText.Text = song.Name;
        SongArtistText.Text = song.Artist;
        ScoreText.Text = "0";
        ComboText.Text = "";

        // Clear old tiles
        TileCanvas.Children.Clear();
        _tileVisuals.Clear();
        ParticleCanvas.Children.Clear();
        _particles.Clear();
        _particleStates.Clear();
    }

    // ═══════════════════════════════════════════════════════════════
    //  INPUT HANDLING
    // ═══════════════════════════════════════════════════════════════

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Prevent key repeat from firing multiple hits
        if (_heldKeys.Contains(e.Key)) return;
        _heldKeys.Add(e.Key);

        switch (e.Key)
        {
            case Key.Escape:
                if (_game.State == PianoTilesGame.GameState.Playing ||
                    _game.State == PianoTilesGame.GameState.Paused)
                {
                    _game.Pause();
                    _midi.AllNotesOff();
                    ShowMenu();
                }
                else
                {
                    Close();
                }
                break;

            case Key.P:
                if (_game.State == PianoTilesGame.GameState.Playing)
                {
                    _game.Pause();
                    _midi.AllNotesOff();
                    PauseOverlay.Visibility = Visibility.Visible;
                }
                else if (_game.State == PianoTilesGame.GameState.Paused)
                {
                    _game.Resume();
                    PauseOverlay.Visibility = Visibility.Collapsed;
                }
                break;

            // Lane keys: D F J K
            case Key.D: HandleLaneInput(0); break;
            case Key.F: HandleLaneInput(1); break;
            case Key.J: HandleLaneInput(2); break;
            case Key.K: HandleLaneInput(3); break;
        }

        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _heldKeys.Remove(e.Key);
        base.OnKeyUp(e);
    }

    private void OnTouchDown(object? sender, TouchContact contact)
    {
        if (_game.State == PianoTilesGame.GameState.Menu)
        {
            // In menu, don't process game input
            return;
        }

        int lane = PianoTilesGame.GetLaneFromTouchX(contact.NormalizedX);
        Dispatcher.BeginInvoke(() => HandleLaneInput(lane));
    }

    private void HandleLaneInput(int lane)
    {
        if (_game.State != PianoTilesGame.GameState.Playing)
            return;

        // Flash the lane
        FlashLane(lane);

        var result = _game.TryHit(lane);

        if (result.IsHit && result.Tile != null)
        {
            // Play the note
            byte velocity = result.Quality == TileState.HitPerfect ? (byte)110 : (byte)85;
            _midi.PlayNote(result.Tile.MidiNote, velocity, 400);

            // Visual feedback
            if (result.Quality == TileState.HitPerfect)
            {
                ShowHitFeedback("PERFECT", Color.FromRgb(0, 245, 255));
                SpawnHitParticles(lane, Color.FromRgb(0, 245, 255));
            }
            else
            {
                ShowHitFeedback("GOOD", Color.FromRgb(132, 94, 247));
                SpawnHitParticles(lane, Color.FromRgb(132, 94, 247));
            }

            // Update score
            ScoreText.Text = _game.Score.ToString();
            if (_game.Combo > 1)
                ComboText.Text = $"COMBO ×{_game.Combo}";
            else
                ComboText.Text = "";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  GAME EVENTS
    // ═══════════════════════════════════════════════════════════════

    private void OnTileHit(Tile tile, TileState quality)
    {
        // Already handled in HandleLaneInput
    }

    private void OnTileMissed(Tile tile)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShowHitFeedback("MISS", Color.FromRgb(255, 23, 68));
            ComboText.Text = "";

            // Red flash on the missed lane
            FlashLane(tile.Lane, Color.FromArgb(40, 255, 23, 68));
        });
    }

    private void OnCountdownTick(int count)
    {
        Dispatcher.BeginInvoke(() =>
        {
            CountdownText.Text = count > 0 ? count.ToString() : "GO!";
            CountdownText.Opacity = 1;

            // Animate countdown
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(800))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            CountdownText.BeginAnimation(OpacityProperty, fadeOut);
        });
    }

    private void OnGameStarted()
    {
        Dispatcher.BeginInvoke(() =>
        {
            CountdownText.Opacity = 0;
        });
    }

    private void OnGameOver()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _midi.AllNotesOff();
            ShowGameOverScreen();
        });
    }

    private void ShowGameOverScreen()
    {
        GameOverOverlay.Visibility = Visibility.Visible;
        FinalScoreText.Text = _game.Score.ToString();
        PerfectCountText.Text = _game.PerfectCount.ToString();
        GoodCountText.Text = _game.GoodCount.ToString();
        MissCountText.Text = _game.MissCount.ToString();
        MaxComboText.Text = $"Max Combo: {_game.MaxCombo}";
    }

    private void ShowMenu()
    {
        MenuOverlay.Visibility = Visibility.Visible;
        GameOverOverlay.Visibility = Visibility.Collapsed;
        PauseOverlay.Visibility = Visibility.Collapsed;
        TileCanvas.Children.Clear();
        _tileVisuals.Clear();
    }

    private void Replay_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSong != null)
            StartGame(_selectedSong);
    }

    private void BackToMenu_Click(object sender, RoutedEventArgs e)
    {
        ShowMenu();
    }

    // ═══════════════════════════════════════════════════════════════
    //  RENDER LOOP
    // ═══════════════════════════════════════════════════════════════

    private void OnRender(object? sender, EventArgs e)
    {
        if (!_isRunning) return;

        double now = _renderTimer.Elapsed.TotalSeconds;
        double delta = now - _lastFrameTime;
        _lastFrameTime = now;

        if (delta <= 0 || delta > 0.1) delta = 0.016; // clamp

        // Update game state
        _game.Update(delta * 1000);

        // Update visuals
        UpdateTileVisuals();
        UpdateParticles(delta);
        UpdateBackgroundParticles(delta);
        UpdateLaneFlashes(delta);
        UpdateHitFeedback(delta);
    }

    // ═══════════════════════════════════════════════════════════════
    //  TILE RENDERING
    // ═══════════════════════════════════════════════════════════════

    private void UpdateTileVisuals()
    {
        if (_game.State != PianoTilesGame.GameState.Playing &&
            _game.State != PianoTilesGame.GameState.Countdown)
            return;

        var tiles = _game.GetTileSnapshot();

        // Ensure we have enough visual elements
        while (_tileVisuals.Count < tiles.Count)
        {
            var visual = CreateTileVisual();
            TileCanvas.Children.Add(visual);
            _tileVisuals.Add(visual);
        }

        // Hide excess visuals
        for (int i = tiles.Count; i < _tileVisuals.Count; i++)
        {
            _tileVisuals[i].Visibility = Visibility.Collapsed;
        }

        // Update visible tile positions and styles
        for (int i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            var visual = _tileVisuals[i];
            visual.Visibility = Visibility.Visible;

            double tilePixelHeight = _screenHeight * PianoTilesGame.TileHeight;
            double x = tile.Lane * _laneWidth + 2;
            double y = tile.YPosition * _screenHeight - tilePixelHeight;

            Canvas.SetLeft(visual, x);
            Canvas.SetTop(visual, y);
            visual.Width = _laneWidth - 4;
            visual.Height = tilePixelHeight;

            // Style based on state
            var bgBrush = (SolidColorBrush)visual.Background;
            var borderBrush = (SolidColorBrush)visual.BorderBrush;
            var dsEffect = (DropShadowEffect)visual.Effect;

            switch (tile.State)
            {
                case TileState.Active:
                    var laneColor = LaneColors[tile.Lane];
                    bgBrush.Color = Color.FromRgb(18, 18, 28);
                    borderBrush.Color = Color.FromArgb(60, laneColor.R, laneColor.G, laneColor.B);
                    visual.Opacity = 1;
                    dsEffect.Color = laneColor;
                    dsEffect.BlurRadius = 12;
                    dsEffect.Opacity = 0.3;
                    break;

                case TileState.HitPerfect:
                    bgBrush.Color = Color.FromArgb((byte)(255 * (1 - tile.AnimProgress)), 0, 245, 255);
                    borderBrush.Color = Color.FromArgb((byte)(100 * (1 - tile.AnimProgress)), 0, 245, 255);
                    visual.Opacity = 1 - tile.AnimProgress;
                    dsEffect.Color = Color.FromRgb(0, 245, 255);
                    dsEffect.BlurRadius = 30 + tile.AnimProgress * 20;
                    dsEffect.Opacity = 0.8 * (1 - tile.AnimProgress);
                    break;

                case TileState.HitGood:
                    bgBrush.Color = Color.FromArgb((byte)(255 * (1 - tile.AnimProgress)), 132, 94, 247);
                    borderBrush.Color = Color.FromArgb((byte)(80 * (1 - tile.AnimProgress)), 132, 94, 247);
                    visual.Opacity = 1 - tile.AnimProgress;
                    dsEffect.Color = Color.FromRgb(132, 94, 247);
                    dsEffect.BlurRadius = 20 + tile.AnimProgress * 15;
                    dsEffect.Opacity = 0.6 * (1 - tile.AnimProgress);
                    break;

                case TileState.Missed:
                    bgBrush.Color = Color.FromArgb((byte)(180 * (1 - tile.AnimProgress)), 255, 23, 68);
                    borderBrush.Color = Color.FromArgb((byte)(60 * (1 - tile.AnimProgress)), 255, 23, 68);
                    visual.Opacity = 1 - tile.AnimProgress;
                    dsEffect.Opacity = 0;
                    break;
            }
        }
    }

    private Border CreateTileVisual()
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(18, 18, 28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 132, 94, 247)),
            Effect = new DropShadowEffect { ShadowDepth = 0 }
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  PARTICLE EFFECTS
    // ═══════════════════════════════════════════════════════════════

    private void SpawnHitParticles(int lane, Color color)
    {
        double hitY = _screenHeight * PianoTilesGame.HitZoneY;
        double centerX = (lane + 0.5) * _laneWidth;
        int count = 16;

        for (int i = 0; i < count; i++)
        {
            double angle = _rng.NextDouble() * Math.PI * 2;
            double speed = _rng.NextDouble() * 300 + 100;
            double size = _rng.NextDouble() * 6 + 2;

            var particle = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(color),
            };
            particle.Effect = new DropShadowEffect
            {
                Color = color,
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.6,
            };

            Canvas.SetLeft(particle, centerX);
            Canvas.SetTop(particle, hitY);
            ParticleCanvas.Children.Add(particle);

            _particles.Add(particle);
            _particleStates.Add(new ParticleState(
                centerX, hitY,
                Math.Cos(angle) * speed,
                Math.Sin(angle) * speed - 100,
                0, 0.6 + _rng.NextDouble() * 0.3,
                color));
        }
    }

    private void UpdateParticles(double delta)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var state = _particleStates[i];
            double newX = state.X + state.VX * delta;
            double newY = state.Y + state.VY * delta;
            double newVY = state.VY + 400 * delta; // gravity
            double newLife = state.Life + delta;

            if (newLife >= state.MaxLife)
            {
                ParticleCanvas.Children.Remove(_particles[i]);
                _particles.RemoveAt(i);
                _particleStates.RemoveAt(i);
                continue;
            }

            double lifeRatio = 1 - (newLife / state.MaxLife);
            _particles[i].Opacity = lifeRatio;
            Canvas.SetLeft(_particles[i], newX);
            Canvas.SetTop(_particles[i], newY);

            _particleStates[i] = state with { X = newX, Y = newY, VY = newVY, Life = newLife };
        }
    }

    private void UpdateBackgroundParticles(double delta)
    {
        for (int i = 0; i < _bgParticles.Count; i++)
        {
            var state = _bgParticleStates[i];
            double newY = state.Y + state.VY * delta;

            // Wrap around
            if (newY < -10)
            {
                newY = _screenHeight + 10;
                state = state with { X = _rng.NextDouble() * _screenWidth };
                Canvas.SetLeft(_bgParticles[i], state.X);
            }

            Canvas.SetTop(_bgParticles[i], newY);
            _bgParticleStates[i] = state with { Y = newY };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  VISUAL FEEDBACK
    // ═══════════════════════════════════════════════════════════════

    private void FlashLane(int lane, Color? overrideColor = null)
    {
        if (lane < 0 || lane >= 4) return;

        var flash = _laneFlashes[lane];
        var color = overrideColor ?? Color.FromArgb(20, LaneColors[lane].R, LaneColors[lane].G, LaneColors[lane].B);
        flash.Background = new SolidColorBrush(color);
        flash.Opacity = 1;
    }

    private void UpdateLaneFlashes(double delta)
    {
        for (int i = 0; i < 4; i++)
        {
            if (_laneFlashes[i].Opacity > 0)
            {
                _laneFlashes[i].Opacity = Math.Max(0, _laneFlashes[i].Opacity - delta * 4);
            }
        }
    }

    private void ShowHitFeedback(string text, Color color)
    {
        _hitFeedbackMessage = text;
        _hitFeedbackColor = color;
        _hitFeedbackTimer = 0.6;

        HitFeedbackText.Text = text;
        HitFeedbackText.Foreground = new SolidColorBrush(color);
        HitFeedbackText.Opacity = 1;

        if (HitFeedbackText.Effect is DropShadowEffect effect)
        {
            effect.Color = color;
        }

        // Position above hit zone
        HitFeedbackText.Margin = new Thickness(0, 0, 0, _screenHeight * (1 - PianoTilesGame.HitZoneY) + 40);
    }

    private void UpdateHitFeedback(double delta)
    {
        if (_hitFeedbackTimer > 0)
        {
            _hitFeedbackTimer -= delta;
            double progress = Math.Max(0, _hitFeedbackTimer / 0.6);
            HitFeedbackText.Opacity = progress;

            // Scale up slightly as it fades
            double scale = 1 + (1 - progress) * 0.3;
            if (!(HitFeedbackText.RenderTransform is ScaleTransform st))
            {
                st = new ScaleTransform(scale, scale);
                HitFeedbackText.RenderTransform = st;
                HitFeedbackText.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            else
            {
                st.ScaleX = scale;
                st.ScaleY = scale;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  CLEANUP
    // ═══════════════════════════════════════════════════════════════

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _isRunning = false;
        CompositionTarget.Rendering -= OnRender;

        if (_touchInput != null)
        {
            _touchInput.TouchDown -= OnTouchDown;
        }

        _midi.AllNotesOff();
        _midi.Dispose();
    }
}
