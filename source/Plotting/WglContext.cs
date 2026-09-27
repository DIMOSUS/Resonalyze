using System.Runtime.InteropServices;

namespace Resonalyze;

/// <summary>A hardware OpenGL context on one window's own DC; the window class must carry CS_OWNDC.</summary>
internal sealed class WglContext : IDisposable
{
    public const int OwnDeviceContextClassStyle = 0x20;

    private const uint DrawToWindow = 0x04;
    private const uint SupportOpenGl = 0x20;
    private const uint DoubleBuffer = 0x01;
    private const uint GenericFormat = 0x40;
    private const uint GenericAccelerated = 0x1000;

    private readonly IntPtr window;
    private IntPtr deviceContext;
    private IntPtr renderingContext;

    private WglContext(IntPtr window, IntPtr deviceContext, IntPtr renderingContext)
    {
        this.window = window;
        this.deviceContext = deviceContext;
        this.renderingContext = renderingContext;
    }

    /// <summary>Null when the driver offers only Windows' software OpenGL 1.1 (Remote Desktop, a VM without a GPU).</summary>
    public static WglContext? TryCreate(IntPtr window)
    {
        IntPtr deviceContext = GetDC(window);
        if (deviceContext == IntPtr.Zero)
        {
            return null;
        }

        var wanted = new PixelFormatDescriptor
        {
            Size = (ushort)Marshal.SizeOf<PixelFormatDescriptor>(),
            Version = 1,
            Flags = DrawToWindow | SupportOpenGl | DoubleBuffer,
            ColorBits = 32,
            AlphaBits = 8,
            StencilBits = 8
        };
        int format = ChoosePixelFormat(deviceContext, ref wanted);
        var chosen = new PixelFormatDescriptor();
        if (format == 0 ||
            DescribePixelFormat(deviceContext, format, (uint)Marshal.SizeOf<PixelFormatDescriptor>(), ref chosen) == 0 ||
            IsSoftware(chosen.Flags) ||
            !SetPixelFormat(deviceContext, format, ref chosen))
        {
            ReleaseDC(window, deviceContext);
            return null;
        }

        IntPtr renderingContext = wglCreateContext(deviceContext);
        if (renderingContext == IntPtr.Zero)
        {
            ReleaseDC(window, deviceContext);
            return null;
        }

        var context = new WglContext(window, deviceContext, renderingContext);
        if (!context.MakeCurrent())
        {
            context.Dispose();
            return null;
        }

        // Presenting must not wait for the vertical blank on the UI thread; DWM composes the window anyway.
        IntPtr swapInterval = wglGetProcAddress("wglSwapIntervalEXT");
        if (!IsFailedProcAddress(swapInterval))
        {
            Marshal.GetDelegateForFunctionPointer<SwapIntervalExt>(swapInterval)(0);
        }

        return context;
    }

    public bool MakeCurrent() => wglMakeCurrent(deviceContext, renderingContext);

    public void SwapBuffers() => SwapBuffersNative(deviceContext);

    public void Dispose()
    {
        if (renderingContext != IntPtr.Zero)
        {
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(renderingContext);
            renderingContext = IntPtr.Zero;
        }

        if (deviceContext != IntPtr.Zero)
        {
            ReleaseDC(window, deviceContext);
            deviceContext = IntPtr.Zero;
        }
    }

    // Some drivers report a missing function as 1, 2, 3 or -1 rather than null.
    private static bool IsFailedProcAddress(IntPtr address) => address is 0 or 1 or 2 or 3 or -1;

    private static bool IsSoftware(uint flags) =>
        (flags & GenericFormat) != 0 && (flags & GenericAccelerated) == 0;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool SwapIntervalExt(int interval);

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        public ushort Size;
        public ushort Version;
        public uint Flags;
        public byte PixelType;
        public byte ColorBits;
        public byte RedBits;
        public byte RedShift;
        public byte GreenBits;
        public byte GreenShift;
        public byte BlueBits;
        public byte BlueShift;
        public byte AlphaBits;
        public byte AlphaShift;
        public byte AccumBits;
        public byte AccumRedBits;
        public byte AccumGreenBits;
        public byte AccumBlueBits;
        public byte AccumAlphaBits;
        public byte DepthBits;
        public byte StencilBits;
        public byte AuxBuffers;
        public byte LayerType;
        public byte Reserved;
        public uint LayerMask;
        public uint VisibleMask;
        public uint DamageMask;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern int ChoosePixelFormat(IntPtr deviceContext, ref PixelFormatDescriptor descriptor);

    [DllImport("gdi32.dll")]
    private static extern int DescribePixelFormat(IntPtr deviceContext, int format, uint size, ref PixelFormatDescriptor descriptor);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPixelFormat(IntPtr deviceContext, int format, ref PixelFormatDescriptor descriptor);

    [DllImport("gdi32.dll", EntryPoint = "SwapBuffers")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SwapBuffersNative(IntPtr deviceContext);

    [DllImport("opengl32.dll")]
    private static extern IntPtr wglCreateContext(IntPtr deviceContext);

    [DllImport("opengl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool wglMakeCurrent(IntPtr deviceContext, IntPtr renderingContext);

    [DllImport("opengl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool wglDeleteContext(IntPtr renderingContext);

    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr wglGetProcAddress(string name);
}
