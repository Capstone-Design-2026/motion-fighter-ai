// Native Windows fixture. Compile the production RedboolSocketOwnership.cs alongside it.
// No Unity, webcam, gameplay packets, port reuse or process-wide termination.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

public static class SocketOwnershipCheck
{
    [DllImport("kernel32.dll", SetLastError=true, ExactSpelling=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError=true, ExactSpelling=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetHandleInformation(IntPtr handle, out uint flags);
    [DllImport("ws2_32.dll", SetLastError=true, ExactSpelling=true)]
    static extern int closesocket(IntPtr handle);

    [DataContract] public sealed class Scenario
    {
        [DataMember] public string name;
        [DataMember] public int[] ports;
        [DataMember] public List<uint> default_flags=new List<uint>();
        [DataMember] public List<uint> spawn_flags=new List<uint>();
        [DataMember] public List<bool> rebind_with_child_alive=new List<bool>();
        [DataMember] public List<int> rebind_native_errors=new List<int>();
        [DataMember] public bool child_alive_after_parent_close, child_exited, all_ports_recovered, passed;
        [DataMember] public int child_pid;
        [DataMember] public int child_exit_code=-1;
        [DataMember] public string child_error;
    }
    [DataContract] public sealed class Evidence
    {
        [DataMember] public bool passed, camera_opened=false, game_packets_sent=false;
        [DataMember] public string runtime, os, utc, error, helper_sha256;
        [DataMember] public string scope="Native Windows socket inheritance mechanism; Framework/standalone Mono is not an actual Unity scene test.";
        [DataMember] public List<string> checks=new List<string>();
        [DataMember] public List<Scenario> scenarios=new List<Scenario>();
    }
    static readonly int[] Ports={5065,5066,5067};
    static void Require(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    static uint Flags(Socket socket)
    {
        uint value;if(!GetHandleInformation(socket.Handle,out value))throw new Win32Exception(Marshal.GetLastWin32Error());return value;
    }
    static UdpClient Bind(int port) {return new UdpClient(new IPEndPoint(IPAddress.Loopback,port));}
    static bool CanBind(int port,out int nativeError)
    {
        try {using(var receiver=Bind(port)) {nativeError=0;return true;}}
        catch(SocketException error)
        {
            nativeError=error.NativeErrorCode;
            if(error.SocketErrorCode!=SocketError.AddressAlreadyInUse)throw;
            return false;
        }
    }
    static string RuntimeName()
    {
        var mono=Type.GetType("Mono.Runtime");
        if(mono==null)return ".NET Framework CLR "+Environment.Version;
        var display=mono.GetMethod("GetDisplayName",BindingFlags.NonPublic|BindingFlags.Static);
        return "Mono "+(display==null?Environment.Version.ToString():(string)display.Invoke(null,null));
    }
    static int Child(string token)
    {
        Console.WriteLine("READY "+token);Console.Out.Flush();
        // Framework may prefix redirected input with its encoding preamble;
        // decode the pipe explicitly instead of the inherited console code page.
        using(var reader=new StreamReader(Console.OpenStandardInput(),Encoding.UTF8,true))
        {
            var input=reader.ReadLineAsync();
            if(!input.Wait(15000))return 3;
            if(input.Result=="STOP "+token)return 0;
            Console.Error.WriteLine("Unexpected stop marker; length="+(input.Result==null?-1:input.Result.Length));
            return 4;
        }
    }
    static void SendStop(Process child,string token)
    {
        byte[] bytes=Encoding.ASCII.GetBytes("STOP "+token+"\n");
        child.StandardInput.BaseStream.Write(bytes,0,bytes.Length);child.StandardInput.BaseStream.Flush();
    }
    static Process StartChild(string token)
    {
        var info=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--child "+token);
        info.UseShellExecute=false;info.CreateNoWindow=true;
        info.RedirectStandardInput=info.RedirectStandardOutput=info.RedirectStandardError=true;
        var child=new Process();child.StartInfo=info;
        try
        {
            Require(child.Start(),"Child did not start");
            var ready=child.StandardOutput.ReadLineAsync();
            Require(ready.Wait(8000)&&ready.Result=="READY "+token,"Child startup timed out or returned wrong marker");
            return child;
        }
        catch {StopChild(child,token);throw;}
    }
    static void StopChild(Process child,string token)
    {
        if(child==null)return;
        try
        {
            if(!child.HasExited)
            {
                try {SendStop(child,token);}
                catch(IOException) { }
                if(!child.WaitForExit(3000)) {child.Kill();if(!child.WaitForExit(3000))throw new TimeoutException("Owned child did not exit after bounded cleanup");}
            }
        }
        catch(InvalidOperationException) { }
        finally {child.Dispose();}
    }
    static void ScenarioRun(Evidence report,string mode)
    {
        var row=new Scenario {name=mode,ports=(int[])Ports.Clone()};report.scenarios.Add(row);
        var sockets=new List<UdpClient>();Process child=null;string token=Guid.NewGuid().ToString("N");
        try
        {
            foreach(int port in Ports)
            {
                var receiver=Bind(port);sockets.Add(receiver);var socket=receiver.Client;
                row.default_flags.Add(Flags(socket));
                int reuse=Convert.ToInt32(socket.GetSocketOption(SocketOptionLevel.Socket,SocketOptionName.ReuseAddress));
                bool exclusive=socket.ExclusiveAddressUse;
                if(mode!="default_observation")
                    Require(SetHandleInformation(socket.Handle,1,1),"Cannot establish inheritable positive control");
                if(mode=="protected")
                {
                    RedboolSocketOwnership.PreventInheritance(socket,"fixture UDP "+port);
                    Require((Flags(socket)&1)==0,"Helper did not clear HANDLE_FLAG_INHERIT");
                    Require(reuse==Convert.ToInt32(socket.GetSocketOption(SocketOptionLevel.Socket,SocketOptionName.ReuseAddress))&&exclusive==socket.ExclusiveAddressUse,"Helper changed exclusive/reuse semantics");
                }
                else if(mode=="inheritable_control")Require((Flags(socket)&1)!=0,"Positive control not inheritable");
                row.spawn_flags.Add(Flags(socket));
                int nativeError;Require(!CanBind(port,out nativeError),"Endpoint was not exclusive before spawning child");
            }
            child=StartChild(token);row.child_pid=child.Id;
            foreach(var socket in sockets)socket.Close();sockets.Clear();
            row.child_alive_after_parent_close=!child.HasExited;Require(row.child_alive_after_parent_close,"Child exited before rebind check");
            foreach(int port in Ports)
            {
                int nativeError;bool rebound=CanBind(port,out nativeError);
                row.rebind_with_child_alive.Add(rebound);row.rebind_native_errors.Add(nativeError);
                Require(!child.HasExited,"Child exited during rebind check");
                if(mode=="inheritable_control")Require(!rebound,"Forced-inheritance positive control did not retain endpoint; do not infer reproduction");
                if(mode=="protected")Require(rebound,"Protected socket leaked into child");
            }
            SendStop(child,token);
            row.child_exited=child.WaitForExit(3000);
            if(row.child_exited) {row.child_exit_code=child.ExitCode;row.child_error=child.StandardError.ReadToEnd();}
            Require(row.child_exited&&row.child_exit_code==0,"Child did not exit gracefully: exited="+row.child_exited+", code="+row.child_exit_code+", stderr="+row.child_error);
            foreach(int port in Ports) {int nativeError;Require(CanBind(port,out nativeError),"Port did not recover after child exit");}
            row.all_ports_recovered=true;row.passed=true;
        }
        finally {foreach(var socket in sockets)socket.Close();StopChild(child,token);}
    }
    static void InvalidHandle(Evidence report)
    {
        bool rejected=false;
        try {RedboolSocketOwnership.PreventInheritance(null,"null fixture");}
        catch(ArgumentNullException) {rejected=true;}
        Require(rejected,"Null socket was accepted");report.checks.Add("null socket rejected");
        using(var socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp))
        {
            Require(closesocket(socket.Handle)==0,"Cannot establish owned invalid native handle fixture");
            rejected=false;
            try {RedboolSocketOwnership.PreventInheritance(socket,"invalid-handle fixture");}
            catch(Win32Exception error) {rejected=error.NativeErrorCode!=0&&error.Message.Contains("invalid-handle fixture");}
            Require(rejected,"Native handle failure was not reported with endpoint/native error");
            bool disposed=false;try {socket.Bind(new IPEndPoint(IPAddress.Loopback,0));}
            catch(ObjectDisposedException) {disposed=true;}
            Require(disposed,"Protection failure did not dispose owned socket");
        }
        report.checks.Add("invalid native handle fails closed with endpoint and native error");
    }
    public static int Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--child")return Child(args[1]);
        string output=null,hash=null;
        for(int i=0;i<args.Length;i+=2)
        {
            if(i+1>=args.Length)return 2;
            if(args[i]=="--output")output=args[i+1];else if(args[i]=="--helper-sha256")hash=args[i+1];else return 2;
        }
        if(output==null)return 2;
        var report=new Evidence {runtime=RuntimeName(),os=Environment.OSVersion.ToString(),utc=DateTime.UtcNow.ToString("o"),helper_sha256=hash};
        try
        {
            Require(Environment.OSVersion.Platform==PlatformID.Win32NT,"Native Windows is required; no emulation result accepted");
            ScenarioRun(report,"default_observation");
            ScenarioRun(report,"inheritable_control");
            ScenarioRun(report,"protected");InvalidHandle(report);report.passed=true;
        }
        catch(Exception error) {report.error=error.ToString();Console.Error.WriteLine(error);}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        using(var file=File.Create(output))new DataContractJsonSerializer(typeof(Evidence)).WriteObject(file,report);
        Console.WriteLine((report.passed?"PASS":"FAIL")+" "+report.runtime+"; evidence="+output);
        return report.passed?0:1;
    }
}
