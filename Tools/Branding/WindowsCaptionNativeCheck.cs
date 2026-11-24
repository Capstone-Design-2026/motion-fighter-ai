using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Redbool.DesktopPresentation;

// Standalone native Win32 fixture: no Unity, GPU, camera, game or user saves.
static class WindowsCaptionNativeCheck
{
    static readonly List<string> Checks = new List<string>();
    static readonly WindowProcedure Procedure = DefWindowProcW;
    static readonly List<IntPtr> Windows = new List<IntPtr>();
    const uint Visible = 0x10000000, Overlapped = 0x00cf0000;
    static IntPtr Module { get { return GetModuleHandleW(null); } }

    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--foreign") return Foreign(args[1]);
        string output = args.Length == 1 ? args[0] : "windows-caption-check.json";
        string ready = Path.GetFullPath(output) + "." + Guid.NewGuid().ToString("N") + ".foreign";
        Process foreign = null;
        bool passed = false;
        string error = null;
        try
        {
            var empty = WindowsCaption.ApplyToCurrentProcess();
            Check(empty.EnumerationSucceeded && !empty.Success && empty.Candidates == 0, "No matching window returns false");
            Register("UnityWndClass"); Register("RedboolOtherClass");
            IntPtr other = Create("RedboolOtherClass", "other-original", IntPtr.Zero, true);
            IntPtr hidden = Create("UnityWndClass", "hidden-original", IntPtr.Zero, false);
            IntPtr owned = Create("UnityWndClass", "owned-original", other, true);
            var excluded = WindowsCaption.ApplyToCurrentProcess();
            Check(!excluded.Success && excluded.Candidates == 0, "Other class, hidden and owned windows excluded");
            Check(Title(other) == "other-original" && Title(hidden) == "hidden-original" && Title(owned) == "owned-original", "Excluded titles untouched");

            IntPtr first = Create("UnityWndClass", "Vision Striker", IntPtr.Zero, true);
            IntPtr second = Create("UnityWndClass", "second-original", IntPtr.Zero, true);
            var changed = WindowsCaption.ApplyToCurrentProcess();
            Check(changed.Success && changed.Candidates == 2 && changed.Updated == 2 && changed.Verified == 2, "Both eligible own-process windows updated and verified");
            Check(Title(first) == "흑운협" && Title(second) == "흑운협", "Unicode caption round trip");
            var repeated = WindowsCaption.ApplyToCurrentProcess();
            Check(repeated.Success && repeated.Updated == 0 && repeated.Verified == 2, "Repeated application is idempotent");
            Check(SetWindowTextW(first, "흑운협 suffix"), "Set prefix-collision fixture");
            Check(WindowsCaption.ApplyToCurrentProcess().Updated == 1 && Title(first) == "흑운협", "Longer caption sharing title prefix is corrected");

            foreign = Process.Start(new ProcessStartInfo {
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                Arguments = "--foreign \"" + ready + "\"", UseShellExecute = false, CreateNoWindow = true
            });
            Stopwatch deadline = Stopwatch.StartNew();
            while (!File.Exists(ready) && !foreign.HasExited && deadline.Elapsed.TotalSeconds < 10) Thread.Sleep(20);
            Check(File.Exists(ready) && !foreign.HasExited, "Foreign process created its same-class visible window");
            IntPtr foreignWindow = new IntPtr(long.Parse(File.ReadAllText(ready)));
            uint foreignPid;
            Check(GetWindowThreadProcessId(foreignWindow, out foreignPid) != 0 && foreignPid == (uint)foreign.Id, "Foreign handle belongs to child PID");
            Check(Title(foreignWindow) == "foreign-original", "Foreign title before core call");
            var withForeign = WindowsCaption.ApplyToCurrentProcess();
            Check(withForeign.Success && withForeign.Candidates == 2 && Title(foreignWindow) == "foreign-original", "Same-class foreign process title is untouched");
            Check(Title(other) == "other-original" && Title(hidden) == "hidden-original" && Title(owned) == "owned-original", "All excluded own windows remain untouched");

            Check(DestroyWindow(first), "Destroy eligible window"); Windows.Remove(first);
            IntPtr recreated = Create("UnityWndClass", "recreated-original", IntPtr.Zero, true);
            Check(WindowsCaption.ApplyToCurrentProcess().Success && Title(recreated) == "흑운협", "Recreated window can be captioned by a later bounded retry");
            foreach (IntPtr window in Windows) DestroyWindow(window);
            Windows.Clear();
            var onlyForeign = WindowsCaption.ApplyToCurrentProcess();
            Check(!onlyForeign.Success && onlyForeign.Candidates == 0 && Title(foreignWindow) == "foreign-original", "Only foreign window never produces success or mutation");
            passed = true;
        }
        catch (Exception exception) { error = exception.ToString(); }
        finally
        {
            foreach (IntPtr window in Windows) DestroyWindow(window);
            if (foreign != null)
            {
                File.WriteAllText(ready + ".stop", "stop");
                if (!foreign.WaitForExit(3000)) { foreign.Kill(); foreign.WaitForExit(); }
                foreign.Dispose();
            }
            if (File.Exists(ready)) File.Delete(ready);
            if (File.Exists(ready + ".stop")) File.Delete(ready + ".stop");
            File.WriteAllText(output, "{\"passed\":" + (passed ? "true" : "false")
                + ",\"scope\":\"Real Windows native APIs; synthetic windows only, not Unity player lifecycle\",\"checks\":["
                + string.Join(",", Checks.ConvertAll(Json).ToArray()) + "],\"error\":" + Json(error) + "}", new UTF8Encoding(false));
        }
        Console.WriteLine((passed ? "PASS " : "FAIL ") + Checks.Count + " native Windows caption checks");
        if (error != null) Console.Error.WriteLine(error);
        return passed ? 0 : 1;
    }

    static int Foreign(string ready)
    {
        Register("UnityWndClass");
        IntPtr window = Create("UnityWndClass", "foreign-original", IntPtr.Zero, true);
        File.WriteAllText(ready + ".tmp", window.ToInt64().ToString());
        File.Move(ready + ".tmp", ready);
        Stopwatch lifetime = Stopwatch.StartNew();
        while (lifetime.Elapsed.TotalSeconds < 30 && !File.Exists(ready + ".stop"))
        {
            Message message;
            while (PeekMessageW(out message, IntPtr.Zero, 0, 0, 1))
            { TranslateMessage(ref message); DispatchMessageW(ref message); }
            Thread.Sleep(10);
        }
        DestroyWindow(window);
        return 0;
    }
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name + " (Win32 " + Marshal.GetLastWin32Error() + ")"); Checks.Add(name); }
    static string Json(string value)
    { return value == null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\""; }
    static void Register(string name)
    {
        WindowClass type = new WindowClass(); type.Size = (uint)Marshal.SizeOf(typeof(WindowClass));
        type.Procedure = Procedure; type.Instance = Module; type.ClassName = name;
        if (RegisterClassExW(ref type) == 0) throw new Exception("RegisterClassExW failed: " + Marshal.GetLastWin32Error());
    }
    static IntPtr Create(string type, string title, IntPtr owner, bool visible)
    {
        IntPtr window = CreateWindowExW(0, type, title, Overlapped | (visible ? Visible : 0), 10, 10, 160, 80, owner, IntPtr.Zero, Module, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new Exception("CreateWindowExW failed: " + Marshal.GetLastWin32Error());
        Windows.Add(window); return window;
    }
    static string Title(IntPtr window)
    { StringBuilder text = new StringBuilder(256); GetWindowTextW(window, text, text.Capacity); return text.ToString(); }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct WindowClass
    {
        public uint Size, Style; public WindowProcedure Procedure; public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background; public string MenuName, ClassName; public IntPtr SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] struct Message
    { public IntPtr Window; public uint Id; public UIntPtr Wparam; public IntPtr Lparam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern IntPtr GetModuleHandleW(string module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] static extern ushort RegisterClassExW(ref WindowClass type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] static extern IntPtr CreateWindowExW(uint extended, string type, string title, uint style, int x, int y, int width, int height, IntPtr owner, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", ExactSpelling = true)] static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern int GetWindowTextW(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowTextW(IntPtr window, string text);
    [DllImport("user32.dll", ExactSpelling = true)] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool PeekMessageW(out Message message, IntPtr window, uint first, uint last, uint remove);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", ExactSpelling = true)] static extern IntPtr DispatchMessageW(ref Message message);
}
