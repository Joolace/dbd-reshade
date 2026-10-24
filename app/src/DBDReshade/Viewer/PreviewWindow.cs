using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace DBDReshade.Viewer;

/// <summary>
/// Direct3D 11 window that shows map screenshots, keeping their aspect ratio. Because it renders
/// through DXGI, the ReShade installed next to the preview exe applies the preset to it, exactly
/// as it would in the game. Ported from D3Daylight; the D3D calls are unchanged.
///
/// Keys: ← / → previous / next image, ESC close, F11 full screen.
/// </summary>
public sealed class PreviewWindow : WinForms.Form
{
    private readonly List<string> _images;
    private readonly string _title;
    private readonly string _hint;
    private int _index;

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _rtv;
    private ID3D11Texture2D? _texture;
    private ID3D11ShaderResourceView? _textureView;
    private ID3D11SamplerState? _sampler;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _resolutionBuffer;
    private Drawing.Size _imageSize;
    private bool _ready;

    private WinForms.FormWindowState _windowedState;
    private WinForms.FormBorderStyle _windowedBorder;

    public PreviewWindow(List<string> images, string title, string hint)
    {
        _images = images;
        _title = title;
        _hint = hint;

        try { Icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { /* cosmetic */ }

        BackColor = Drawing.Color.FromArgb(26, 26, 26);
        StartPosition = WinForms.FormStartPosition.CenterScreen;
        var area = WinForms.Screen.PrimaryScreen?.WorkingArea ?? new Drawing.Rectangle(0, 0, 1600, 900);
        Size = new Drawing.Size(area.Width * 4 / 5, area.Height * 4 / 5);
        WindowState = WinForms.FormWindowState.Maximized;

        KeyPreview = true;
        KeyDown += OnKeyDown;
        Shown += OnShown;
        Resize += (_, _) => ResizeSwapChain();
        FormClosed += (_, _) => Cleanup();

        UpdateTitle();
    }

    private void UpdateTitle() =>
        Text = $"{_title}  ({_index + 1}/{_images.Count})  —  {_hint}";

    private void OnKeyDown(object? sender, WinForms.KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case WinForms.Keys.Right:
                ShowImage((_index + 1) % _images.Count);
                break;
            case WinForms.Keys.Left:
                ShowImage((_index - 1 + _images.Count) % _images.Count);
                break;
            case WinForms.Keys.Escape:
                if (FormBorderStyle == WinForms.FormBorderStyle.None) ToggleFullScreen();
                else Close();
                break;
            case WinForms.Keys.F11:
                ToggleFullScreen();
                break;
        }
    }

    private void ToggleFullScreen()
    {
        if (FormBorderStyle == WinForms.FormBorderStyle.None)
        {
            FormBorderStyle = _windowedBorder;
            WindowState = _windowedState;
        }
        else
        {
            _windowedBorder = FormBorderStyle;
            _windowedState = WindowState;
            WindowState = WinForms.FormWindowState.Normal; // required for the borderless maximize to cover the taskbar
            FormBorderStyle = WinForms.FormBorderStyle.None;
            WindowState = WinForms.FormWindowState.Maximized;
        }
    }

    // ---- Setup ---------------------------------------------------------------------------------

    private void OnShown(object? sender, EventArgs e)
    {
        try
        {
            var swapDesc = new SwapChainDescription1
            {
                BufferCount = 2,
                Width = (uint)Math.Max(1, ClientSize.Width),
                Height = (uint)Math.Max(1, ClientSize.Height),
                Format = Format.R8G8B8A8_UNorm,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipDiscard,
            };

            using var factory = CreateDXGIFactory2<IDXGIFactory2>(false);
            D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_0 }, out _device, out _context);

            _swapChain = factory.CreateSwapChainForHwnd(_device!, Handle, swapDesc);
            CreateRenderTarget();

            SetupShaders();
            SetupQuad();
            LoadImageTexture();
            UpdateResolution();

