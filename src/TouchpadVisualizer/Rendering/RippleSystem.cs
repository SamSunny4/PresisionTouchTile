using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using TouchpadVisualizer.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace TouchpadVisualizer.Rendering;

/// <summary>
/// Renders expanding ring ripple/shockwave effects at touch-down positions.
/// </summary>
public sealed class RippleSystem : IDisposable
{
    private const int MAX_RIPPLES = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct RippleConstants
    {
        public Matrix4x4 ViewProjection;
        public float Time;
        public float GlowIntensity;
        public Vector2 _pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RippleInstance
    {
        public Vector2 Center;
        public float StartTime;
        public float Intensity;
        public Vector4 Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RippleDataConstants
    {
        public RippleInstance R0, R1, R2, R3, R4, R5, R6, R7;
        public RippleInstance R8, R9, R10, R11, R12, R13, R14, R15;
        public RippleInstance R16, R17, R18, R19, R20, R21, R22, R23;
        public RippleInstance R24, R25, R26, R27, R28, R29, R30, R31;
        public int RippleCount;
        public Vector3 _pad;
    }

    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _rippleBuffer;
    private ID3D11Buffer? _rippleDataBuffer;

    private RippleInstance[] _ripples = new RippleInstance[MAX_RIPPLES];
    private int _rippleCount;
    private int _nextSlot;

    private int _width, _height;
    private float _glowIntensity = 1.0f;
    private float _rippleIntensity = 1.0f;

    public void Initialize(ID3D11Device device, int width, int height)
    {
        _width = width;
        _height = height;

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Ripple.hlsl");
        var vsBytecode = D3DRenderer.CompileShader(shaderPath, "VSMain", "vs_5_0");
        var psBytecode = D3DRenderer.CompileShader(shaderPath, "PSMain", "ps_5_0");

        _vertexShader = device.CreateVertexShader(vsBytecode);
        _pixelShader = device.CreatePixelShader(psBytecode);

        var inputElements = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0)
        };
        _inputLayout = device.CreateInputLayout(inputElements, vsBytecode);

        _rippleBuffer = D3DRenderer.CreateConstantBuffer(device, Marshal.SizeOf<RippleConstants>());
        _rippleDataBuffer = D3DRenderer.CreateConstantBuffer(device, Marshal.SizeOf<RippleDataConstants>());
    }

    public void UpdateSettings(AppSettings settings)
    {
        _glowIntensity = settings.GlowIntensity;
        _rippleIntensity = settings.RippleIntensity;
    }

    public void AddRipple(float normX, float normY, float intensity)
    {
        lock (_ripples)
        {
            _ripples[_nextSlot] = new RippleInstance
            {
                Center = new Vector2(normX, normY),
                StartTime = -1,
                Intensity = intensity * _rippleIntensity,
                Color = new Vector4(0.5f, 0.3f, 1.0f, 1.0f)
            };
            _nextSlot = (_nextSlot + 1) % MAX_RIPPLES;
            _rippleCount = Math.Min(_rippleCount + 1, MAX_RIPPLES);
        }
    }

    public void Render(ID3D11DeviceContext context, ID3D11Buffer quadVB, float time, float deltaTime)
    {
        int activeCount = 0;
        var activeRipples = new RippleInstance[MAX_RIPPLES];

        lock (_ripples)
        {
            for (int i = 0; i < _rippleCount; i++)
            {
                ref var r = ref _ripples[i];
                if (r.StartTime < 0) r.StartTime = time;
                if (time - r.StartTime < 2.0f)
                {
                    activeRipples[activeCount++] = r;
                }
            }
        }

        if (activeCount == 0) return;

        var constants = new RippleConstants
        {
            ViewProjection = Matrix4x4.Identity,
            Time = time,
            GlowIntensity = _glowIntensity
        };
        D3DRenderer.UpdateConstantBuffer(context, _rippleBuffer!, ref constants);

        var rippleData = new RippleDataConstants { RippleCount = activeCount };
        unsafe
        {
            var ptr = (RippleInstance*)&rippleData;
            for (int i = 0; i < Math.Min(activeCount, 32); i++)
            {
                ptr[i] = activeRipples[i];
            }
        }
        D3DRenderer.UpdateConstantBuffer(context, _rippleDataBuffer!, ref rippleData);

        context.IASetInputLayout(_inputLayout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        context.IASetVertexBuffer(0, quadVB, (uint)(4 * sizeof(float)));

        context.VSSetShader(_vertexShader);
        context.VSSetConstantBuffer(0, _rippleBuffer);

        context.PSSetShader(_pixelShader);
        context.PSSetConstantBuffer(0, _rippleBuffer);
        context.PSSetConstantBuffer(1, _rippleDataBuffer);

        context.Draw(4, 0);
    }

    public void Dispose()
    {
        _vertexShader?.Dispose();
        _pixelShader?.Dispose();
        _inputLayout?.Dispose();
        _rippleBuffer?.Dispose();
        _rippleDataBuffer?.Dispose();
    }
}
