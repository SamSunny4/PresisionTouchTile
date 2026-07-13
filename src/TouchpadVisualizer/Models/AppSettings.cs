using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchpadVisualizer.Models;

/// <summary>
/// Quality presets for the animation system.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnimationQuality
{
    Low,
    Medium,
    High,
    Ultra
}

/// <summary>
/// Application settings that persist between sessions.
/// Controls rendering quality, visual parameters, and input behavior.
/// </summary>
public class AppSettings
{
    /// <summary>Target FPS cap. 0 = unlimited.</summary>
    public int FpsLimit { get; set; } = 120;

    /// <summary>Glow intensity multiplier (0.0–3.0).</summary>
    public float GlowIntensity { get; set; } = 1.0f;

    /// <summary>Particle density multiplier (0.1–3.0).</summary>
    public float ParticleDensity { get; set; } = 1.0f;

    /// <summary>Touch indicator base size multiplier (0.5–3.0).</summary>
    public float TouchIndicatorSize { get; set; } = 1.0f;

    /// <summary>3D perspective tilt angle in degrees (0–80).</summary>
    public float PerspectiveAngle { get; set; } = 45f;

    /// <summary>Whether to launch in fullscreen mode.</summary>
    public bool IsFullscreen { get; set; } = true;

    /// <summary>Animation quality preset.</summary>
    public AnimationQuality Quality { get; set; } = AnimationQuality.High;

    /// <summary>Whether the touchpad physical edges have been calibrated.</summary>
    public bool IsCalibrated { get; set; } = false;

    public int CalibratedXMin { get; set; } = 0;
    public int CalibratedXMax { get; set; } = 0;
    public int CalibratedYMin { get; set; } = 0;
    public int CalibratedYMax { get; set; } = 0;

    /// <summary>Background gradient colors as hex strings.</summary>
    public string[] BackgroundColors { get; set; } =
    [
        "#0B1026",
        "#141B4D",
        "#3D2C8D",
        "#6A3DE8",
        "#845EF7"
    ];

    /// <summary>Bloom post-processing intensity (0.0–2.0).</summary>
    public float BloomIntensity { get; set; } = 0.8f;

    /// <summary>Bloom brightness threshold (0.0–1.0).</summary>
    public float BloomThreshold { get; set; } = 0.4f;

    /// <summary>Background animation speed multiplier.</summary>
    public float BackgroundSpeed { get; set; } = 1.0f;

    /// <summary>Cursor auto-hide delay in seconds.</summary>
    public float CursorHideDelay { get; set; } = 3.0f;

    /// <summary>Trail length multiplier (0.0–3.0).</summary>
    public float TrailLength { get; set; } = 1.0f;

    /// <summary>Ripple intensity on tap (0.0–3.0).</summary>
    public float RippleIntensity { get; set; } = 1.0f;

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TouchpadVisualizer");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Load settings from disk, or return defaults if not found.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Fall through to defaults
        }
        return new AppSettings();
    }

    /// <summary>Save current settings to disk.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Silently fail — non-critical
        }
    }
}
