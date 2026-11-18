using System;
using System.IO;

/// <summary>Explicit offline fixture permission, shared by release and development players.</summary>
public static class RedboolCameraCheckMode
{
    public const string CheckFlag="--redbool-camera-check";
    public const string SyntheticFlag="--redbool-camera-synthetic";
    public const string OutputVariable="REDBOOL_CAMERA_CHECK_OUTPUT";
    public static bool Requested(string[] args) => args!=null &&
        (Array.IndexOf(args,CheckFlag)>=0 || Array.IndexOf(args,SyntheticFlag)>=0);

    // A partial opt-in is NEVER permission to fall back to a real camera.
    // All users' ordinary launches remain unchanged. No hardware, sockets or
    // directories are opened by this pure configuration check.
    public static bool Authorize(string[] args,string output,bool editor,bool batch,out string error)
    {
        error=null;
        if(!Requested(args))return false;
        if(editor||batch)error="Offline camera fixture requires an interactive standalone player.";
        else if(Array.IndexOf(args,CheckFlag)<0||Array.IndexOf(args,SyntheticFlag)<0)
            error="Offline camera fixture requires both explicit check and synthetic flags.";
        else if(Array.IndexOf(args,"--redbool-external-camera")>=0
            ||Array.IndexOf(args,"--redbool-online-host")>=0||Array.IndexOf(args,"--redbool-online-join")>=0)
            error="Offline camera fixture cannot be combined with external or online automation.";
        else if(String.IsNullOrWhiteSpace(output)||!Path.IsPathFullyQualified(output))
            error="Offline camera fixture requires an absolute dedicated output directory.";
        else
        {
            try
            {
                string full=Path.GetFullPath(output);
                if(String.Equals(full.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),
                    Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.Ordinal))
                    error="Offline camera fixture output must not be a filesystem root.";
            }
            catch(ArgumentException) {error="Offline camera fixture output is invalid.";}
            catch(NotSupportedException) {error="Offline camera fixture output is invalid.";}
            catch(PathTooLongException) {error="Offline camera fixture output is invalid.";}
        }
        return error==null;
    }
}
