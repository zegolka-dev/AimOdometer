using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace AimOdometer.Tools.GameInputProbe;

/// <summary>
/// Phase 10 experiment: does Microsoft GameInput give a background process mouse input without the coalescing that
/// Windows 11 applies to background Raw Input? Reads the mouse through both for N seconds (this console is never the
/// foreground window) and prints events and path length for each. A merged event keeps the sums of dx and dy but
/// shortens the path, so the path ratio shows coalescing differences.
/// Needs the GameInput redistributable (installed with the Xbox app / games) and real mouse movement: injected input
/// never reaches GameInput. Usage: GameInputProbe [seconds]
/// </summary>
internal static unsafe partial class Program
{
    private static readonly string[] Runtimes =
    [
        @"C:\Program Files\Microsoft GameInput\x64\GameInputRedist.dll",
        @"C:\Windows\System32\GameInputRedist.dll",
        @"C:\Windows\System32\GameInput.dll",
    ];

    // IGameInput (v3, GameInput 3.x headers): vtable slots after IUnknown's three.
    private const int GetCurrentReadingSlot = 4;
    private const int GetNextReadingSlot = 5;
    private const int SetFocusPolicySlot = 16;

    // IGameInputReading: GetDevice and GetMouseState.
    private const int GetDeviceSlot = 5;
    private const int GetMouseStateSlot = 14;

    private const uint GameInputKindMouse = 0x20;
    private const uint GameInputEnableBackgroundInput = 0x40;
    private const uint ReferenceReadingTooOld = 0x838A0004;

    private static int Main(string[] args)
    {
        var seconds = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 15;

        nint module = 0;
        var runtime = Runtimes.FirstOrDefault(path => File.Exists(path) && NativeLibrary.TryLoad(path, out module));
        if (runtime is null)
        {
            Console.Error.WriteLine("GameInput is not installed.");
            return 1;
        }

        Console.WriteLine($"GameInput runtime: {runtime}");
        var initialize = (delegate* unmanaged<Guid*, void**, int>)NativeLibrary.GetExport(module, "GameInputInitialize");
        var iid = new Guid("20EFC1C7-5D9A-43BA-B26F-B807FA48609C");
        void* gameInput = null;
        var hr = initialize(&iid, &gameInput);
        Console.WriteLine($"GameInputInitialize: 0x{hr:X8}");
        if (hr < 0)
        {
            return 1;
        }

        var table = *(void***)gameInput;
        ((delegate* unmanaged<void*, uint, void>)table[SetFocusPolicySlot])(gameInput, GameInputEnableBackgroundInput);
        var getCurrent = (delegate* unmanaged<void*, uint, void*, void**, int>)table[GetCurrentReadingSlot];
        var getNext = (delegate* unmanaged<void*, void*, uint, void*, void**, int>)table[GetNextReadingSlot];

        using var raw = new RawInputCounter();
        var game = new PathCounter();
        var lastPosition = new Dictionary<nint, (long X, long Y)>();

        void* last = null;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            if (last == null)
            {
                getCurrent(gameInput, GameInputKindMouse, null, &last);
                if (last != null)
                {
                    Track(last, lastPosition, game);
                }
            }

            while (last != null)
            {
                void* next = null;
                var result = getNext(gameInput, last, GameInputKindMouse, null, &next);
                if (result < 0 || next == null)
                {
                    if ((uint)result == ReferenceReadingTooOld)
                    {
                        Release(last);
                        last = null;
                    }

                    break;
                }

                Track(next, lastPosition, game);
                Release(last);
                last = next;
            }

            Thread.Sleep(1);
        }

        Console.WriteLine($"GameInput : {game}");
        Console.WriteLine($"Raw Input : {raw.Counter}");
        if (raw.Counter.Path > 0)
        {
            Console.WriteLine($"GameInput path / Raw Input path = {game.Path / raw.Counter.Path:0.0000}");
        }

