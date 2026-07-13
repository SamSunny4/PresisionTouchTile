using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TouchpadVisualizer.Models;

namespace TouchpadVisualizer.ViewModels;

/// <summary>
/// Main view model for the application window.
/// Exposes current FPS, active touch count, and settings toggle.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private float _currentFps;

    [ObservableProperty]
    private int _activeTouchCount;

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private string _statusText = "Waiting for touchpad input...";

    [ObservableProperty]
    private bool _isTouchpadDetected;

    [ObservableProperty]
    private string _gestureText = "";

    public SettingsViewModel Settings { get; }

    public MainViewModel()
    {
        Settings = new SettingsViewModel();
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsVisible = !IsSettingsVisible;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsVisible = false;
    }

    public void UpdateFps(float fps)
    {
        CurrentFps = fps;
    }

    public void UpdateTouchCount(int count)
    {
        ActiveTouchCount = count;
        if (count > 0 && !IsTouchpadDetected)
        {
            IsTouchpadDetected = true;
            StatusText = "Touchpad active";
        }
    }

    public void UpdateGesture(GestureType type)
    {
        GestureText = type switch
        {
            GestureType.TwoFingerParallel => "⇅ Two-Finger Scroll",
            GestureType.Pinch => "⊕ Pinch",
            GestureType.Spread => "⊖ Spread",
            GestureType.Rotation => "↻ Rotation",
            GestureType.ThreeFingerSwipe => "≡ Three-Finger Swipe",
            GestureType.FourFingerSwipe => "⊞ Four-Finger Swipe",
            _ => ""
        };
    }
}
