using System;
using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;

/// <summary>
/// A camera child must not keep a scene's game socket alive after Unity closes it.
/// Older Windows Mono creates inheritable sockets; redirected Process.Start can
/// pass them to the child unless the native inheritance flag is cleared explicitly.
/// Call immediately after socket creation, before starting a worker or child process.
/// </summary>
public static class RedboolSocketOwnership
{
    const uint HandleFlagInherit = 1;

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetHandleInformation(IntPtr handle, out uint flags);

    public static void PreventInheritance(Socket socket, string endpoint)
    {
        if (socket == null) throw new ArgumentNullException("socket");
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
        try
        {
            IntPtr handle = socket.Handle;
            if (!SetHandleInformation(handle, HandleFlagInherit, 0))
                throw Failure(endpoint, "SetHandleInformation", Marshal.GetLastWin32Error());
            uint flags;
            if (!GetHandleInformation(handle, out flags))
                throw Failure(endpoint, "GetHandleInformation", Marshal.GetLastWin32Error());
            if ((flags & HandleFlagInherit) != 0)
                throw Failure(endpoint, "inheritance flag remained set after verification", 0);
        }
        catch
        {
            // Never leave an unsafe listener open after protection failed.
            socket.Dispose();
            throw;
        }
    }

    static Win32Exception Failure(string endpoint, string operation, int error)
    {
        return new Win32Exception(error, "Socket ownership for " + endpoint + ": " + operation
            + " failed (native error " + error + "). The socket was closed.");
    }
}
