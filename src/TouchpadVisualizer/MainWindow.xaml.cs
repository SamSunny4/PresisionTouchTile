using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TouchpadVisualizer.Input;
using TouchpadVisualizer.Views;
using TouchpadVisualizer.Models;
using TouchpadVisualizer.Rendering;
using TouchpadVisualizer.ViewModels;

namespace TouchpadVisualizer;

/// <summary>
/// Main window code-behind. Wires together input, rendering, and UI.
/// Handles borderless fullscreen, cursor hiding, keyboard shortcuts,
/// and the render loop.
/// </summary>
public partial class MainWindow : Window
{
    // Win32 imports for cursor control
    [DllImport("user32.dll")]
    private static extern int ShowCursor(bool bShow);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    // Components
    private readonly MainViewModel _viewModel = new();
    private readonly TouchpadInputManager _touchpadInput = new();
    private readonly GestureDetector _gestureDetector = new();
    private D3DRenderer? _renderer;
    private D3DImage? _d3dImage;

    // Render loop
    private Thread? _renderThread;
    private volatile bool _isRunning;
    private readonly object _touchLock = new();
    private TouchContact[] _currentContacts = [];
    private ActiveGesture _currentGesture;

    // Cursor hiding
    private readonly DispatcherTimer _cursorTimer;
    private bool _cursorHidden;
    private DateTime _lastMouseMove = DateTime.Now;

    // Settings
    private bool _settingsVisible;
    private bool _showGestureHint = true;

    // Window handle for cleanup
    private IntPtr _hwnd;

    // Calibration
    private bool _isCalibrating;
    private int _calibXMin = int.MaxValue;
    private int _calibXMax = int.MinValue;
    private int _calibYMin = int.MaxValue;
    private int _calibYMax = int.MinValue;

    private bool _isGameRunning;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        // Cursor hide timer
        _cursorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _cursorTimer.Tick += CursorTimer_Tick;
        _cursorTimer.Start();

        // Settings change handler
        _viewModel.Settings.SettingsChanged += OnSettingsChanged;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Show the gesture disable hint on first launch
        if (_showGestureHint)
        {
            GestureHintPanel.Visibility = Visibility.Visible;
        }

        // Initialize D3D renderer
        int width = (int)ActualWidth;
        int height = (int)ActualHeight;

        if (width == 0 || height == 0)
        {
            width = (int)SystemParameters.PrimaryScreenWidth;
            height = (int)SystemParameters.PrimaryScreenHeight;
        }

        _renderer = new D3DRenderer();
        if (_renderer.Initialize(width, height))
        {
            // Setup D3DImage for WPF interop
            _d3dImage = new D3DImage();
            D3DImageHost.Source = _d3dImage;

            // Apply initial settings
            _renderer.UpdateSettings(_viewModel.Settings.GetSettings());

            // Start render loop
            _isRunning = true;
            _renderThread = new Thread(RenderLoop)
            {
                Name = "D3D Render Thread",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _renderThread.Start();

            // Use CompositionTarget for D3DImage updates on the UI thread
            CompositionTarget.Rendering += CompositionTarget_Rendering;

            Debug.WriteLine("[MainWindow] Renderer initialized successfully.");
        }
        else
        {
            _viewModel.StatusText = "Failed to initialize GPU renderer";
            Debug.WriteLine("[MainWindow] Renderer initialization failed!");
        }

        // Register touchpad input
        var hwndSource = HwndSource.FromVisual(this) as HwndSource;
        if (hwndSource != null)
        {
            _hwnd = hwndSource.Handle;
            hwndSource.AddHook(WndProc);

            // Register as a touch window to receive WM_TOUCH and suppress WM_GESTURE
            HidInterop.RegisterTouchWindow(_hwnd, HidInterop.TWF_WANTPALM);

            if (_touchpadInput.Register(_hwnd))
            {
                _viewModel.StatusText = "Touchpad registered — touch to begin";
                _viewModel.IsTouchpadDetected = true;
            }
            else
            {
                _viewModel.StatusText = "No precision touchpad detected";
                _viewModel.IsTouchpadDetected = false;
            }

            // Wire touch events
            _touchpadInput.ContactsUpdated += OnContactsUpdated;
            _touchpadInput.TouchDown += OnTouchDown;
            _touchpadInput.TouchUp += OnTouchUp;

            // Apply calibration settings if they exist
            var settings = _viewModel.Settings.GetSettings();
            if (settings.IsCalibrated)
            {
                _touchpadInput.SetCalibration(true, settings.CalibratedXMin, settings.CalibratedXMax, settings.CalibratedYMin, settings.CalibratedYMax);
            }
            else
            {
                // Show calibration panel on first run
                StartCalibrationMode();
            }
        }
    }

