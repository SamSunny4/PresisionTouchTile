using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TouchpadVisualizer.Input;
using TouchpadVisualizer.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.D3DCompiler;
using Vortice.Mathematics;

namespace TouchpadVisualizer.Rendering;

/// <summary>
/// Core Direct3D 11 renderer that manages the GPU pipeline, render targets,
/// and coordinates all rendering subsystems. Interops with WPF via D3DImage
/// for seamless integration.
/// </summary>
public sealed class D3DRenderer : IDisposable
{
    // D3D11 Core
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;

    // Render targets
    private ID3D11Texture2D? _renderTarget;
    private ID3D11RenderTargetView? _renderTargetView;

    // Shared surface for WPF interop
    private ID3D11Texture2D? _sharedTexture;
    private IntPtr _sharedHandle;

    // D3D9 Interop for WPF D3DImage
    private Vortice.Direct3D9.IDirect3D9Ex? _d3d9;
    private Vortice.Direct3D9.IDirect3DDevice9Ex? _d3d9Device;
    private Vortice.Direct3D9.IDirect3DTexture9? _d3d9Texture;
    private Vortice.Direct3D9.IDirect3DSurface9? _d3d9Surface;
    private IntPtr _d3d9SurfacePointer;

    // Render state
    private ID3D11BlendState? _additiveBlendState;
    private ID3D11BlendState? _alphaBlendState;
    private ID3D11SamplerState? _linearSampler;
    private ID3D11RasterizerState? _rasterizerState;
    private ID3D11DepthStencilState? _noDepthState;

    // Full-screen quad
    private ID3D11Buffer? _quadVertexBuffer;

    // Subsystems
    private BackgroundRenderer? _backgroundRenderer;
    private TouchpadSurfaceRenderer? _touchpadRenderer;
    private ParticleSystem? _particleSystem;
    private RippleSystem? _rippleSystem;
    private TrailSystem? _trailSystem;

    // State
    private int _width;
    private int _height;
    private bool _isInitialized;
    private bool _disposed;
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private AppSettings _settings = new();

    // Frame timing
    private long _lastFrameTime;
    private float _currentFps;
    private int _frameCount;
    private long _fpsTimer;

    // Render synchronization for D3DImage interop
    private readonly object _renderLock = new();
    private volatile bool _isFrameReady;

    /// <summary>Current frames per second.</summary>
    public float CurrentFps => _currentFps;

