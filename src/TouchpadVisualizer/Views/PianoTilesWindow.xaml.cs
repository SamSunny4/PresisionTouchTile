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
/// Supports configurable lane count (3–6), Easy Mode, and wrong-input penalties.
/// 
/// Performance notes:
/// - DropShadowEffect removed from particles (software-rendered, massive FPS cost)
/// - Tile DropShadowEffect cached and only updated when state changes
/// - Brush objects are reused instead of allocating new ones every frame
/// - BitmapCache applied to canvases for GPU-accelerated composition
/// - Background particles use shared frozen brush
/// - Particle removal uses index-based swap-remove pattern
/// </summary>
public partial class PianoTilesWindow : Window
{
    // ─── Core Systems ──────────────────────────────────────────────
    private readonly PianoTilesGame _game = new();
    private readonly MidiPlayer _midi = new();
    private readonly TouchpadInputManager? _touchInput;
    private readonly List<SongPattern> _songs;
    private SongPattern? _selectedSong;

    // ─── Settings State ────────────────────────────────────────────
    private int _selectedLaneCount = PianoTilesGame.DefaultLaneCount;
    private bool _easyModeEnabled = false;
    private bool _autopilotEnabled = false;
    private readonly Border[] _laneCountButtons = new Border[PianoTilesGame.MaxLanes - PianoTilesGame.MinLanes + 1];
    private int _menuSelectedSongIndex = 0;
    private readonly List<Border> _songMenuBorders = new();

    // ─── Visual Element Pools ──────────────────────────────────────
    private readonly List<TileVisual> _tileVisuals = new();
    private readonly List<Ellipse> _particles = new();
    private readonly List<ParticleState> _particleStates = new();
    private Border[] _laneFlashes = new Border[PianoTilesGame.DefaultLaneCount];
    private readonly SolidColorBrush[] _laneFlashBrushes = new SolidColorBrush[PianoTilesGame.MaxLanes];
    private readonly List<Ellipse> _bgParticles = new();
    private readonly List<BgParticleState> _bgParticleStates = new();

    // ─── Reusable brushes (avoid GC pressure) ──────────────────────
    private readonly SolidColorBrush _hitFeedbackBrush = new(Colors.White);
    private static readonly SolidColorBrush FrozenBgParticleBrush;

    // ─── Rendering ─────────────────────────────────────────────────
    private double _screenWidth;
    private double _screenHeight;
    private double _laneWidth;
    private bool _isRunning;
    private readonly Stopwatch _renderTimer = Stopwatch.StartNew();
    private double _lastFrameTime;

    // ─── Hit feedback animation ────────────────────────────────────
    private double _hitFeedbackTimer;

    // ─── Easy Mode waiting indicator pulse ─────────────────────────
    private double _waitingPulseTimer;

    // ─── Random for particles ──────────────────────────────────────
    private readonly Random _rng = new();

    // ─── Neon colors for lanes (supports up to 6 lanes) ────────────
    private static readonly Color[] LaneColors =
    [
        Color.FromRgb(0, 245, 255),   // Cyan
        Color.FromRgb(132, 94, 247),  // Purple
        Color.FromRgb(255, 107, 157), // Pink
        Color.FromRgb(57, 255, 20),   // Green
        Color.FromRgb(255, 107, 0),   // Orange
        Color.FromRgb(255, 214, 0),   // Yellow
    ];

    // ─── Pre-frozen brushes for particles (one per lane color) ─────
    private static readonly SolidColorBrush[] FrozenParticleBrushes;

    // ─── Keyboard mappings per lane count ──────────────────────────
    private static readonly Dictionary<int, Key[]> LaneKeyMappings = new()
    {
        [3] = [Key.D, Key.Space, Key.K],
        [4] = [Key.D, Key.F, Key.J, Key.K],
        [5] = [Key.D, Key.F, Key.Space, Key.J, Key.K],
        [6] = [Key.S, Key.D, Key.F, Key.J, Key.K, Key.L],
    };

    private static readonly Dictionary<int, string[]> LaneKeyLabels = new()
    {
        [3] = ["D", "SPACE", "K"],
        [4] = ["D", "F", "J", "K"],
        [5] = ["D", "F", "SPACE", "J", "K"],
        [6] = ["S", "D", "F", "J", "K", "L"],
    };

    // ─── Tile visual wrapper (caches brush references) ─────────────
    private class TileVisual
    {
        public Border Element;
        public SolidColorBrush BgBrush;
        public SolidColorBrush BorderBrush;
        public DropShadowEffect? Shadow;
        public TileState LastState = (TileState)(-1); // force first update