    /// <summary>
    /// WndProc hook to capture WM_INPUT and suppress gesture/touch/pointer messages.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case HidInterop.WM_INPUT:
                handled = _touchpadInput.ProcessRawInput(lParam);
                break;

            // Block Windows native gesture recognition
            case HidInterop.WM_GESTURE:
                handled = true;
                break;

            case HidInterop.WM_GESTURENOTIFY:
                handled = true;
                break;

            // Consume WM_TOUCH so Windows doesn't convert to gestures
            case HidInterop.WM_TOUCH:
                // Close the touch input handle to prevent resource leaks
                HidInterop.CloseTouchInputHandle(lParam);
                handled = true;
                break;

            // Suppress WM_POINTER messages that may trigger system gestures
            case HidInterop.WM_POINTERUPDATE:
            case HidInterop.WM_POINTERDOWN:
            case HidInterop.WM_POINTERUP:
            case HidInterop.WM_POINTERENTER:
            case HidInterop.WM_POINTERLEAVE:
            case HidInterop.WM_POINTERACTIVATE:
            case HidInterop.WM_POINTERCAPTURECHANGED:
            case HidInterop.WM_POINTERWHEEL:
            case HidInterop.WM_POINTERHWHEEL:
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Called when touchpad contacts are updated.
    /// </summary>
    private void OnContactsUpdated(object? sender, TouchpadContactEventArgs e)
    {
        if (_isCalibrating)
        {
            // During calibration, track min/max raw coordinates from any active contact.
            // NOTE: e.Contacts only contains IsDown=true contacts (from _activeContacts).
            bool updatedBounds = false;
            foreach (var contact in e.Contacts)
            {
                // Only update from contacts that have valid, non-zero positions
                if (contact.IsDown && (contact.RawX != 0 || contact.RawY != 0))
                {
                    if (contact.RawX < _calibXMin) { _calibXMin = contact.RawX; updatedBounds = true; }
                    if (contact.RawX > _calibXMax) { _calibXMax = contact.RawX; updatedBounds = true; }
                    if (contact.RawY < _calibYMin) { _calibYMin = contact.RawY; updatedBounds = true; }
                    if (contact.RawY > _calibYMax) { _calibYMax = contact.RawY; updatedBounds = true; }
                }
            }

            if (updatedBounds)
            {
                int xMin = _calibXMin == int.MaxValue ? 0 : _calibXMin;
                int xMax = _calibXMax == int.MinValue ? 0 : _calibXMax;
                int yMin = _calibYMin == int.MaxValue ? 0 : _calibYMin;
                int yMax = _calibYMax == int.MinValue ? 0 : _calibYMax;
                Dispatcher.BeginInvoke(() =>
                {
                    CalibXText.Text = $"{xMin}  –  {xMax}";
                    CalibYText.Text = $"{yMin}  –  {yMax}";
                });
            }
            // Still allow rendering during calibration
        }

        // Update gesture detector
        _gestureDetector.Update(e.Contacts);

        lock (_touchLock)
        {
            _currentContacts = e.Contacts;
            _currentGesture = _gestureDetector.CurrentGesture;
        }

        // Update UI on dispatcher thread
        Dispatcher.BeginInvoke(() =>
        {
            int activeCount = e.Contacts.Count(c => c.IsDown);
            _viewModel.UpdateTouchCount(activeCount);
            _viewModel.UpdateGesture(_gestureDetector.CurrentGesture.Type);

            // Update HUD
            TouchCountText.Text = $"{activeCount} touch{(activeCount != 1 ? "es" : "")}";
            GestureText.Text = _viewModel.GestureText;

            // Dismiss gesture hint on first touch
            if (_showGestureHint && activeCount > 0)
            {
                _showGestureHint = false;
                GestureHintPanel.Visibility = Visibility.Collapsed;
            }
        });
    }

    /// <summary>
    /// Called when a finger touches down — triggers ripple.
    /// </summary>
    private void OnTouchDown(object? sender, TouchContact contact)
    {
        _renderer?.AddRipple(contact.NormalizedX, contact.NormalizedY, contact.Pressure);
    }

    /// <summary>
    /// Called when a finger lifts.
    /// </summary>
    private void OnTouchUp(object? sender, TouchContact contact)
    {
        // Could trigger a fade-out burst effect here
    }