            _ready = true;
            WinForms.Application.Idle += OnIdle;
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show($"Direct3D 11 could not be started.\n\n{ex.Message}", "DBD Reshade",
                WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            Close();
        }
    }

    private void CreateRenderTarget()
    {
        var backBuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
        _rtv = _device!.CreateRenderTargetView(backBuffer);
        backBuffer.Dispose();
    }

    /// <summary>Recreates the back buffer at the new window size (the original kept 1920×1080 and stretched).</summary>
    private void ResizeSwapChain()
    {
        if (!_ready || _swapChain is null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        _context!.ClearState();
        _rtv?.Dispose();
        _rtv = null;
        _swapChain.ResizeBuffers(2, (uint)ClientSize.Width, (uint)ClientSize.Height, Format.R8G8B8A8_UNorm, SwapChainFlags.None);
        CreateRenderTarget();
        UpdateResolution();
    }

    private void ShowImage(int index)
    {
        _index = index;
        UpdateTitle();
        if (!_ready) return;

        LoadImageTexture();
        UpdateResolution();
    }

    /// <summary>Loads the current PNG/JPG into a texture (same method as D3Daylight).</summary>
    private void LoadImageTexture()
    {
        using var bitmap = new Drawing.Bitmap(_images[_index]);
        bitmap.RotateFlip(Drawing.RotateFlipType.RotateNoneFlipY);

        var data = bitmap.LockBits(
            new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            Drawing.Imaging.ImageLockMode.ReadOnly,
            Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            var textureDesc = new Texture2DDescription
            {
                Width = (uint)bitmap.Width,
                Height = (uint)bitmap.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
            };

            var dataBox = new SubresourceData(data.Scan0, (uint)data.Stride, 0);

            _textureView?.Dispose();
            _texture?.Dispose();
            _texture = _device!.CreateTexture2D(textureDesc, new[] { dataBox });
            _textureView = _device.CreateShaderResourceView(_texture);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        _sampler ??= _device!.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
        });

        _imageSize = bitmap.Size;
    }

    /// <summary>Feeds image and window size to the vertex shader, which letterboxes the image.</summary>
    private void UpdateResolution()
    {
        if (_resolutionBuffer is null) return;
        var resolution = new Vector4(_imageSize.Width, _imageSize.Height,
            Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        _context!.UpdateSubresource(ref resolution, _resolutionBuffer);
    }

    private void SetupShaders()
    {
        const string vsCode = @"
cbuffer Resolution : register(b0)
{
    float2 imageSize;
    float2 screenSize;
};

struct VSInput {
    float3 position : POSITION;
    float2 texcoord : TEXCOORD;
};

struct PSInput {
    float4 position : SV_POSITION;
    float2 texcoord : TEXCOORD;
};

PSInput VSMain(VSInput input) {
    PSInput output;

    float aspectImage = imageSize.x / imageSize.y;
    float aspectScreen = screenSize.x / screenSize.y;

    float scaleX = 1.0;
    float scaleY = 1.0;

    if (aspectImage > aspectScreen)
        scaleY = aspectScreen / aspectImage;
    else
        scaleX = aspectImage / aspectScreen;

    output.position = float4(input.position.x * scaleX, input.position.y * scaleY, input.position.z, 1.0);
    output.texcoord = input.texcoord;
    return output;
}";

        const string psCode = @"
Texture2D tex : register(t0);
SamplerState samp : register(s0);

float4 PSMain(float4 position : SV_POSITION, float2 texcoord : TEXCOORD) : SV_TARGET {
    return tex.Sample(samp, texcoord);
}";

        // Compiled from temp files, exactly as D3Daylight did.
        var vsPath = Path.GetTempFileName();
        var psPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(vsPath, vsCode);
            File.WriteAllText(psPath, psCode);

            var vsBytecode = Compiler.CompileFromFile(vsPath, "VSMain", "vs_4_0");
            var psBytecode = Compiler.CompileFromFile(psPath, "PSMain", "ps_4_0");

            _vertexShader = _device!.CreateVertexShader(vsBytecode.Span);
            _pixelShader = _device.CreatePixelShader(psBytecode.Span);

            _inputLayout = _device.CreateInputLayout(
                new[]
                {
                    new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                    new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12, 0),
                },
                vsBytecode.Span);
        }
        finally
        {
            File.Delete(vsPath);
            File.Delete(psPath);
        }

        var bufferDesc = new BufferDescription(
            bindFlags: BindFlags.ConstantBuffer,
            byteWidth: (uint)(sizeof(float) * 4),
            usage: ResourceUsage.Default);

        unsafe
        {
            var buffer = stackalloc float[4] { 1, 1, 1, 1 };
            _resolutionBuffer = _device.CreateBuffer(bufferDesc, new SubresourceData((IntPtr)buffer));
        }
    }

    /// <summary>Full-window quad with flipped UVs (top-left origin).</summary>
    private void SetupQuad()
    {
        float[] vertices =
        {
            // POSITION       // TEXCOORD (flip Y)
            -1f,  1f, 0f,     0f, 1f, // top-left
             1f,  1f, 0f,     1f, 1f, // top-right
            -1f, -1f, 0f,     0f, 0f, // bottom-left
             1f, -1f, 0f,     1f, 0f, // bottom-right
        };

        var vertexBufferDesc = new BufferDescription(
            bindFlags: BindFlags.VertexBuffer,
            byteWidth: (uint)(sizeof(float) * vertices.Length),
            usage: ResourceUsage.Default,
            cpuAccessFlags: CpuAccessFlags.None,
            miscFlags: ResourceOptionFlags.None,
            structureByteStride: 0);

        unsafe
        {
            fixed (float* pVertices = vertices)
            {
                _vertexBuffer = _device!.CreateBuffer(vertexBufferDesc, new SubresourceData((IntPtr)pVertices));
            }
        }
    }

    // ---- Render loop -------------------------------------------------------------------------

    /// <summary>
    /// Renders continuously while no window message is waiting (D3Daylight drew once per Idle event,
    /// which left the ReShade overlay sluggish). Present(1) syncs to the monitor refresh rate.
    /// </summary>
    private void OnIdle(object? sender, EventArgs e)
    {
        while (_ready && !IsDisposed && !PeekMessage(out _, IntPtr.Zero, 0, 0, 0))
            Render();
    }

    private void Render()
    {
        if (_textureView is null || _sampler is null || _rtv is null) return;

        var context = _context!;
        context.RSSetViewport(0, 0, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        context.OMSetRenderTargets(_rtv);
        context.ClearRenderTargetView(_rtv, new Color4(0.1f, 0.1f, 0.1f, 1.0f));

        context.IASetInputLayout(_inputLayout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        context.IASetVertexBuffer(0, _vertexBuffer!, sizeof(float) * 5);

        context.VSSetShader(_vertexShader);
        context.VSSetConstantBuffer(0, _resolutionBuffer);
        context.PSSetShader(_pixelShader);
        context.PSSetShaderResource(0, _textureView);
        context.PSSetSampler(0, _sampler);

        context.Draw(4, 0);
        _swapChain!.Present(1, PresentFlags.None);
    }

    private void Cleanup()
    {
        _ready = false;
        WinForms.Application.Idle -= OnIdle;

        _textureView?.Dispose();
        _texture?.Dispose();
        _sampler?.Dispose();
        _resolutionBuffer?.Dispose();
        _vertexBuffer?.Dispose();
        _inputLayout?.Dispose();
        _pixelShader?.Dispose();
        _vertexShader?.Dispose();
        _rtv?.Dispose();
        _swapChain?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Handle;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Drawing.Point Location;
    }

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax, uint remove);
}