        return 0;
    }

    /// <summary>GameInput reports positions accumulated per mouse; the difference to the previous reading is the move.</summary>
    private static void Track(void* reading, Dictionary<nint, (long X, long Y)> lastPosition, PathCounter counter)
    {
        var table = *(void***)reading;
        MouseState state;
        if (((delegate* unmanaged<void*, MouseState*, byte>)table[GetMouseStateSlot])(reading, &state) == 0)
        {
            return;
        }

        void* device = null;
        ((delegate* unmanaged<void*, void**, void>)table[GetDeviceSlot])(reading, &device);
        var key = (nint)device;
        if (device != null)
        {
            Release(device);
        }

        if (lastPosition.TryGetValue(key, out var previous))
        {
            counter.Add(state.PositionX - previous.X, state.PositionY - previous.Y);
        }

        lastPosition[key] = (state.PositionX, state.PositionY);
    }

    private static void Release(void* unknown) => ((delegate* unmanaged<void*, uint>)(*(void***)unknown)[2])(unknown);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseState
    {
        public uint Buttons;
        public uint Positions;
        public long PositionX;
        public long PositionY;
        public long AbsolutePositionX;
        public long AbsolutePositionY;
        public long WheelX;
        public long WheelY;
    }
}

/// <summary>Events, path length (sum of per-event vector lengths) and sums of |dx| and |dy|.</summary>
internal sealed class PathCounter
{
    public long Events { get; private set; }

    public double Path { get; private set; }

    public long SumX { get; private set; }

    public long SumY { get; private set; }

    public void Add(long dx, long dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        Events++;
        SumX += Math.Abs(dx);
        SumY += Math.Abs(dy);
        Path += Math.Sqrt(((double)dx * dx) + ((double)dy * dy));
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Events,8} events, path {Path,12:0} counts, |dx| {SumX,10}, |dy| {SumY,10}");
}

/// <summary>Background Raw Input on a message-only window (RIDEV_INPUTSINK), the way the tracker reads the mouse.</summary>
internal sealed unsafe partial class RawInputCounter : IDisposable
{
    private static RawInputCounter? _current;
    private readonly Thread _thread;
    private nint _window;

    public RawInputCounter()
    {
        _current = this;
        _thread = new Thread(Run) { IsBackground = true };
        _thread.Start();
        Thread.Sleep(300);
    }

    public PathCounter Counter { get; } = new();

    public void Dispose()
    {
        PostMessageW(_window, 0x0012, 0, 0); // WM_QUIT
        _thread.Join(1000);
    }

    private void Run()
    {
        var className = Marshal.StringToHGlobalUni("AimOdometer.GameInputProbe");
        var windowClass = new WindowClass
        {
            Size = (uint)sizeof(WindowClass),
            Procedure = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WindowProcedure,
            ClassName = className,
        };
        RegisterClassExW(&windowClass);
        _window = CreateWindowExW(0, className, 0, 0, 0, 0, 0, 0, -3, 0, 0, 0); // HWND_MESSAGE
        var device = new RawInputDevice { UsagePage = 1, Usage = 2, Flags = 0x100, Target = _window }; // mouse, RIDEV_INPUTSINK
        if (RegisterRawInputDevices(&device, 1, (uint)sizeof(RawInputDevice)) == 0)
        {
            Console.Error.WriteLine("Raw Input registration failed.");
            return;
        }

        Message message;
        while (GetMessageW(&message, 0, 0, 0) > 0)
        {
            DispatchMessageW(&message);
        }

        Marshal.FreeHGlobal(className);
    }

    [UnmanagedCallersOnly]
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == 0x00FF && _current is { } self) // WM_INPUT
        {
            var buffer = stackalloc byte[64];
            uint size = 64;
            if (GetRawInputData(lParam, 0x10000003, buffer, &size, 24) != uint.MaxValue && (*(ushort*)(buffer + 24) & 1) == 0)
            {
                // RAWINPUTHEADER (24 bytes), then RAWMOUSE: flags, buttons, raw buttons, lLastX at +12, lLastY at +16.
                self.Counter.Add(*(int*)(buffer + 36), *(int*)(buffer + 40));
            }
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WindowClass* windowClass);

    [LibraryImport("user32.dll")]
    private static partial nint CreateWindowExW(uint exStyle, nint className, nint name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    private static partial int RegisterRawInputDevices(RawInputDevice* devices, uint count, uint size);

    [LibraryImport("user32.dll")]
    private static partial uint GetRawInputData(nint rawInput, uint command, byte* data, uint* size, uint headerSize);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(Message* message, nint window, uint min, uint max);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(Message* message);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
