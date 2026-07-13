using System.Numerics;
using TouchpadVisualizer.Models;

namespace TouchpadVisualizer.Input;

/// <summary>
/// Analyzes active touch contacts to detect multi-finger gestures.
/// Produces gesture metadata that drives specialized visual effects.
/// </summary>
public class GestureDetector
{
    private readonly Dictionary<int, Vector2> _prevPositions = new();
    private ActiveGesture _currentGesture;

    /// <summary>Current detected gesture.</summary>
    public ActiveGesture CurrentGesture => _currentGesture;

    /// <summary>Number of active contacts.</summary>
    public int ActiveFingerCount { get; private set; }

    /// <summary>
    /// Update gesture detection with the latest contacts.
    /// </summary>
    public void Update(TouchContact[] contacts)
    {
        var activeContacts = contacts.Where(c => c.IsDown).ToArray();
        ActiveFingerCount = activeContacts.Length;

        if (activeContacts.Length < 2)
        {
            _currentGesture = new ActiveGesture { Type = GestureType.None };
            UpdatePreviousPositions(activeContacts);
            return;
        }

        // Compute center of all contacts
        var center = new Vector2(
            activeContacts.Average(c => c.NormalizedX),
            activeContacts.Average(c => c.NormalizedY));

        if (activeContacts.Length == 2)
        {
            DetectTwoFingerGesture(activeContacts, center);
        }
        else if (activeContacts.Length == 3)
        {
            _currentGesture = new ActiveGesture
            {
                Type = GestureType.ThreeFingerSwipe,
                Center = center,
                Intensity = CalculateMovementIntensity(activeContacts)
            };
        }
        else if (activeContacts.Length >= 4)
        {
            _currentGesture = new ActiveGesture
            {
                Type = GestureType.FourFingerSwipe,
                Center = center,
                Intensity = CalculateMovementIntensity(activeContacts)
            };
        }

        UpdatePreviousPositions(activeContacts);
    }

    private void DetectTwoFingerGesture(TouchContact[] contacts, Vector2 center)
    {
        var a = new Vector2(contacts[0].NormalizedX, contacts[0].NormalizedY);
        var b = new Vector2(contacts[1].NormalizedX, contacts[1].NormalizedY);

        float currentDist = Vector2.Distance(a, b);

        if (!_prevPositions.TryGetValue(contacts[0].ContactId, out var prevA) ||
            !_prevPositions.TryGetValue(contacts[1].ContactId, out var prevB))
        {
            _currentGesture = new ActiveGesture
            {
                Type = GestureType.TwoFingerParallel,
                Center = center,
                Scale = 1f,
                Intensity = 0f
            };
            return;
        }

        float prevDist = Vector2.Distance(prevA, prevB);
        float distDelta = currentDist - prevDist;

        // Check for rotation
        var prevAngle = MathF.Atan2(prevB.Y - prevA.Y, prevB.X - prevA.X);
        var currAngle = MathF.Atan2(b.Y - a.Y, b.X - a.X);
        float angleDelta = currAngle - prevAngle;

        // Normalize angle delta
        while (angleDelta > MathF.PI) angleDelta -= 2 * MathF.PI;
        while (angleDelta < -MathF.PI) angleDelta += 2 * MathF.PI;

        float pinchThreshold = 0.001f;
        float rotationThreshold = 0.01f;

        if (MathF.Abs(angleDelta) > rotationThreshold && MathF.Abs(angleDelta) > MathF.Abs(distDelta) * 10)
        {
            _currentGesture = new ActiveGesture
            {
                Type = GestureType.Rotation,
                Center = center,
                Angle = currAngle,
                Intensity = MathF.Abs(angleDelta) * 50
            };
        }
        else if (distDelta < -pinchThreshold)
        {
            _currentGesture = new ActiveGesture
            {
                Type = GestureType.Pinch,
                Center = center,
                Scale = currentDist / Math.Max(prevDist, 0.001f),
                Intensity = MathF.Abs(distDelta) * 100
            };
        }
        else if (distDelta > pinchThreshold)
        {
            _currentGesture = new ActiveGesture
            {
                Type = GestureType.Spread,
                Center = center,
                Scale = currentDist / Math.Max(prevDist, 0.001f),
                Intensity = MathF.Abs(distDelta) * 100
            };
        }
        else
        {
            _currentGesture = new ActiveGesture
            {
                Type = GestureType.TwoFingerParallel,
                Center = center,
                Scale = 1f,
                Intensity = CalculateMovementIntensity(contacts)
            };
        }
    }

    private float CalculateMovementIntensity(TouchContact[] contacts)
    {
        float totalSpeed = 0;
        foreach (var c in contacts)
        {
            totalSpeed += c.Speed;
        }
        return Math.Min(totalSpeed / contacts.Length, 5f);
    }

    private void UpdatePreviousPositions(TouchContact[] contacts)
    {
        // Remove stale entries
        var activeIds = new HashSet<int>(contacts.Select(c => c.ContactId));
        var toRemove = _prevPositions.Keys.Where(k => !activeIds.Contains(k)).ToList();
        foreach (var k in toRemove)
            _prevPositions.Remove(k);

        // Update
        foreach (var c in contacts)
        {
            _prevPositions[c.ContactId] = new Vector2(c.NormalizedX, c.NormalizedY);
        }
    }

    /// <summary>Reset all state.</summary>
    public void Reset()
    {
        _prevPositions.Clear();
        _currentGesture = default;
        ActiveFingerCount = 0;
    }
}
