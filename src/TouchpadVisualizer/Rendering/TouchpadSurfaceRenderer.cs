using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using TouchpadVisualizer.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace TouchpadVisualizer.Rendering;

/// <summary>
/// Renders the virtual touchpad surface — a tilted rounded rectangle with
/// glass/frosted material, metallic border, reflections, and touch point highlights.
/// </summary>
public sealed class TouchpadSurfaceRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct TouchpadConstants
    {
        public Matrix4x4 WorldViewProjection;
        public Matrix4x4 World;
        public float Time;
        public float GlowIntensity;
        public Vector2 TouchpadSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TouchConstants
    {
        public Vector4 Touch0, Touch1, Touch2, Touch3, Touch4;
        public Vector4 Touch5, Touch6, Touch7, Touch8, Touch9;
        public int ActiveTouchCount;
        public Vector3 _pad;
    }

    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _indexBuffer;
    private ID3D11Buffer? _touchpadBuffer;
    private ID3D11Buffer? _touchBuffer;

    private int _width, _height;
    private float _perspectiveAngle = 45f;
    private float _glowIntensity = 1.0f;
    private int _indexCount;
    private TouchConstants _touchData;

    public void Initialize(ID3D11Device device, int width, int height)
    {
        _width = width;
        _height = height;

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Touchpad.hlsl");

        var vsBytecode = D3DRenderer.CompileShader(shaderPath, "VSMain", "vs_5_0");
        var psBytecode = D3DRenderer.CompileShader(shaderPath, "PSMain", "ps_5_0");

        _vertexShader = device.CreateVertexShader(vsBytecode);
        _pixelShader = device.CreatePixelShader(psBytecode);

        var inputElements = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 20, 0)
        };
        _inputLayout = device.CreateInputLayout(inputElements, vsBytecode);

        CreateTouchpadMesh(device);

        _touchpadBuffer = D3DRenderer.CreateConstantBuffer(device, Marshal.SizeOf<TouchpadConstants>());
        _touchBuffer = D3DRenderer.CreateConstantBuffer(device, Marshal.SizeOf<TouchConstants>());
    }

    private void CreateTouchpadMesh(ID3D11Device device)
    {
        float padW = 0.55f;
        float padH = padW * 0.65f;

        // Vertex: Position(3) + TexCoord(2) + Normal(3) = 8 floats
        var vertices = new float[]
        {
            -padW, -padH, 0, 0, 1, 0, 0, 1,
             padW, -padH, 0, 1, 1, 0, 0, 1,
            -padW,  padH, 0, 0, 0, 0, 0, 1,
             padW,  padH, 0, 1, 0, 0, 0, 1,
        };

        var indices = new ushort[] { 0, 1, 2, 2, 1, 3 };
        _indexCount = indices.Length;

        _vertexBuffer = device.CreateBuffer(
            vertices.AsSpan(),
            new BufferDescription
            {
                ByteWidth = (uint)(vertices.Length * sizeof(float)),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.VertexBuffer
            });

        _indexBuffer = device.CreateBuffer(
            indices.AsSpan(),
            new BufferDescription
            {
                ByteWidth = (uint)(indices.Length * sizeof(ushort)),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.IndexBuffer
            });
    }

    public void UpdateSettings(AppSettings settings)
    {
        _perspectiveAngle = settings.PerspectiveAngle;
        _glowIntensity = settings.GlowIntensity;
    }

    public void UpdateTouches(TouchContact[] contacts)
    {
        _touchData = new TouchConstants();
        int count = Math.Min(contacts.Length, 10);
        _touchData.ActiveTouchCount = 0;

        for (int i = 0; i < count; i++)
        {
            var c = contacts[i];
            if (!c.IsDown) continue;

            var touchVec = new Vector4(c.NormalizedX, c.NormalizedY, 1.0f, 1.0f);

            switch (_touchData.ActiveTouchCount)
            {
                case 0: _touchData.Touch0 = touchVec; break;
                case 1: _touchData.Touch1 = touchVec; break;
                case 2: _touchData.Touch2 = touchVec; break;
                case 3: _touchData.Touch3 = touchVec; break;
                case 4: _touchData.Touch4 = touchVec; break;
                case 5: _touchData.Touch5 = touchVec; break;
                case 6: _touchData.Touch6 = touchVec; break;
                case 7: _touchData.Touch7 = touchVec; break;
                case 8: _touchData.Touch8 = touchVec; break;
                case 9: _touchData.Touch9 = touchVec; break;
            }
            _touchData.ActiveTouchCount++;
        }
    }

    public void Render(ID3D11DeviceContext context, ID3D11Buffer quadVB, float time, float deltaTime)
    {
        float aspect = (float)_width / _height;
        float tiltRad = _perspectiveAngle * MathF.PI / 180f;

        var world = Matrix4x4.CreateRotationX(-tiltRad * 0.6f) *
                    Matrix4x4.CreateTranslation(0, -0.05f, 0);

        var view = Matrix4x4.CreateLookAt(
            new Vector3(0, 0.8f, 2.2f),
            new Vector3(0, -0.1f, 0),
            Vector3.UnitY);

        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4f, aspect, 0.1f, 100f);

        var wvp = world * view * projection;

        var constants = new TouchpadConstants
        {
            WorldViewProjection = Matrix4x4.Transpose(wvp),
            World = Matrix4x4.Transpose(world),
            Time = time,
            GlowIntensity = _glowIntensity,
            TouchpadSize = new Vector2(0.55f, 0.55f * 0.65f)
        };

        D3DRenderer.UpdateConstantBuffer(context, _touchpadBuffer!, ref constants);
        D3DRenderer.UpdateConstantBuffer(context, _touchBuffer!, ref _touchData);

        context.IASetInputLayout(_inputLayout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.IASetVertexBuffer(0, _vertexBuffer!, (uint)(8 * sizeof(float)));
        context.IASetIndexBuffer(_indexBuffer!, Format.R16_UInt, 0);

        context.VSSetShader(_vertexShader);
        context.VSSetConstantBuffer(0, _touchpadBuffer);

        context.PSSetShader(_pixelShader);
        context.PSSetConstantBuffer(0, _touchpadBuffer);
        context.PSSetConstantBuffer(1, _touchBuffer);

        context.DrawIndexed((uint)_indexCount, 0, 0);
    }

    public void Dispose()
    {
        _vertexShader?.Dispose();
        _pixelShader?.Dispose();
        _inputLayout?.Dispose();
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _touchpadBuffer?.Dispose();
        _touchBuffer?.Dispose();
    }
}
