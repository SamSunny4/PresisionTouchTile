namespace TouchpadVisualizer.Models;

/// <summary>
/// Represents a single touch contact on the touchpad at a point in time.
/// Coordinates are normalized to 0.0–1.0 range relative to the touchpad surface.
/// </summary>
public record struct TouchContact
{
    /// <summary>Unique contact ID assigned by the hardware for tracking across frames.</summary>
    public int ContactId { get; init; }

    /// <summary>Raw un-normalized X position directly from the HID report.</summary>
    public int RawX { get; init; }

    /// <summary>Raw un-normalized Y position directly from the HID report.</summary>
    public int RawY { get; init; }

    /// <summary>Normalized X position (0.0 = left edge, 1.0 = right edge).</summary>
    public float NormalizedX { get; init; }

    /// <summary>Normalized Y position (0.0 = top edge, 1.0 = bottom edge).</summary>
    public float NormalizedY { get; init; }

    /// <summary>Whether the finger is currently in contact with the surface.</summary>
    public bool IsDown { get; init; }

    /// <summary>Pressure value if available (0.0–1.0). Defaults to 1.0 if unsupported.</summary>
    public float Pressure { get; init; }

    /// <summary>Instantaneous velocity in X (normalized units per frame).</summary>
    public float VelocityX { get; init; }

    /// <summary>Instantaneous velocity in Y (normalized units per frame).</summary>
    public float VelocityY { get; init; }

    /// <summary>Timestamp in ticks when this contact was recorded.</summary>
    public long Timestamp { get; init; }

    /// <summary>The speed magnitude derived from velocity components.</summary>
    public float Speed => MathF.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
}