        public TileVisual(Border element, SolidColorBrush bgBrush, SolidColorBrush borderBrush, DropShadowEffect? shadow)
        {
            Element = element;
            BgBrush = bgBrush;
            BorderBrush = borderBrush;
            Shadow = shadow;
        }
    }

    // ─── Particle helper structs ───────────────────────────────────
    private record struct ParticleState(double X, double Y, double VX, double VY, double Life, double MaxLife, Color C);
    private record struct BgParticleState(double X, double Y, double VY, double Size, double Opacity);

    // ─── Track key states to prevent repeat ────────────────────────
    private readonly HashSet<Key> _heldKeys = new();

    static PianoTilesWindow()
    {
        // Pre-create and freeze particle brushes for each lane color
        FrozenParticleBrushes = new SolidColorBrush[LaneColors.Length];
        for (int i = 0; i < LaneColors.Length; i++)
        {
            var brush = new SolidColorBrush(LaneColors[i]);
            brush.Freeze();
            FrozenParticleBrushes[i] = brush;
        }

        // Frozen brush for background particles
        FrozenBgParticleBrush = new SolidColorBrush(Color.FromArgb(20, 132, 94, 247));
        FrozenBgParticleBrush.Freeze();
    }

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
        _laneWidth = _screenWidth / _selectedLaneCount;

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

        // Build settings UI
        BuildLaneCountSelector();
        UpdateEasyModeToggleVisual();
        UpdateKeyLabels();
        UpdateControlsHint();

        // Enable BitmapCache on game canvases for GPU-accelerated compositing
        TileCanvas.CacheMode = new BitmapCache { RenderAtScale = 1.0 };
        ParticleCanvas.CacheMode = new BitmapCache { RenderAtScale = 1.0 };
        // Background particles are nearly static — cache aggressively
        BackgroundCanvas.CacheMode = new BitmapCache { RenderAtScale = 0.5 };

        // Wire touchpad input
        if (_touchInput != null)
        {
            _touchInput.TouchDown += OnTouchDown;
            _touchInput.TouchUp += OnTouchUp;
        }

        // Wire game events
        _game.OnTileHit += OnTileHit;
        _game.OnTileMissed += OnTileMissed;
        _game.OnGameOver += OnGameOver;
        _game.OnCountdownTick += OnCountdownTick;
        _game.OnGameStarted += OnGameStarted;
        _game.OnWrongHit += OnWrongHit;

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
        LaneSeparatorCanvas.Children.Clear();
        var separatorBrush = new SolidColorBrush(Color.FromArgb(15, 132, 94, 247));
        separatorBrush.Freeze();

