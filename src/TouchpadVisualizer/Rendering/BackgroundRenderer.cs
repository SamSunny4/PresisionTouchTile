using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using TouchpadVisualizer.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace TouchpadVisualizer.Rendering;

/// <summary>
/// Renders the animated gradient background with breathing effect and floating particles.
/// </summary>
public sealed class BackgroundRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct TimeConstants
    {
        public float Time;
        public float Speed;
        public Vector2 Resolution;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ColorConstants
    {
        public Vector4 Color0;
        public Vector4 Color1;
        public Vector4 Color2;
        public Vector4 Color3;
        public Vector4 Color4;
    }

    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _timeBuffer;
    private ID3D11Buffer? _colorBuffer;

    private int _width, _height;
    private float _speed = 1.0f;
    private ColorConstants _colors;

    public void Initialize(ID3D11Device device, int width, int height)
    {
        _width = width;
        _height = height;

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Background.hlsl");

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

        _timeBuffer = D3DRenderer.CreateConstantBuffer(device, Marshal.SizeOf<TimeConstants>());
        _colorBuffer = D3DRenderer.CreateConstantBuffer(device, Marshal.SizeOf<ColorConstants>());

        SetDefaultColors();
    }

    private void SetDefaultColors()
    {
        _colors = new ColorConstants
        {
            Color0 = HexToVector4("#0B1026"),
            Color1 = HexToVector4("#141B4D"),
            Color2 = HexToVector4("#3D2C8D"),
            Color3 = HexToVector4("#6A3DE8"),
            Color4 = HexToVector4("#845EF7")
        };
    }

    public void UpdateSettings(AppSettings settings)
    {
        _speed = settings.BackgroundSpeed;

        if (settings.BackgroundColors.Length >= 5)
        {
            _colors = new ColorConstants
            {
                Color0 = HexToVector4(settings.BackgroundColors[0]),
                Color1 = HexToVector4(settings.BackgroundColors[1]),
                Color2 = HexToVector4(settings.BackgroundColors[2]),
                Color3 = HexToVector4(settings.BackgroundColors[3]),
                Color4 = HexToVector4(settings.BackgroundColors[4])
            };
        }
    }

    public void Render(ID3D11DeviceContext context, ID3D11Buffer quadVB, float time, float deltaTime)
    {
        var timeData = new TimeConstants
        {
            Time = time,
            Speed = _speed,
            Resolution = new Vector2(_width, _height)
        };
        D3DRenderer.UpdateConstantBuffer(context, _timeBuffer!, ref timeData);
        D3DRenderer.UpdateConstantBuffer(context, _colorBuffer!, ref _colors);

        context.IASetInputLayout(_inputLayout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        context.IASetVertexBuffer(0, quadVB, (uint)(4 * sizeof(float)));

        context.VSSetShader(_vertexShader);
        context.VSSetConstantBuffer(0, _timeBuffer);

        context.PSSetShader(_pixelShader);
        context.PSSetConstantBuffer(0, _timeBuffer);
        context.PSSetConstantBuffer(1, _colorBuffer);

        context.Draw(4, 0);
    }

    private static Vector4 HexToVector4(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length < 6) hex = "0B1026";
        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return new Vector4(r / 255f, g / 255f, b / 255f, 1f);
    }

    public void Dispose()
    {
        _vertexShader?.Dispose();
        _pixelShader?.Dispose();
        _inputLayout?.Dispose();
        _timeBuffer?.Dispose();
        _colorBuffer?.Dispose();
    }
}
