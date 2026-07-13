using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using TouchpadVisualizer.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace TouchpadVisualizer.Rendering;

/// <summary>
/// Renders fading motion trails behind moving touch points.
/// Each contact maintains a ring buffer of positions that form a ribbon.
/// </summary>
public sealed class TrailSystem : IDisposable
{
    private const int MAX_TRAIL_POINTS = 64;
    private const int MAX_CONTACTS = 10;
    private const int MAX_VERTICES = MAX_CONTACTS * MAX_TRAIL_POINTS * 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct TrailConstants
    {
        public Matrix4x4 ViewProjection;
        public float GlowIntensity;
        public float TrailWidth;
        public Vector2 _pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrailVertex
    {
        public Vector2 Position;
        public Vector2 Normal;
        public Vector4 Color;
        public float Age;
        public float Side;
        public Vector2 _pad;
    }

    private class ContactTrail
    {
        public Vector2[] Points = new Vector2[MAX_TRAIL_POINTS];
        public int Count;
        public int Head;
        public Vector4 Color;
        public bool Active;

        public void AddPoint(Vector2 p)
        {
            Points[Head] = p;
            Head = (Head + 1) % MAX_TRAIL_POINTS;
            Count = Math.Min(Count + 1, MAX_TRAIL_POINTS);
        }
    }

    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _constantBuffer;

    private readonly Dictionary<int, ContactTrail> _trails = new();
    private TrailVertex[] _vertices = new TrailVertex[MAX_VERTICES];

    private int _width, _height;
    private float _glowIntensity = 1.0f;
    private float _trailLength = 1.0f;

    private static readonly Vector4[] FingerColors =
    [
        new(0.6f, 0.4f, 1.0f, 0.8f),
        new(0.4f, 0.6f, 1.0f, 0.8f),
        new(0.3f, 0.9f, 0.8f, 0.8f),
        new(0.9f, 0.4f, 0.8f, 0.8f),
        new(0.5f, 1.0f, 0.5f, 0.8f),
        new(1.0f, 0.6f, 0.3f, 0.8f),
        new(1.0f, 0.3f, 0.5f, 0.8f),
        new(0.8f, 0.8f, 0.3f, 0.8f),
        new(0.3f, 0.5f, 1.0f, 0.8f),
        new(0.7f, 0.3f, 1.0f, 0.8f),
    ];

    public void Initialize(ID3D11Device device, int width, int height)
    {
        _width = width;
        _height = height;

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Trail.hlsl");
        var vsBytecode = D3DRenderer.CompileShader(shaderPath, "VSMain", "vs_5_0");
        var psBytecode = D3DRenderer.CompileShader(shaderPath, "PSMain", "ps_5_0");

        _vertexShader = device.CreateVertexShader(vsBytecode);
        _pixelShader = device.CreatePixelShader(psBytecode);

        var inputElements = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32_Float, 8, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 16, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32_Float, 32, 0),
            new InputElementDescription("TEXCOORD", 1, Format.R32_Float, 36, 0),
        };
        _inputLayout = device.CreateInputLayout(inputElements, vsBytecode);

        _vertexBuffer = device.CreateBuffer(new BufferDescription
        {
            ByteWidth = (uint)(MAX_VERTICES * Marshal.SizeOf<TrailVertex>()),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.VertexBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        });