        for (int i = 1; i < _selectedLaneCount; i++)
        {
            var line = new Rectangle
            {
                Width = 1,
                Height = _screenHeight,
                Fill = separatorBrush,
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
        LaneFlashCanvas.Children.Clear();
        _laneFlashes = new Border[_selectedLaneCount];
        for (int i = 0; i < _selectedLaneCount; i++)
        {
            var laneColor = LaneColors[i % LaneColors.Length];
            // Create a reusable brush for this lane flash
            var flashBrush = new SolidColorBrush(Color.FromArgb(20, laneColor.R, laneColor.G, laneColor.B));
            _laneFlashBrushes[i] = flashBrush;

            var flash = new Border
            {
                Width = _laneWidth,
                Height = _screenHeight,
                Opacity = 0,
                Background = flashBrush,
            };
            Canvas.SetLeft(flash, i * _laneWidth);
            Canvas.SetTop(flash, 0);
            LaneFlashCanvas.Children.Add(flash);
            _laneFlashes[i] = flash;
        }
    }

    private void SetupBackgroundParticles()
    {
        // Clear existing
        foreach (var p in _bgParticles)
        {
            BackgroundCanvas.Children.Remove(p);
        }
        _bgParticles.Clear();
        _bgParticleStates.Clear();

        int count = 40; // Reduced from 60 — barely visible, saves per-frame updates
        for (int i = 0; i < count; i++)
        {
            var size = _rng.NextDouble() * 3 + 1;
            var opacity = _rng.NextDouble() * 0.15 + 0.03;
            var particle = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = FrozenBgParticleBrush, // Shared frozen brush
                Opacity = opacity,
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

    /// <summary>
    /// Rebuilds lane-dependent visuals when lane count changes.
    /// </summary>
    private void RebuildLaneVisuals()
    {
        _laneWidth = _screenWidth / _selectedLaneCount;
        SetupLaneSeparators();
        SetupLaneFlashes();
        UpdateKeyLabels();
        UpdateControlsHint();
    }

    // ═══════════════════════════════════════════════════════════════
    //  SETTINGS UI
    // ═══════════════════════════════════════════════════════════════

    private void BuildLaneCountSelector()
    {
        LaneCountPanel.Children.Clear();
        for (int i = PianoTilesGame.MinLanes; i <= PianoTilesGame.MaxLanes; i++)
        {
            int lanes = i;
            var isSelected = lanes == _selectedLaneCount;
            var btn = new Border
            {
                Width = 52,
                Height = 36,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(4, 0, 4, 0),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(isSelected
                    ? Color.FromArgb(40, 132, 94, 247)
                    : Color.FromArgb(10, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(isSelected
                    ? Color.FromArgb(100, 132, 94, 247)
                    : Color.FromArgb(15, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = lanes.ToString(),
                    Foreground = new SolidColorBrush(isSelected
                        ? Color.FromRgb(132, 94, 247)
                        : Color.FromArgb(120, 255, 255, 255)),
                    FontFamily = (FontFamily)Resources["GameFont"],
                    FontSize = 16,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            btn.MouseLeftButtonDown += (s, e) =>
            {
                _selectedLaneCount = lanes;
                BuildLaneCountSelector(); // Rebuild to update selection
                RebuildLaneVisuals();
            };

            btn.MouseEnter += (s, e) =>
            {
                if (lanes != _selectedLaneCount)
                {
                    btn.Background = new SolidColorBrush(Color.FromArgb(20, 132, 94, 247));
                }
            };
            btn.MouseLeave += (s, e) =>
            {
                if (lanes != _selectedLaneCount)
                {
                    btn.Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255));
                }
            };

            int idx = i - PianoTilesGame.MinLanes;
            _laneCountButtons[idx] = btn;
            LaneCountPanel.Children.Add(btn);
        }
    }

    private void EasyModeToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _easyModeEnabled = !_easyModeEnabled;
        UpdateEasyModeToggleVisual();
        UpdateControlsHint();
    }

    private void UpdateEasyModeToggleVisual()
    {
        string modeText = $"{_selectedLaneCount} LANES";
        if (_autopilotEnabled) modeText = "AUTOPILOT  •  " + modeText;
        else if (_easyModeEnabled) modeText = "EASY MODE  •  " + modeText;
        ModeIndicatorText.Text = modeText;

        if (_easyModeEnabled || _autopilotEnabled)
        {
            EasyModeToggle.Background = new SolidColorBrush(Color.FromArgb(25, 57, 255, 20));
            EasyModeToggle.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 57, 255, 20));
            EasyModeIcon.Text = "●";
            EasyModeIcon.Foreground = new SolidColorBrush(Color.FromRgb(57, 255, 20));
        }
        else
        {
            EasyModeToggle.Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255));
            EasyModeToggle.BorderBrush = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
            EasyModeIcon.Text = "○";
            EasyModeIcon.Foreground = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
        }
    }

    private void UpdateKeyLabels()
    {
        KeyLabelsPanel.Children.Clear();
        if (!LaneKeyLabels.TryGetValue(_selectedLaneCount, out var labels)) return;

        foreach (var label in labels)
        {
            KeyLabelsPanel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
                FontFamily = (FontFamily)Resources["GameFont"],
                FontSize = 16,
                FontWeight = FontWeights.Light,
                Width = _laneWidth,
                TextAlignment = TextAlignment.Center,
            });
        }
    }

    private void UpdateControlsHint()
    {
        if (!LaneKeyLabels.TryGetValue(_selectedLaneCount, out var labels)) return;
        var keyStr = string.Join("  ", labels);
        ControlsHintText.Inlines.Clear();
        ControlsHintText.Inlines.Add(new System.Windows.Documents.Run("Touchpad: tap zones for lanes"));
        ControlsHintText.Inlines.Add(new System.Windows.Documents.LineBreak());
        ControlsHintText.Inlines.Add(new System.Windows.Documents.Run($"Keyboard: {keyStr}"));
        if (_easyModeEnabled)
        {
            ControlsHintText.Inlines.Add(new System.Windows.Documents.LineBreak());
            ControlsHintText.Inlines.Add(new System.Windows.Documents.Run("Easy Mode: tiles wait for your input") { Foreground = new SolidColorBrush(Color.FromRgb(57, 255, 20)) });
        }
        ControlsHintText.Inlines.Add(new System.Windows.Documents.LineBreak());
        var menuHint = new System.Windows.Documents.Run("Menu: [↑/↓] Select, [Enter] Play, [3-6] Lanes, [E] Easy Mode, [A] Autopilot");
        menuHint.Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
        ControlsHintText.Inlines.Add(menuHint);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SONG MENU
    // ═══════════════════════════════════════════════════════════════

    private void BuildSongMenu()
    {
        SongListPanel.Children.Clear();
        _songMenuBorders.Clear();

        for (int i = 0; i < _songs.Count; i++)
        {
            var song = _songs[i];
            int index = i;
            var btn = new Border
            {
                Padding = new Thickness(20, 14, 20, 14),
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(10),
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

            // Mouse hover updates active keyboard selection
            btn.MouseEnter += (s, e) =>
            {
                _menuSelectedSongIndex = index;
                UpdateSongMenuSelection();
            };
            btn.MouseLeftButtonDown += (s, e) => StartGame(song);

            _songMenuBorders.Add(btn);
            SongListPanel.Children.Add(btn);
        }

        UpdateSongMenuSelection();
    }

    private void UpdateSongMenuSelection()
    {
        for (int i = 0; i < _songMenuBorders.Count; i++)
        {
            var btn = _songMenuBorders[i];
            bool isSelected = i == _menuSelectedSongIndex;
            btn.Background = new SolidColorBrush(isSelected
                ? Color.FromArgb(40, 132, 94, 247)
                : Color.FromArgb(12, 255, 255, 255));
            btn.BorderBrush = new SolidColorBrush(isSelected
                ? Color.FromArgb(100, 132, 94, 247)
                : Color.FromArgb(8, 132, 94, 247));
        }
    }

    private void ChangeSelectedLanes(int lanes)
    {
        _selectedLaneCount = lanes;
        BuildLaneCountSelector();
        RebuildLaneVisuals();
    }

    // ═══════════════════════════════════════════════════════════════
    //  GAME CONTROL
    // ═══════════════════════════════════════════════════════════════

    private void StartGame(SongPattern song)
    {
        // Apply settings
        _game.SetLaneCount(_selectedLaneCount);
        _game.EasyMode = _easyModeEnabled;
        _game.Autopilot = _autopilotEnabled;

        // Remap song lanes if needed
        var remappedSong = song.RemapToLanes(_selectedLaneCount);

        _selectedSong = song; // keep original for replay
        _game.StartSong(remappedSong);

        // Rebuild lane visuals for the selected lane count
        RebuildLaneVisuals();

        // Update UI
        MenuOverlay.Visibility = Visibility.Collapsed;
        GameOverOverlay.Visibility = Visibility.Collapsed;
        PauseOverlay.Visibility = Visibility.Collapsed;
        HudGrid.Visibility = Visibility.Visible;

        SongNameText.Text = song.Name;
        SongArtistText.Text = song.Artist;
        string modeText = $"{_selectedLaneCount} LANES";
        if (_autopilotEnabled) modeText = "AUTOPILOT  •  " + modeText;
        else if (_easyModeEnabled) modeText = "EASY MODE  •  " + modeText;
        ModeIndicatorText.Text = modeText;
        ScoreText.Text = "0";
        ComboText.Text = "";
        WaitingIndicator.Opacity = 0;

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

        // Menu key navigation
        if (_game.State == PianoTilesGame.GameState.Menu)
        {
            switch (e.Key)
            {
                case Key.Up:
                    _menuSelectedSongIndex = (_menuSelectedSongIndex - 1 + _songs.Count) % _songs.Count;
                    UpdateSongMenuSelection();
                    e.Handled = true;
                    return;

                case Key.Down:
                    _menuSelectedSongIndex = (_menuSelectedSongIndex + 1) % _songs.Count;
                    UpdateSongMenuSelection();
                    e.Handled = true;
                    return;

                case Key.Enter:
                case Key.Space:
                    StartGame(_songs[_menuSelectedSongIndex]);
                    e.Handled = true;
                    return;

                case Key.D3:
                case Key.NumPad3:
                    ChangeSelectedLanes(3);
                    e.Handled = true;
                    return;

                case Key.D4:
                case Key.NumPad4:
                    ChangeSelectedLanes(4);
                    e.Handled = true;
                    return;

                case Key.D5:
                case Key.NumPad5:
                    ChangeSelectedLanes(5);
                    e.Handled = true;
                    return;

                case Key.D6:
                case Key.NumPad6:
                    ChangeSelectedLanes(6);
                    e.Handled = true;
                    return;

                case Key.E:
                    _easyModeEnabled = !_easyModeEnabled;
                    if (_easyModeEnabled) _autopilotEnabled = false; // Mutually exclusive
                    UpdateEasyModeToggleVisual();
                    UpdateControlsHint();
                    if (_game != null) _game.Autopilot = _autopilotEnabled;
                    e.Handled = true;
                    return;

                case Key.A:
                    _autopilotEnabled = !_autopilotEnabled;
                    if (_autopilotEnabled) _easyModeEnabled = false; // Mutually exclusive
                    UpdateEasyModeToggleVisual();
                    UpdateControlsHint();
                    if (_game != null) _game.Autopilot = _autopilotEnabled;
                    e.Handled = true;
                    return;

                case Key.Escape:
                    Close();
                    e.Handled = true;
                    return;
            }
        }

        // Game Over key navigation
        if (_game.State == PianoTilesGame.GameState.GameOver)
        {
            switch (e.Key)
            {
                case Key.Enter:
                case Key.R:
                    if (_selectedSong != null)
                        StartGame(_selectedSong);
                    e.Handled = true;
                    return;

                case Key.Escape:
                case Key.M:
                case Key.Back:
                    ShowMenu();
                    e.Handled = true;
                    return;
            }
        }

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

            default:
                // Check dynamic lane key mappings
                if (LaneKeyMappings.TryGetValue(_selectedLaneCount, out var keys))
                {
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (e.Key == keys[i])
                        {
                            _game.SetLaneHeld(i, true);
                            HandleLaneInput(i);
                            break;
                        }
                    }
                }
                break;
        }

        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _heldKeys.Remove(e.Key);

        if (LaneKeyMappings.TryGetValue(_selectedLaneCount, out var keys))
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (e.Key == keys[i])
                {
                    _game.SetLaneHeld(i, false);
                    break;
                }
            }
        }

        base.OnKeyUp(e);
    }

    private void OnTouchDown(object? sender, TouchContact contact)
    {
        if (_game.State == PianoTilesGame.GameState.Menu)
        {
            // In menu, don't process game input
            return;
        }

        int lane = _game.GetLaneFromTouchX(contact.NormalizedX);
        _game.SetLaneHeld(lane, true);
        Dispatcher.BeginInvoke(() => HandleLaneInput(lane));
    }

    private void OnTouchUp(object? sender, TouchContact contact)
    {
        if (_game.State != PianoTilesGame.GameState.Playing)
            return;

        int lane = _game.GetLaneFromTouchX(contact.NormalizedX);
        _game.SetLaneHeld(lane, false);
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
            if (result.Tile.IsHoldNote)
            {
                // Sustain note (holding)
                _midi.NoteOn(result.Tile.MidiNote, 100);
            }
            else
            {
                byte velocity = result.Quality == TileState.HitPerfect ? (byte)110 : (byte)85;
                _midi.PlayNote(result.Tile.MidiNote, velocity, 400);
            }

            // Visual feedback
            if (result.Quality == TileState.HitPerfect || result.Quality == TileState.Holding)
            {
                string text = result.Quality == TileState.Holding ? "HOLD" : "PERFECT";
                Color c = Color.FromRgb(0, 245, 255);
                ShowHitFeedback(text, c);
                SpawnHitParticles(lane, c);
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
        else if (!result.IsHit && result.Quality == TileState.Missed)
        {
            // Wrong input was already handled via OnWrongHit event, but update score display
            ScoreText.Text = _game.Score.ToString();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  GAME EVENTS
    // ═══════════════════════════════════════════════════════════════

    private void OnTileHit(Tile tile, TileState quality)
    {
        if (tile.IsHoldNote && quality == TileState.HitPerfect)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _midi.NoteOff(tile.MidiNote);
                ShowHitFeedback("HOLD COMPLETE", Color.FromRgb(57, 255, 20));
                SpawnHitParticles(tile.Lane, Color.FromRgb(57, 255, 20));
            });
        }
    }

    private void OnTileMissed(Tile tile)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (tile.IsHoldNote)
            {
                _midi.NoteOff(tile.MidiNote);
            }

            ShowHitFeedback("MISS", Color.FromRgb(255, 23, 68));
            ComboText.Text = "";

            // Red flash on the missed lane
            FlashLane(tile.Lane, Color.FromArgb(40, 255, 23, 68));
        });
    }

    private void OnWrongHit(int lane)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Play error sound
            _midi.PlayErrorSound();

            // Show WRONG feedback in orange
            ShowHitFeedback("WRONG", Color.FromRgb(255, 107, 0));
            ComboText.Text = "";

            // Flash lane in orange-red
            FlashLane(lane, Color.FromArgb(50, 255, 107, 0));

            // Update score display
            ScoreText.Text = _game.Score.ToString();
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
            WaitingIndicator.Opacity = 0;
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
        WrongCountText.Text = _game.WrongHitCount.ToString();
        MaxComboText.Text = $"Max Combo: {_game.MaxCombo}";
    }

    private void ShowMenu()
    {
        MenuOverlay.Visibility = Visibility.Visible;
        GameOverOverlay.Visibility = Visibility.Collapsed;
        PauseOverlay.Visibility = Visibility.Collapsed;
        WaitingIndicator.Opacity = 0;
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

        // Update score display in case it ticks up during holding
        if (_game.State == PianoTilesGame.GameState.Playing)
        {
            ScoreText.Text = _game.Score.ToString();
        }

        // Update visuals — order matters for layering
        UpdateTileVisuals();
        UpdateParticles(delta);
        UpdateBackgroundParticles(delta);
        UpdateLaneFlashes(delta);
        UpdateHitFeedback(delta);
        UpdateWaitingIndicator(delta);
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
            var tv = CreateTileVisual();
            TileCanvas.Children.Add(tv.Element);
            _tileVisuals.Add(tv);
        }

        // Hide excess visuals
        for (int i = tiles.Count; i < _tileVisuals.Count; i++)
        {
            _tileVisuals[i].Element.Visibility = Visibility.Collapsed;
        }

        double tilePixelHeight = _screenHeight * PianoTilesGame.TileHeight;
        double laneWidthMinusGap = _laneWidth - 4;

        // Update visible tile positions and styles
        for (int i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            var tv = _tileVisuals[i];
            var visual = tv.Element;
            visual.Visibility = Visibility.Visible;

            double x = tile.Lane * _laneWidth + 2;

            if (tile.IsHoldNote)
            {
                double holdHeight = (tile.DurationMs / PianoTilesGame.ScrollDurationMs) * _screenHeight;
                double height = holdHeight;
                double y;

                if (tile.State == TileState.Holding)
                {
                    double bottomY = _screenHeight * PianoTilesGame.HitZoneY;
                    double timePastTarget = _game.ElapsedMs - tile.TargetTimeMs;
                    double remainingTime = tile.DurationMs - timePastTarget;
                    height = (remainingTime / PianoTilesGame.ScrollDurationMs) * _screenHeight;
                    height = Math.Max(tilePixelHeight, height);
                    y = bottomY - height;

                    if (_rng.NextDouble() < 0.25)
                    {
                        SpawnHitParticles(tile.Lane, Color.FromRgb(0, 255, 128));
                    }
                }
                else
                {
                    height = Math.Max(tilePixelHeight, holdHeight);
                    y = tile.YPosition * _screenHeight - height;
                }

                Canvas.SetLeft(visual, x);
                Canvas.SetTop(visual, y);
                visual.Width = laneWidthMinusGap;
                visual.Height = height;
            }
            else
            {
                double y = tile.YPosition * _screenHeight - tilePixelHeight;
                Canvas.SetLeft(visual, x);
                Canvas.SetTop(visual, y);
                visual.Width = laneWidthMinusGap;
                visual.Height = tilePixelHeight;
            }

            // Style based on state — use cached brushes
            var laneColor = LaneColors[tile.Lane % LaneColors.Length];

            if (tile.IsHoldNote)
            {
                switch (tile.State)
                {
                    case TileState.Active:
                        tv.BgBrush.Color = Color.FromArgb(50, laneColor.R, laneColor.G, laneColor.B);
                        tv.BorderBrush.Color = Color.FromArgb(180, laneColor.R, laneColor.G, laneColor.B);
                        visual.Opacity = 1;
                        if (tv.Shadow != null)
                        {
                            tv.Shadow.Color = laneColor;
                            tv.Shadow.BlurRadius = 15;
                            tv.Shadow.Opacity = 0.5;
                        }

                        // In Easy Mode, highlight the waiting tile
                        if (_game.EasyMode && _game.IsWaitingForInput && tile.Lane == _game.WaitingLane
                            && tile.YPosition >= PianoTilesGame.HitZoneY - 0.01)
                        {
                            tv.BorderBrush.Color = Color.FromArgb(230, laneColor.R, laneColor.G, laneColor.B);
                            if (tv.Shadow != null)
                            {
                                tv.Shadow.BlurRadius = 30;
                                tv.Shadow.Opacity = 0.9;
                            }
                        }
                        break;

                    case TileState.Holding:
                        tv.BgBrush.Color = Color.FromArgb(160, 0, 255, 128); // Bright emerald hold fill
                        tv.BorderBrush.Color = Color.FromRgb(0, 255, 128);
                        visual.Opacity = 1;
                        if (tv.Shadow != null)
                        {
                            tv.Shadow.Color = Color.FromRgb(0, 255, 128);
                            tv.Shadow.BlurRadius = 30;
                            tv.Shadow.Opacity = 0.9;
                        }
                        break;

                    case TileState.HitPerfect:
                        byte alphaBg = (byte)(255 * (1 - tile.AnimProgress));
                        byte alphaBorder = (byte)(100 * (1 - tile.AnimProgress));
                        tv.BgBrush.Color = Color.FromArgb(alphaBg, 0, 245, 255);
                        tv.BorderBrush.Color = Color.FromArgb(alphaBorder, 0, 245, 255);
                        visual.Opacity = 1 - tile.AnimProgress;
                        if (tv.Shadow != null)
                        {
                            tv.Shadow.Color = Color.FromRgb(0, 245, 255);
                            tv.Shadow.BlurRadius = 30 + tile.AnimProgress * 20;
                            tv.Shadow.Opacity = 0.8 * (1 - tile.AnimProgress);
                        }
                        break;

                    case TileState.HitGood:
                        alphaBg = (byte)(255 * (1 - tile.AnimProgress));
                        alphaBorder = (byte)(80 * (1 - tile.AnimProgress));
                        tv.BgBrush.Color = Color.FromArgb(alphaBg, 132, 94, 247);
                        tv.BorderBrush.Color = Color.FromArgb(alphaBorder, 132, 94, 247);
                        visual.Opacity = 1 - tile.AnimProgress;
                        if (tv.Shadow != null)
                        {
                            tv.Shadow.Color = Color.FromRgb(132, 94, 247);
                            tv.Shadow.BlurRadius = 20 + tile.AnimProgress * 15;
                            tv.Shadow.Opacity = 0.6 * (1 - tile.AnimProgress);
                        }
                        break;

                    case TileState.Missed:
                        alphaBg = (byte)(180 * (1 - tile.AnimProgress));
                        alphaBorder = (byte)(60 * (1 - tile.AnimProgress));
                        tv.BgBrush.Color = Color.FromArgb(alphaBg, 255, 23, 68);
                        tv.BorderBrush.Color = Color.FromArgb(alphaBorder, 255, 23, 68);
                        visual.Opacity = 1 - tile.AnimProgress;
                        if (tv.Shadow != null)
                        {
                            tv.Shadow.Opacity = 0;
                        }
                        break;
                }
            }
            else
            {
                switch (tile.State)
                {
                    case TileState.Active:
                        tv.BgBrush.Color = Color.FromRgb(18, 18, 28);
                        tv.BorderBrush.Color = Color.FromArgb(60, laneColor.R, laneColor.G, laneColor.B);
                        visual.Opacity = 1;
                        if (tv.Shadow != null)
                        {
                            tv.Shadow.Color = laneColor;
                            tv.Shadow.BlurRadius = 12;
                            tv.Shadow.Opacity = 0.3;
                        }

                        // In Easy Mode, highlight the waiting tile
                        if (_game.EasyMode && _game.IsWaitingForInput && tile.Lane == _game.WaitingLane
                            && tile.YPosition >= PianoTilesGame.HitZoneY - 0.01)
                        {
                            tv.BorderBrush.Color = Color.FromArgb(150, laneColor.R, laneColor.G, laneColor.B);
                            if (tv.Shadow != null)
                            {
                                tv.Shadow.BlurRadius = 25;
                                tv.Shadow.Opacity = 0.7;
                            }
                        }
                        break;

                    case TileState.HitPerfect:
                        byte alphaBg = (byte)(255 * (1 - tile.AnimProgress));
                        byte alphaBorder = (byte)(100 * (1 - tile.AnimProgress));
                        tv.BgBrush.Color = Color.FromArgb(alphaBg, 0, 245, 255);
                        tv.BorderBrush.Color = Color.FromArgb(alphaBorder, 0, 245, 255);
                        visual.Opacity = 1 - tile.AnimProgress;
                        if (tv.Shadow != null)
                        {
                            tv.Shadow.Color = Color.FromRgb(0, 245, 255);
                            tv.Shadow.BlurRadius = 30 + tile.AnimProgress * 20;
                            tv.Shadow.Opacity = 0.8 * (1 - tile.AnimProgress);
                        }
                        break;

                    case TileState.HitGood:
                        alphaBg = (byte)(255 * (1 - tile.AnimProgress));
                        alphaBorder = (byte)(80 * (1 - tile.AnimProgress));
                        tv.BgBrush.Color = Color.FromArgb(alphaBg, 132, 94, 247);
                        tv.BorderBrush.Color = Color.FromArgb(alphaBorder, 132, 94, 247);
                        visual.Opacity = 1 - tile.AnimProgress;
                        if (tv.Shadow != null)
                        {
                            tv.Shadow.Color = Color.FromRgb(132, 94, 247);
                            tv.Shadow.BlurRadius = 20 + tile.AnimProgress * 15;
                            tv.Shadow.Opacity = 0.6 * (1 - tile.AnimProgress);
                        }
                        break;

                    case TileState.Missed:
                        alphaBg = (byte)(180 * (1 - tile.AnimProgress));
                        alphaBorder = (byte)(60 * (1 - tile.AnimProgress));
                        tv.BgBrush.Color = Color.FromArgb(alphaBg, 255, 23, 68);
                        tv.BorderBrush.Color = Color.FromArgb(alphaBorder, 255, 23, 68);
                        visual.Opacity = 1 - tile.AnimProgress;
                        if (tv.Shadow != null)
                        {
                            tv.Shadow.Opacity = 0;
                        }
                        break;
                }
            }
        }
    }

    private TileVisual CreateTileVisual()
    {
        var bgBrush = new SolidColorBrush(Color.FromRgb(18, 18, 28));
        var borderBrush = new SolidColorBrush(Color.FromArgb(40, 132, 94, 247));
        var shadow = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 12, Opacity = 0.3 };

        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = bgBrush,
            BorderBrush = borderBrush,
            Effect = shadow,
            CacheMode = new BitmapCache { RenderAtScale = 1.0 }, // GPU cache per tile
        };

        return new TileVisual(border, bgBrush, borderBrush, shadow);
    }

    // ═══════════════════════════════════════════════════════════════
    //  PARTICLE EFFECTS
    // ═══════════════════════════════════════════════════════════════

    private void SpawnHitParticles(int lane, Color color)
    {
        double hitY = _screenHeight * PianoTilesGame.HitZoneY;
        double centerX = (lane + 0.5) * _laneWidth;
        int count = 10; // Reduced from 16 — still looks great, fewer elements

        // Create or reuse a frozen brush for this color
        var brush = new SolidColorBrush(color);
        brush.Freeze(); // Frozen = thread-safe & faster rendering

        for (int i = 0; i < count; i++)
        {
            double angle = _rng.NextDouble() * Math.PI * 2;
            double speed = _rng.NextDouble() * 300 + 100;
            double size = _rng.NextDouble() * 5 + 2;

            var particle = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = brush,
                // NO DropShadowEffect — this was the #1 FPS killer
                // The glow effect is barely visible on tiny particles
            };

            Canvas.SetLeft(particle, centerX);
            Canvas.SetTop(particle, hitY);
            ParticleCanvas.Children.Add(particle);

            _particles.Add(particle);
            _particleStates.Add(new ParticleState(
                centerX, hitY,
                Math.Cos(angle) * speed,
                Math.Sin(angle) * speed - 100,
                0, 0.5 + _rng.NextDouble() * 0.3,
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
                // Remove from canvas — use RemoveAt with indexed access for O(1) when at end
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
        if (lane < 0 || lane >= _laneFlashes.Length) return;

        var flash = _laneFlashes[lane];
        if (overrideColor.HasValue)
        {
            // Override color — update the existing brush instead of allocating new
            if (flash.Background is SolidColorBrush existing)
                existing.Color = overrideColor.Value;
            else
                flash.Background = new SolidColorBrush(overrideColor.Value);
        }
        else
        {
            // Use the pre-created lane brush
            flash.Background = _laneFlashBrushes[lane];
        }
        flash.Opacity = 1;
    }

    private void UpdateLaneFlashes(double delta)
    {
        for (int i = 0; i < _laneFlashes.Length; i++)
        {
            if (_laneFlashes[i].Opacity > 0)
            {
                _laneFlashes[i].Opacity = Math.Max(0, _laneFlashes[i].Opacity - delta * 4);
            }
        }
    }

    private void ShowHitFeedback(string text, Color color)
    {
        _hitFeedbackTimer = 0.6;

        HitFeedbackText.Text = text;
        // Reuse the stored brush instead of allocating a new one
        _hitFeedbackBrush.Color = color;
        HitFeedbackText.Foreground = _hitFeedbackBrush;
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

    private void UpdateWaitingIndicator(double delta)
    {
        if (_game.EasyMode && _game.IsWaitingForInput && _game.State == PianoTilesGame.GameState.Playing)
        {
            _waitingPulseTimer += delta * 3;
            double pulse = 0.5 + 0.5 * Math.Sin(_waitingPulseTimer);
            WaitingIndicator.Opacity = 0.4 + pulse * 0.6;
        }
        else
        {
            if (WaitingIndicator.Opacity > 0)
            {
                WaitingIndicator.Opacity = Math.Max(0, WaitingIndicator.Opacity - delta * 3);
            }
            _waitingPulseTimer = 0;
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
            _touchInput.TouchUp -= OnTouchUp;
        }

        _midi.AllNotesOff();
        _midi.Dispose();
    }
}
