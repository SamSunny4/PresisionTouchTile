using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TouchpadVisualizer.Models;

namespace TouchpadVisualizer.ViewModels;

/// <summary>
/// View model for the settings overlay panel.
/// Wraps AppSettings with change notification and save/load functionality.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private AppSettings _settings;

    [ObservableProperty]
    private int _fpsLimit;

    [ObservableProperty]
    private float _glowIntensity;

    [ObservableProperty]
    private float _particleDensity;

    [ObservableProperty]
    private float _touchIndicatorSize;

    [ObservableProperty]
    private float _perspectiveAngle;

    [ObservableProperty]
    private float _bloomIntensity;

    [ObservableProperty]
    private float _backgroundSpeed;

    [ObservableProperty]
    private float _trailLength;

    [ObservableProperty]
    private float _rippleIntensity;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private int _selectedQuality;

    /// <summary>Raised when any setting changes.</summary>
    public event Action<AppSettings>? SettingsChanged;

    public SettingsViewModel()
    {
        _settings = AppSettings.Load();
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        FpsLimit = _settings.FpsLimit;
        GlowIntensity = _settings.GlowIntensity;
        ParticleDensity = _settings.ParticleDensity;
        TouchIndicatorSize = _settings.TouchIndicatorSize;
        PerspectiveAngle = _settings.PerspectiveAngle;
        BloomIntensity = _settings.BloomIntensity;
        BackgroundSpeed = _settings.BackgroundSpeed;
        TrailLength = _settings.TrailLength;
        RippleIntensity = _settings.RippleIntensity;
        IsFullscreen = _settings.IsFullscreen;
        SelectedQuality = (int)_settings.Quality;
    }

    partial void OnFpsLimitChanged(int value) { _settings.FpsLimit = value; NotifySettingsChanged(); }
    partial void OnGlowIntensityChanged(float value) { _settings.GlowIntensity = value; NotifySettingsChanged(); }
    partial void OnParticleDensityChanged(float value) { _settings.ParticleDensity = value; NotifySettingsChanged(); }
    partial void OnTouchIndicatorSizeChanged(float value) { _settings.TouchIndicatorSize = value; NotifySettingsChanged(); }
    partial void OnPerspectiveAngleChanged(float value) { _settings.PerspectiveAngle = value; NotifySettingsChanged(); }
    partial void OnBloomIntensityChanged(float value) { _settings.BloomIntensity = value; NotifySettingsChanged(); }
    partial void OnBackgroundSpeedChanged(float value) { _settings.BackgroundSpeed = value; NotifySettingsChanged(); }
    partial void OnTrailLengthChanged(float value) { _settings.TrailLength = value; NotifySettingsChanged(); }
    partial void OnRippleIntensityChanged(float value) { _settings.RippleIntensity = value; NotifySettingsChanged(); }
    partial void OnIsFullscreenChanged(bool value) { _settings.IsFullscreen = value; NotifySettingsChanged(); }
    partial void OnSelectedQualityChanged(int value)
    {
        _settings.Quality = (AnimationQuality)value;
        ApplyQualityPreset();
        NotifySettingsChanged();
    }

    private void ApplyQualityPreset()
    {
        switch (_settings.Quality)
        {
            case AnimationQuality.Low:
                ParticleDensity = 0.3f;
                BloomIntensity = 0.3f;
                TrailLength = 0.5f;
                break;
            case AnimationQuality.Medium:
                ParticleDensity = 0.7f;
                BloomIntensity = 0.6f;
                TrailLength = 0.8f;
                break;
            case AnimationQuality.High:
                ParticleDensity = 1.0f;
                BloomIntensity = 0.8f;
                TrailLength = 1.0f;
                break;
            case AnimationQuality.Ultra:
                ParticleDensity = 2.0f;
                BloomIntensity = 1.2f;
                TrailLength = 1.5f;
                break;
        }
    }

    private void NotifySettingsChanged()
    {
        SettingsChanged?.Invoke(_settings);
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settings.Save();
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        _settings = new AppSettings();
        LoadFromSettings();
        NotifySettingsChanged();
    }

    /// <summary>Get the current settings snapshot.</summary>
    public AppSettings GetSettings() => _settings;
}