    /// <summary>Whether the renderer has been successfully initialized.</summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Initialize the D3D11 device and all rendering resources.
    /// </summary>
    public bool Initialize(int width, int height)
    {
        _width = width;
        _height = height;

        try
        {
            // Create D3D11 device
            var featureLevels = new[]
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0
            };

            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            factory.EnumAdapters1(0, out var adapter);

            D3D11.D3D11CreateDevice(
                adapter,
                adapter == null ? DriverType.Hardware : DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out _device,
                out _context);

            adapter?.Dispose();

            if (_device == null || _context == null)
            {
                Debug.WriteLine("[D3DRenderer] Failed to create D3D11 device");
                return false;
            }

            Vortice.Direct3D9.D3D9.Direct3DCreate9Ex(out _d3d9);
            if (_d3d9 != null)
            {
                // Use the desktop window handle — IntPtr.Zero causes D3D9 device creation to fail silently
                IntPtr desktopHwnd = HidInterop.GetDesktopWindow();

                var presentParams = new Vortice.Direct3D9.PresentParameters
                {
                    Windowed = true,
                    SwapEffect = Vortice.Direct3D9.SwapEffect.Discard,
                    DeviceWindowHandle = desktopHwnd,
                    PresentationInterval = Vortice.Direct3D9.PresentInterval.Default,
                    BackBufferWidth = 1,
                    BackBufferHeight = 1,
                    BackBufferFormat = Vortice.Direct3D9.Format.Unknown
                };

                _d3d9Device = _d3d9.CreateDeviceEx(
                    0,
                    Vortice.Direct3D9.DeviceType.Hardware,
                    desktopHwnd,
                    Vortice.Direct3D9.CreateFlags.HardwareVertexProcessing | Vortice.Direct3D9.CreateFlags.Multithreaded | Vortice.Direct3D9.CreateFlags.FpuPreserve,
                    presentParams);
            }

            CreateRenderTargets();
            CreateRenderStates();
            CreateQuadGeometry();
            InitializeSubsystems();

            _isInitialized = true;
            _lastFrameTime = _timer.ElapsedTicks;
            _fpsTimer = _timer.ElapsedTicks;

            Debug.WriteLine($"[D3DRenderer] Initialized: {width}x{height}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[D3DRenderer] Initialization failed: {ex.Message}");
            return false;
        }
    }

    private void CreateRenderTargets()
    {
        // Main render target
        var rtDesc = new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        _renderTarget = _device!.CreateTexture2D(rtDesc);
        _renderTargetView = _device.CreateRenderTargetView(_renderTarget);

        // Shared texture for WPF interop
        var sharedDesc = rtDesc;
        sharedDesc.MiscFlags = ResourceOptionFlags.Shared;
        _sharedTexture = _device.CreateTexture2D(sharedDesc);

        // Get shared handle
        using var dxgiResource = _sharedTexture.QueryInterface<IDXGIResource>();
        _sharedHandle = dxgiResource.SharedHandle;

        if (_d3d9Device != null && _sharedHandle != IntPtr.Zero)
        {
            IntPtr handle = _sharedHandle;
            _d3d9Texture = _d3d9Device.CreateTexture((uint)_width, (uint)_height, 1, Vortice.Direct3D9.Usage.RenderTarget, Vortice.Direct3D9.Format.A8R8G8B8, Vortice.Direct3D9.Pool.Default, ref handle);
            if (_d3d9Texture != null)
            {
                _d3d9Surface = _d3d9Texture.GetSurfaceLevel(0);
                if (_d3d9Surface != null)
                {
                    _d3d9SurfacePointer = _d3d9Surface.NativePointer;
                }
            }
        }
    }

    private void CreateRenderStates()
    {
        // Additive blend state for particles
        var blendDesc = new BlendDescription();
        blendDesc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.SourceAlpha,
            DestinationBlend = Blend.One,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.One,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All
        };
        _additiveBlendState = _device!.CreateBlendState(blendDesc);

        // Alpha blend state for UI elements
        blendDesc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.SourceAlpha,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All
        };
        _alphaBlendState = _device.CreateBlendState(blendDesc);

        // Linear sampler
        _linearSampler = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        });

        // Rasterizer state
        _rasterizerState = _device.CreateRasterizerState(new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            FrontCounterClockwise = false,
            DepthClipEnable = false
        });

        // No depth testing
        _noDepthState = _device.CreateDepthStencilState(new DepthStencilDescription
        {
            DepthEnable = false,
            DepthWriteMask = DepthWriteMask.Zero,
            StencilEnable = false
        });
    }

    private void CreateQuadGeometry()
    {
        // Full-screen quad: position (x,y) + texcoord (u,v)
        var vertices = new float[]
        {
            -1f, -1f,  0f, 1f,  // Bottom-left
             1f, -1f,  1f, 1f,  // Bottom-right
            -1f,  1f,  0f, 0f,  // Top-left
             1f,  1f,  1f, 0f,  // Top-right
        };

        _quadVertexBuffer = _device!.CreateBuffer(
            vertices.AsSpan(),
            new BufferDescription
            {
                ByteWidth = (uint)(vertices.Length * sizeof(float)),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.VertexBuffer
            });
    }

    private void InitializeSubsystems()
    {
        _backgroundRenderer = new BackgroundRenderer();
        _backgroundRenderer.Initialize(_device!, _width, _height);

        _touchpadRenderer = new TouchpadSurfaceRenderer();
        _touchpadRenderer.Initialize(_device!, _width, _height);

        _particleSystem = new ParticleSystem();
        _particleSystem.Initialize(_device!, _width, _height);

        _rippleSystem = new RippleSystem();
        _rippleSystem.Initialize(_device!, _width, _height);

        _trailSystem = new TrailSystem();
        _trailSystem.Initialize(_device!, _width, _height);
    }

    /// <summary>Update application settings.</summary>
    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _backgroundRenderer?.UpdateSettings(settings);
        _touchpadRenderer?.UpdateSettings(settings);
        _particleSystem?.UpdateSettings(settings);
        _rippleSystem?.UpdateSettings(settings);
        _trailSystem?.UpdateSettings(settings);
    }

    /// <summary>Update touch state for visualization.</summary>
    public void UpdateTouches(TouchContact[] contacts, ActiveGesture gesture)
    {
        _touchpadRenderer?.UpdateTouches(contacts);
        _particleSystem?.UpdateTouches(contacts, gesture);
        _trailSystem?.UpdateTouches(contacts);
    }

    /// <summary>Add a ripple effect at the given normalized position.</summary>
    public void AddRipple(float x, float y, float intensity)
    {
        _rippleSystem?.AddRipple(x, y, intensity);
    }

    /// <summary>Render one frame.</summary>
    public void RenderFrame()
    {
        if (!_isInitialized || _device == null || _context == null)
            return;

        // Frame timing
        long currentTime = _timer.ElapsedTicks;
        float deltaTime = (float)(currentTime - _lastFrameTime) / Stopwatch.Frequency;
        _lastFrameTime = currentTime;
        float time = (float)_timer.Elapsed.TotalSeconds;

        // FPS counting
        _frameCount++;
        if (currentTime - _fpsTimer >= Stopwatch.Frequency)
        {
            _currentFps = _frameCount * Stopwatch.Frequency / (float)(currentTime - _fpsTimer);
            _frameCount = 0;
            _fpsTimer = currentTime;
        }

        // FPS limiting
        if (_settings.FpsLimit > 0)
        {
            float targetFrameTime = 1.0f / _settings.FpsLimit;
            if (deltaTime < targetFrameTime)
            {
                int sleepMs = (int)((targetFrameTime - deltaTime) * 1000);
                if (sleepMs > 0) Thread.Sleep(sleepMs);
            }
        }

        // Set render state
        _context.RSSetState(_rasterizerState);
        _context.OMSetDepthStencilState(_noDepthState);
        _context.PSSetSampler(0, _linearSampler);

        // Set render target and viewport
        _context.OMSetRenderTargets(_renderTargetView);
        _context.RSSetViewport(0, 0, _width, _height);
        _context.ClearRenderTargetView(_renderTargetView!, new Color4(0.04f, 0.06f, 0.15f, 1f));

        // === RENDER PASS 1: Background ===
        _context.OMSetBlendState(_alphaBlendState);
        _backgroundRenderer?.Render(_context, _quadVertexBuffer!, time, deltaTime);

        // === RENDER PASS 2: Touchpad surface ===
        _context.OMSetBlendState(_alphaBlendState);
        _touchpadRenderer?.Render(_context, _quadVertexBuffer!, time, deltaTime);

        // === RENDER PASS 3: Trails ===
        _context.OMSetBlendState(_additiveBlendState);
        _trailSystem?.Render(_context, time, deltaTime);

        // === RENDER PASS 4: Particles ===
        _context.OMSetBlendState(_additiveBlendState);
        _particleSystem?.Render(_context, time, deltaTime);

        // === RENDER PASS 5: Ripples ===
        _context.OMSetBlendState(_additiveBlendState);
        _rippleSystem?.Render(_context, _quadVertexBuffer!, time, deltaTime);

        // === Copy to shared texture for WPF ===
        _context.CopyResource(_sharedTexture!, _renderTarget!);
        _context.Flush();

        lock (_renderLock)
        {
            _isFrameReady = true;
        }
    }

    /// <summary>Get the shared texture handle for WPF D3DImage interop.</summary>
    public IntPtr GetSharedHandle() => _d3d9SurfacePointer;

    /// <summary>
    /// Check if a new frame is ready and consume the flag.
    /// Called from the UI thread before updating D3DImage.
    /// </summary>
    public bool ConsumeFrame()
    {
        lock (_renderLock)
        {
            if (_isFrameReady)
            {
                _isFrameReady = false;
                return true;
            }
            return false;
        }
    }

    /// <summary>Get the D3D11 device.</summary>
    public ID3D11Device? Device => _device;

    /// <summary>
    /// Compile an HLSL shader from file.
    /// Returns the compiled bytecode as a byte array.
    /// </summary>
    public static byte[] CompileShader(string filePath, string entryPoint, string profile)
    {
        var flags = ShaderFlags.OptimizationLevel3;
#if DEBUG
        flags = ShaderFlags.Debug | ShaderFlags.SkipOptimization;
#endif

        var sourceCode = File.ReadAllText(filePath);
        var result = Compiler.Compile(sourceCode, entryPoint, filePath, profile, flags);

        // result is ReadOnlyMemory<byte> containing the compiled shader bytecode
        return result.ToArray();
    }

    /// <summary>Create a constant buffer of the given size.</summary>
    public static ID3D11Buffer CreateConstantBuffer(ID3D11Device device, int size)
    {
        // Align to 16 bytes
        uint alignedSize = (uint)((size + 15) & ~15);
        return device.CreateBuffer(new BufferDescription
        {
            ByteWidth = alignedSize,
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        });
    }

    /// <summary>Update a constant buffer with new data.</summary>
    public static unsafe void UpdateConstantBuffer<T>(ID3D11DeviceContext context, ID3D11Buffer buffer, ref T data)
        where T : unmanaged
    {
        var mapped = context.Map(buffer, MapMode.WriteDiscard);
        try
        {
            fixed (T* ptr = &data)
            {
                Buffer.MemoryCopy(ptr, (void*)mapped.DataPointer, mapped.RowPitch, sizeof(T));
            }
        }
        finally
        {
            context.Unmap(buffer, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _backgroundRenderer?.Dispose();
        _touchpadRenderer?.Dispose();
        _particleSystem?.Dispose();
        _rippleSystem?.Dispose();
        _trailSystem?.Dispose();

        _quadVertexBuffer?.Dispose();
        _additiveBlendState?.Dispose();
        _alphaBlendState?.Dispose();
        _linearSampler?.Dispose();
        _rasterizerState?.Dispose();
        _noDepthState?.Dispose();

        _renderTargetView?.Dispose();
        _renderTarget?.Dispose();
        _sharedTexture?.Dispose();

        _d3d9Surface?.Dispose();
        _d3d9Texture?.Dispose();
        _d3d9Device?.Dispose();
        _d3d9?.Dispose();

        _context?.Dispose();
        _device?.Dispose();
    }
}