        _constantBuffer = D3DRenderer.CreateConstantBuffer(device, Marshal.SizeOf<TrailConstants>());
    }

    public void UpdateSettings(AppSettings settings)
    {
        _glowIntensity = settings.GlowIntensity;
        _trailLength = settings.TrailLength;
    }

    public void UpdateTouches(TouchContact[] contacts)
    {
        var activeIds = new HashSet<int>();

        foreach (var contact in contacts)
        {
            if (!contact.IsDown) continue;
            activeIds.Add(contact.ContactId);

            var screenPos = TouchToScreenSpace(contact.NormalizedX, contact.NormalizedY);

            if (!_trails.TryGetValue(contact.ContactId, out var trail))
            {
                trail = new ContactTrail
                {
                    Color = FingerColors[Math.Abs(contact.ContactId) % FingerColors.Length]
                };
                _trails[contact.ContactId] = trail;
            }

            trail.Active = true;
            trail.AddPoint(screenPos);
        }

        foreach (var kvp in _trails)
        {
            if (!activeIds.Contains(kvp.Key))
                kvp.Value.Active = false;
        }

        var toRemove = _trails.Where(kvp => !kvp.Value.Active && kvp.Value.Count <= 1)
            .Select(kvp => kvp.Key).ToList();
        foreach (var key in toRemove)
            _trails.Remove(key);
    }

    public void Render(ID3D11DeviceContext context, float time, float deltaTime)
    {
        int totalVertices = 0;

        foreach (var trail in _trails.Values)
        {
            if (trail.Count < 2) continue;

            if (!trail.Active && trail.Count > 0)
                trail.Count = Math.Max(0, trail.Count - 2);

            int maxPoints = (int)(MAX_TRAIL_POINTS * _trailLength);
            int pointCount = Math.Min(trail.Count, maxPoints);
            if (pointCount < 2) continue;

            for (int i = 0; i < pointCount - 1 && totalVertices < MAX_VERTICES - 2; i++)
            {
                int idx = (trail.Head - pointCount + i + MAX_TRAIL_POINTS) % MAX_TRAIL_POINTS;
                int nextIdx = (idx + 1) % MAX_TRAIL_POINTS;

                var p0 = trail.Points[idx];
                var p1 = trail.Points[nextIdx];

                var diff = p1 - p0;
                var len = diff.Length();
                var dir = len > 0.0001f ? diff / len : Vector2.UnitX;
                var normal = new Vector2(-dir.Y, dir.X);
                float age = (float)i / (pointCount - 1);

                _vertices[totalVertices++] = new TrailVertex
                {
                    Position = p0, Normal = normal, Color = trail.Color,
                    Age = age, Side = -1.0f
                };
                _vertices[totalVertices++] = new TrailVertex
                {
                    Position = p0, Normal = normal, Color = trail.Color,
                    Age = age, Side = 1.0f
                };
            }
        }

        if (totalVertices == 0) return;

        unsafe
        {
            var mapped = context.Map(_vertexBuffer!, MapMode.WriteDiscard);
            try
            {
                fixed (TrailVertex* src = _vertices)
                {
                    Buffer.MemoryCopy(src, (void*)mapped.DataPointer,
                        mapped.RowPitch, totalVertices * Marshal.SizeOf<TrailVertex>());
                }
            }
            finally
            {
                context.Unmap(_vertexBuffer!, 0);
            }
        }

        float aspect = (float)_width / _height;
        var projection = Matrix4x4.CreateOrthographic(2f * aspect, 2f, -1f, 1f);

        var constants = new TrailConstants
        {
            ViewProjection = Matrix4x4.Transpose(projection),
            GlowIntensity = _glowIntensity,
            TrailWidth = 0.008f
        };
        D3DRenderer.UpdateConstantBuffer(context, _constantBuffer!, ref constants);

        context.IASetInputLayout(_inputLayout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        context.IASetVertexBuffer(0, _vertexBuffer!, (uint)Marshal.SizeOf<TrailVertex>());

        context.VSSetShader(_vertexShader);
        context.VSSetConstantBuffer(0, _constantBuffer);

        context.PSSetShader(_pixelShader);
        context.PSSetConstantBuffer(0, _constantBuffer);

        context.Draw((uint)totalVertices, 0);
    }

    private static Vector2 TouchToScreenSpace(float normX, float normY)
    {
        return new Vector2(
            (normX - 0.5f) * 0.9f,
            (0.5f - normY) * 0.5f - 0.15f
        );
    }

    public void Dispose()
    {
        _vertexShader?.Dispose();
        _pixelShader?.Dispose();
        _inputLayout?.Dispose();
        _vertexBuffer?.Dispose();
        _constantBuffer?.Dispose();
    }
}
