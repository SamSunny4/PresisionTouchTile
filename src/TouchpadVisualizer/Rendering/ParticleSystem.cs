using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using TouchpadVisualizer.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace TouchpadVisualizer.Rendering;

/// <summary>
/// GPU-accelerated particle system that emits glowing particles from touch points.
/// Uses instanced rendering for high performance.
/// </summary>
public sealed class ParticleSystem : IDisposable
{
    private const int MAX_PARTICLES = 4096;

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Matrix4x4 ViewProjection;
        public float Time;
        public float GlowIntensity;
        public Vector2 _pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceData
    {
        public Vector2 Center;
        public Vector2 Size;
        public Vector4 Color;
        public float Rotation;
        public float Glow;
        public Vector2 _pad;
    }

    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _quadVB;
    private ID3D11Buffer? _instanceBuffer;
    private ID3D11Buffer? _frameBuffer;

    private ParticleData[] _particles = new ParticleData[MAX_PARTICLES];
    private InstanceData[] _instances = new InstanceData[MAX_PARTICLES];
    private int _activeCount;
    private int _nextSlot;
    private readonly Random _rng = new();

    private int _width, _height;
    private float _density = 1.0f;
    private float _glowIntensity = 1.0f;
    private float _indicatorSize = 1.0f;

    private static readonly Vector4[] FingerColors =
    [
        new(0.6f, 0.4f, 1.0f, 1.0f),
        new(0.4f, 0.6f, 1.0f, 1.0f),
        new(0.3f, 0.9f, 0.8f, 1.0f),
        new(0.9f, 0.4f, 0.8f, 1.0f),
        new(0.5f, 1.0f, 0.5f, 1.0f),
        new(1.0f, 0.6f, 0.3f, 1.0f),
        new(1.0f, 0.3f, 0.5f, 1.0f),
        new(0.8f, 0.8f, 0.3f, 1.0f),
        new(0.3f, 0.5f, 1.0f, 1.0f),
        new(0.7f, 0.3f, 1.0f, 1.0f),
    ];