    /// <summary>
    /// Background render thread — runs at target FPS.
    /// </summary>
    private void RenderLoop()
    {
        while (_isRunning)
        {
            if (_isGameRunning)
            {
                Thread.Sleep(16);
                continue;
            }
            try
            {
                TouchContact[] contacts;
                ActiveGesture gesture;

                lock (_touchLock)
                {
                    contacts = _currentContacts;
                    gesture = _currentGesture;
                }

                _renderer?.UpdateTouches(contacts, gesture);
                _renderer?.RenderFrame();

                // Update FPS display periodically
                if (_renderer != null)
                {
                    float fps = _renderer.CurrentFps;
                    Dispatcher.BeginInvoke(() =>
                    {
                        FpsText.Text = $"{fps:F0} FPS";
                        _viewModel.UpdateFps(fps);
                    }, DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RenderLoop] Error: {ex.Message}");
                Thread.Sleep(16); // Prevent spin on error
            }
        }
    }

    /// <summary>
    /// WPF composition rendering — updates D3DImage from shared texture.
    /// Uses ConsumeFrame() to synchronize with the render thread.
    /// </summary>
    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (_isGameRunning || _d3dImage == null || _renderer == null || !_renderer.IsInitialized)
            return;

        // Only update if the render thread has produced a new frame
        if (!_renderer.ConsumeFrame())
            return;

        try
        {
            var sharedHandle = _renderer.GetSharedHandle();
            if (sharedHandle == IntPtr.Zero)
                return;

            _d3dImage.Lock();
            try
            {
                _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, sharedHandle, true);

                int pw = _d3dImage.PixelWidth;
                int ph = _d3dImage.PixelHeight;
                if (pw > 0 && ph > 0)
                {
                    _d3dImage.AddDirtyRect(new Int32Rect(0, 0, pw, ph));
                }
            }
            finally
            {
                _d3dImage.Unlock();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WPF] D3DImage error: {ex.Message}");
        }
    }

    // === Keyboard and Input Events ===

    private void StartCalibrationMode()
    {
        _isCalibrating = true;
        _calibXMin = int.MaxValue;
        _calibXMax = int.MinValue;
        _calibYMin = int.MaxValue;
        _calibYMax = int.MinValue;

        // Disable any existing calibration so we see raw device coordinates
        _touchpadInput.SetCalibration(false, 0, 0, 0, 0);

        CalibXText.Text = "— — —";
        CalibYText.Text = "— — —";

        CalibrationPanel.Visibility = Visibility.Visible;
        if (_settingsVisible) ToggleSettings();
    }

    private void CompleteCalibration_Click(object sender, RoutedEventArgs e)
    {
        // Require at least 100 units of range in both axes — if less, the user
        // probably didn't actually slide to the corners
        bool hasValidX = _calibXMin != int.MaxValue && _calibXMax != int.MinValue
                         && (_calibXMax - _calibXMin) > 50;
        bool hasValidY = _calibYMin != int.MaxValue && _calibYMax != int.MinValue
                         && (_calibYMax - _calibYMin) > 50;

        if (!hasValidX || !hasValidY)
        {
            // Not enough data — skip instead
            SkipCalibration_Click(sender, e);
            return;
        }

        _isCalibrating = false;
        CalibrationPanel.Visibility = Visibility.Collapsed;

        var settings = _viewModel.Settings.GetSettings();
        settings.IsCalibrated = true;
        settings.CalibratedXMin = _calibXMin;
        settings.CalibratedXMax = _calibXMax;
        settings.CalibratedYMin = _calibYMin;
        settings.CalibratedYMax = _calibYMax;
        settings.Save();

        _touchpadInput.SetCalibration(true, _calibXMin, _calibXMax, _calibYMin, _calibYMax);
        Debug.WriteLine($"[Calibration] Complete: X=[{_calibXMin},{_calibXMax}] Y=[{_calibYMin},{_calibYMax}]");
    }

    private void SkipCalibration_Click(object sender, RoutedEventArgs e)
    {
        _isCalibrating = false;
        CalibrationPanel.Visibility = Visibility.Collapsed;
        
        // Disable calibration override and fall back to logical min/max
        var settings = _viewModel.Settings.GetSettings();
        settings.IsCalibrated = false;
        settings.Save();
        
        _touchpadInput.SetCalibration(false, 0, 0, 0, 0);
    }

    private void StartCalibration_Click(object sender, RoutedEventArgs e)
    {
        StartCalibrationMode();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;

            case Key.S:
                ToggleSettings();
                break;

            case Key.F11:
                ToggleFullscreen();
                break;

            case Key.G:
                LaunchPianoTiles();
                break;

            case Key.H:
                // Toggle HUD visibility
                var overlay = RootGrid.Children[2] as UIElement;
                if (overlay != null)
                    overlay.Visibility = overlay.Visibility == Visibility.Visible
                        ? Visibility.Collapsed : Visibility.Visible;
                break;
        }
    }

    private void LaunchPianoTiles()
    {
        _isGameRunning = true;
        var gameWindow = new PianoTilesWindow(_touchpadInput)
        {
            Owner = this
        };
        gameWindow.ShowDialog();
        _isGameRunning = false;
    }

    private void ToggleSettings()
    {
        _settingsVisible = !_settingsVisible;
        SettingsPanel.Visibility = _settingsVisible ? Visibility.Visible : Visibility.Collapsed;

        // Show cursor when settings are open
        if (_settingsVisible && _cursorHidden)
        {
            ShowCursor(true);
            Cursor = Cursors.Arrow;
            _cursorHidden = false;
        }
    }

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.Maximized && WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.CanResize;
        }
        else
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
        }
    }

    // ─── Cursor Hiding ─────────────────────────────────────────────────

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        _lastMouseMove = DateTime.Now;

        if (_cursorHidden)
        {
            ShowCursor(true);
            Cursor = Cursors.Arrow;
            _cursorHidden = false;
        }
    }

    private void CursorTimer_Tick(object? sender, EventArgs e)
    {
        if (_settingsVisible) return; // Don't hide cursor when settings are open

        var elapsed = DateTime.Now - _lastMouseMove;
        if (elapsed.TotalSeconds >= 3 && !_cursorHidden)
        {
            ShowCursor(false);
            Cursor = Cursors.None;
            _cursorHidden = true;
        }
    }

    // ─── Settings UI Events ────────────────────────────────────────────

    private void OnSettingsChanged(AppSettings settings)
    {
        _renderer?.UpdateSettings(settings);
    }

    private void QualityCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (QualityCombo?.SelectedIndex >= 0)
            _viewModel.Settings.SelectedQuality = QualityCombo.SelectedIndex;
    }

    private void FpsLimitSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)e.NewValue;
        if (FpsLimitValue != null) FpsLimitValue.Text = val.ToString();
        _viewModel.Settings.FpsLimit = val;
    }

    private void GlowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        float val = (float)e.NewValue / 100f;
        if (GlowValue != null) GlowValue.Text = val.ToString("F1");
        _viewModel.Settings.GlowIntensity = val;
    }

    private void DensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        float val = (float)e.NewValue / 100f;
        if (DensityValue != null) DensityValue.Text = val.ToString("F1");
        _viewModel.Settings.ParticleDensity = val;
    }

    private void IndicatorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        float val = (float)e.NewValue / 100f;
        if (IndicatorValue != null) IndicatorValue.Text = val.ToString("F1");
        _viewModel.Settings.TouchIndicatorSize = val;
    }

    private void PerspectiveSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        float val = (float)e.NewValue;
        if (PerspectiveValue != null) PerspectiveValue.Text = $"{val:F0}°";
        _viewModel.Settings.PerspectiveAngle = val;
    }

    private void TrailSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        float val = (float)e.NewValue / 100f;
        if (TrailValue != null) TrailValue.Text = val.ToString("F1");
        _viewModel.Settings.TrailLength = val;
    }

    private void RippleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        float val = (float)e.NewValue / 100f;
        if (RippleValue != null) RippleValue.Text = val.ToString("F1");
        _viewModel.Settings.RippleIntensity = val;
    }

    private void BgSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        float val = (float)e.NewValue / 100f;
        if (BgSpeedValue != null) BgSpeedValue.Text = val.ToString("F1");
        _viewModel.Settings.BackgroundSpeed = val;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Settings.SaveSettingsCommand.Execute(null);
        StatusText.Text = "Settings saved ✓";
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Settings.ResetDefaultsCommand.Execute(null);
        // Refresh slider values
        LoadSettingsToUI();
    }

    private void LoadSettingsToUI()
    {
        var s = _viewModel.Settings;
        FpsLimitSlider.Value = s.FpsLimit;
        GlowSlider.Value = s.GlowIntensity * 100;
        DensitySlider.Value = s.ParticleDensity * 100;
        IndicatorSlider.Value = s.TouchIndicatorSize * 100;
        PerspectiveSlider.Value = s.PerspectiveAngle;
        TrailSlider.Value = s.TrailLength * 100;
        RippleSlider.Value = s.RippleIntensity * 100;
        BgSpeedSlider.Value = s.BackgroundSpeed * 100;
        QualityCombo.SelectedIndex = s.SelectedQuality;
    }

    // ─── Cleanup ───────────────────────────────────────────────────────

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _isRunning = false;
        _renderThread?.Join(2000);

        CompositionTarget.Rendering -= CompositionTarget_Rendering;

        // Unregister touch window
        if (_hwnd != IntPtr.Zero)
        {
            HidInterop.UnregisterTouchWindow(_hwnd);
        }

        _touchpadInput.Dispose();
        _renderer?.Dispose();
        _cursorTimer.Stop();

        // Restore cursor
        if (_cursorHidden)
        {
            ShowCursor(true);
        }
    }
}