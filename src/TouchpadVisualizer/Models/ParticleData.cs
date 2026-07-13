using System.Numerics;

namespace TouchpadVisualizer.Models;

/// <summary>
/// Represents a single GPU particle for the visualization system.
/// Stored in flat arrays for cache-friendly iteration.
/// </summary>
public struct ParticleData
{
    /// <summary>World-space position.</summary>
    public Vector2 Position;

    /// <summary>Velocity in units per second.</summary>
    public Vector2 Velocity;

    /// <summary>Current age in seconds.</summary>
    public float Age;

    /// <summary>Maximum lifetime in seconds.</summary>
    public float Lifetime;

    /// <summary>Base color (RGBA).</summary>
    public Vector4 Color;

    /// <summary>Current size.</summary>
    public float Size;

    /// <summary>Initial size for interpolation.</summary>
    public float InitialSize;

    /// <summary>Rotation angle in radians.</summary>
    public float Rotation;

    /// <summary>Rotation speed in radians per second.</summary>
    public float RotationSpeed;

    /// <summary>Whether this particle slot is active.</summary>
    public bool IsAlive => Age < Lifetime;
}

/// <summary>
/// Vertex data for a particle, sent to the GPU.
/// </summary>
public struct ParticleVertex
{
    public Vector2 Position;
    public Vector2 Size;     // width, height
    public Vector4 Color;
    public float Rotation;
    public float Glow;       // glow intensity
}

/// <summary>
/// Detected gesture type for multi-touch visualization.
/// </summary>
public enum GestureType
{
    None,
    TwoFingerParallel,
    Pinch,
    Spread,
    Rotation,
    ThreeFingerSwipe,
    FourFingerSwipe
}

/// <summary>
/// Describes a currently active gesture for visualization.
/// </summary>
public struct ActiveGesture
{
    public GestureType Type;
    public Vector2 Center;
    public float Scale;      // For pinch/spread: current distance ratio
    public float Angle;      // For rotation: current angle
    public float Intensity;  // Strength of the gesture for visual feedback
}