    public void Initialize(ID3D11Device device, int width, int height)
    {
        _width = width;
        _height = height;

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Particle.hlsl");
        var vsBytecode = D3DRenderer.CompileShader(shaderPath, "VSMain", "vs_5_0");
        var psBytecode = D3DRenderer.CompileShader(shaderPath, "PSMain", "ps_5_0");

        _vertexShader = device.CreateVertexShader(vsBytecode);
        _pixelShader = device.CreatePixelShader(psBytecode);

        var inputElements = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0,
                InputClassification.PerVertexData, 0),
            new InputElementDescription("INST_CENTER", 0, Format.R32G32_Float, 0, 1,
                InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST_SIZE", 0, Format.R32G32_Float, 8, 1,
                InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST_COLOR", 0, Format.R32G32B32A32_Float, 16, 1,
                InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST_ROTATION", 0, Format.R32_Float, 32, 1,
                InputClassification.PerInstanceData, 1),
            new InputElementDescription("INST_GLOW", 0, Format.R32_Float, 36, 1,
                InputClassification.PerInstanceData, 1),
        };
        _inputLayout = device.CreateInputLayout(inputElements, vsBytecode);

        var quadVerts = new float[] { -1, -1, 1, -1, -1, 1, 1, 1 };
        _quadVB = device.CreateBuffer(
            quadVerts.AsSpan(),
            new BufferDescription
            {
                ByteWidth = (uint)(quadVerts.Length * sizeof(float)),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.VertexBuffer
            });

        _instanceBuffer = device.CreateBuffer(new BufferDescription
        {
            ByteWidth = (uint)(MAX_PARTICLES * Marshal.SizeOf<InstanceData>()),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.VertexBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        });

        _frameBuffer = D3DRenderer.CreateConstantBuffer(device, Marshal.SizeOf<FrameConstants>());
    }

    public void UpdateSettings(AppSettings settings)
    {
        _density = settings.ParticleDensity;
        _glowIntensity = settings.GlowIntensity;
        _indicatorSize = settings.TouchIndicatorSize;
    }

    public void UpdateTouches(TouchContact[] contacts, ActiveGesture gesture)
    {
        foreach (var contact in contacts)
        {
            if (!contact.IsDown) continue;

            float speed = contact.Speed;
            int emitCount = (int)(2 + speed * 10 * _density);
            emitCount = Math.Min(emitCount, 20);

            var screenPos = TouchToScreenSpace(contact.NormalizedX, contact.NormalizedY);
            var baseColor = GetFingerColor(contact.ContactId);

            if (gesture.Type == GestureType.ThreeFingerSwipe)
                baseColor = new Vector4(0.3f, 0.9f, 0.9f, 1.0f);
            else if (gesture.Type == GestureType.FourFingerSwipe)
                baseColor = new Vector4(1.0f, 0.5f, 0.2f, 1.0f);

            for (int i = 0; i < emitCount; i++)
                EmitParticle(screenPos, contact.VelocityX, contact.VelocityY, speed, baseColor, contact.Pressure);

            EmitGlowParticle(screenPos, baseColor, contact.Pressure);
        }
    }

    private void EmitParticle(Vector2 position, float velX, float velY,
        float speed, Vector4 color, float pressure)
    {
        ref var p = ref _particles[_nextSlot];
        float angle = (float)(_rng.NextDouble() * Math.PI * 2);
        float spread = 0.01f + speed * 0.02f;

        p.Position = position + new Vector2(
            (float)(_rng.NextDouble() - 0.5) * 0.01f,
            (float)(_rng.NextDouble() - 0.5) * 0.01f);
        p.Velocity = new Vector2(
            MathF.Cos(angle) * spread + velX * 0.002f,
            MathF.Sin(angle) * spread + velY * 0.002f);
        p.Age = 0;
        p.Lifetime = 0.3f + (float)_rng.NextDouble() * 0.7f;
        p.Size = (0.005f + (float)_rng.NextDouble() * 0.01f) * _indicatorSize;
        p.InitialSize = p.Size;
        p.Rotation = (float)(_rng.NextDouble() * Math.PI * 2);
        p.RotationSpeed = ((float)_rng.NextDouble() - 0.5f) * 4f;
        p.Color = color + new Vector4(
            ((float)_rng.NextDouble() - 0.5f) * 0.15f,
            ((float)_rng.NextDouble() - 0.5f) * 0.15f,
            ((float)_rng.NextDouble() - 0.5f) * 0.1f, 0);
        p.Color.W = 0.6f + (float)_rng.NextDouble() * 0.4f;

        _nextSlot = (_nextSlot + 1) % MAX_PARTICLES;
    }

    private void EmitGlowParticle(Vector2 position, Vector4 color, float pressure)
    {
        ref var p = ref _particles[_nextSlot];
        p.Position = position;
        p.Velocity = Vector2.Zero;
        p.Age = 0;
        p.Lifetime = 0.1f;
        p.Size = 0.04f * _indicatorSize * pressure;
        p.InitialSize = p.Size;
        p.Rotation = 0;
        p.RotationSpeed = 0;
        p.Color = color with { W = 0.8f };
        _nextSlot = (_nextSlot + 1) % MAX_PARTICLES;
    }

    public void Render(ID3D11DeviceContext context, float time, float deltaTime)
    {
        _activeCount = 0;
        for (int i = 0; i < MAX_PARTICLES; i++)
        {
            ref var p = ref _particles[i];
            if (!p.IsAlive) continue;

            p.Age += deltaTime;
            if (!p.IsAlive) continue;

            p.Position += p.Velocity * deltaTime;
            p.Velocity *= 0.97f;
            p.Rotation += p.RotationSpeed * deltaTime;

            float lifeRatio = p.Age / p.Lifetime;
            float alpha = 1.0f - lifeRatio * lifeRatio;
            float size = p.InitialSize * (1.0f - lifeRatio * 0.5f);

            _instances[_activeCount] = new InstanceData
            {
                Center = p.Position,
                Size = new Vector2(size, size),
                Color = p.Color with { W = p.Color.W * alpha },
                Rotation = p.Rotation,
                Glow = (1.0f - lifeRatio) * 2.0f
            };
            _activeCount++;
        }

        if (_activeCount == 0) return;

        // Upload instance data
        unsafe
        {
            var mapped = context.Map(_instanceBuffer!, MapMode.WriteDiscard);
            try
            {
                fixed (InstanceData* src = _instances)
                {
                    Buffer.MemoryCopy(src, (void*)mapped.DataPointer,
                        mapped.RowPitch, _activeCount * Marshal.SizeOf<InstanceData>());
                }
            }
            finally
            {
                context.Unmap(_instanceBuffer!, 0);
            }
        }

        float aspect = (float)_width / _height;
        var projection = Matrix4x4.CreateOrthographic(2f * aspect, 2f, -1f, 1f);

        var frameData = new FrameConstants
        {
            ViewProjection = Matrix4x4.Transpose(projection),
            Time = time,
            GlowIntensity = _glowIntensity
        };
        D3DRenderer.UpdateConstantBuffer(context, _frameBuffer!, ref frameData);

        context.IASetInputLayout(_inputLayout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);

        // Bind vertex buffers: slot 0 = quad, slot 1 = instances
        uint quadStride = 2 * sizeof(float);
        uint instanceStride = (uint)Marshal.SizeOf<InstanceData>();
        context.IASetVertexBuffers(0,
            new[] { _quadVB!, _instanceBuffer! },
            new[] { quadStride, instanceStride },
            new uint[] { 0, 0 });

        context.VSSetShader(_vertexShader);
        context.VSSetConstantBuffer(0, _frameBuffer);

        context.PSSetShader(_pixelShader);
        context.PSSetConstantBuffer(0, _frameBuffer);

        context.DrawInstanced(4, (uint)_activeCount, 0, 0);
    }

    private static Vector2 TouchToScreenSpace(float normX, float normY)
    {
        return new Vector2(
            (normX - 0.5f) * 0.9f,
            (0.5f - normY) * 0.5f - 0.15f
        );
    }

    private static Vector4 GetFingerColor(int contactId)
    {
        int idx = Math.Abs(contactId) % FingerColors.Length;
        return FingerColors[idx];
    }

    public void Dispose()
    {
        _vertexShader?.Dispose();
        _pixelShader?.Dispose();
        _inputLayout?.Dispose();
        _quadVB?.Dispose();
        _instanceBuffer?.Dispose();
        _frameBuffer?.Dispose();
    }
}
