using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Redbool.DesktopPresentation
{
    /// <summary>Changes only captions of this process's visible, unowned Unity windows.</summary>
    public static class WindowsCaption
    {
        public const string DisplayTitle = "흑운협";
        const uint Owner = 4;

        public struct Result
        {
            public int Candidates, Updated, Verified, Failed;
            public bool EnumerationSucceeded;
            public bool Success { get { return EnumerationSucceeded && Verified > 0 && Failed == 0; } }
        }

        public static Result ApplyToCurrentProcess()
        {
            uint ownProcess = GetCurrentProcessId();
            Result result = new Result();
            StringBuilder windowClass = new StringBuilder(64);
            StringBuilder caption = new StringBuilder(DisplayTitle.Length + 1);
            EnumWindow callback = delegate(IntPtr window, IntPtr unused)
            {
                // Never let a managed exception escape a reverse P/Invoke callback.
                try
                {
                    uint process;
                    if (GetWindowThreadProcessId(window, out process) == 0 || process != ownProcess
                        || !IsWindowVisible(window) || GetWindow(window, Owner) != IntPtr.Zero)
                        return true;
                    windowClass.Length = 0;
                    if (GetClassNameW(window, windowClass, windowClass.Capacity) == 0
                        || windowClass.ToString() != "UnityWndClass") return true;
                    result.Candidates++;
                    // Revalidate ownership immediately before changing a native handle.
                    if (GetWindowThreadProcessId(window, out process) == 0 || process != ownProcess)
                    { result.Failed++; return true; }
                    if (!HasTitle(window, caption))
                    {
                        if (!SetWindowTextW(window, DisplayTitle))
                        { result.Failed++; return true; }
                        result.Updated++;
                    }
                    if (HasTitle(window, caption)) result.Verified++;
                    else result.Failed++;
                }
                catch (Exception) { result.Failed++; }
                return true;
            };
            result.EnumerationSucceeded = EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return result;
        }

        static bool HasTitle(IntPtr window, StringBuilder caption)
        {
            if (GetWindowTextLengthW(window) != DisplayTitle.Length) return false;
            caption.Length = 0;
            return GetWindowTextW(window, caption, caption.Capacity) == DisplayTitle.Length
                && caption.ToString() == DisplayTitle;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        delegate bool EnumWindow(IntPtr window, IntPtr parameter);
        [DllImport("kernel32.dll", ExactSpelling = true)] static extern uint GetCurrentProcessId();
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)] static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
        [DllImport("user32.dll", ExactSpelling = true)] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", ExactSpelling = true)] static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr window, StringBuilder name, int capacity);
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowTextW(IntPtr window, string caption);
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] static extern int GetWindowTextLengthW(IntPtr window);
        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr window, StringBuilder caption, int capacity);
    }
}
