using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class RedboolMacCameraPrivacyCheck
{
    [Serializable] sealed class Result
    {
        public bool passed;
        public string error, unity, camera_usage_description, rejected_app, rejected_plist_sha256;
        public bool camera_opened=false, play_mode_entered=false, app_or_source_settings_modified=false;
        public string[] checks;
    }
    static string Sha(byte[] bytes)
    {
        using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
    }
    public static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "redbool-privacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Info.plist");
        var checks = new List<string>();
        var result = new Result { unity=Application.unityVersion, camera_usage_description=PlayerSettings.iOS.cameraUsageDescription };
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);checks.Add(label);};
        try
        {
            check(Application.isBatchMode && !Application.isPlaying,"Batch edit mode; no scene or camera startup");
            RedboolMacCameraPrivacy.ValidateSettings();
            check(!String.IsNullOrWhiteSpace(PlayerSettings.iOS.cameraUsageDescription),"Actual source camera usage setting accepted by native Unity API");
            bool missingRejected=false;
            try { RedboolMacCameraPrivacy.ValidatePlist(Path.Combine(folder,"missing.plist")); }
            catch(BuildFailedException) { missingRejected=true; }
            check(missingRejected,"Missing Info.plist rejected");
            foreach (string body in new[] {
                "", "<key>NSCameraUsageDescription</key><string></string>",
                "<key>NSCameraUsageDescription</key><string>  </string>",
                "<key>NSCameraUsageDescription</key><true/>",
                "<key>NSCameraUsageDescription</key><string>Camera</string><key>NSCameraUsageDescription</key><string>Other</string>" })
            {
                File.WriteAllText(path, "<plist><dict>" + body + "</dict></plist>");
                bool rejected = false;
                try { RedboolMacCameraPrivacy.ValidatePlist(path); }
                catch (BuildFailedException) { rejected = true; }
                check(rejected,"Invalid camera privacy fixture rejected: " + body);
            }
            File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\"><plist version=\"1.0\"><dict><key>NSCameraUsageDescription</key><string>흑운협은 동작 인식에 카메라를 사용합니다.</string></dict></plist>", new UTF8Encoding(false));
            var before = File.ReadAllBytes(path);
            RedboolMacCameraPrivacy.ValidatePlist(path);checks.Add("Non-empty Korean explanation with standard Apple DOCTYPE accepted");
            check(Sha(before)==Sha(File.ReadAllBytes(path)),"Valid plist bytes unchanged by validation");
            string validApp=Path.Combine(folder,"Valid.app");Directory.CreateDirectory(Path.Combine(validApp,"Contents"));
            File.Copy(path,Path.Combine(validApp,"Contents","Info.plist"));RedboolMacCameraPrivacy.ValidateApp(validApp);
            checks.Add("Valid app bundle fixture accepted through actual ValidateApp path");

            // A known broken release is an explicit negative fixture, never an
            // app to patch, launch, sign or silently accept for test convenience.
            string rejectedApp=Environment.GetEnvironmentVariable("REDBOOL_PRIVACY_CHECK_REJECT_APP");
            if(!String.IsNullOrEmpty(rejectedApp))
            {
                string actualPlist=Path.Combine(rejectedApp,"Contents","Info.plist");
                byte[] original=File.ReadAllBytes(actualPlist);bool rejected=false;
                try { RedboolMacCameraPrivacy.ValidateApp(rejectedApp); }
                catch(BuildFailedException) { rejected=true; }
                check(rejected,"Actual known-broken app rejected");
                check(Sha(original)==Sha(File.ReadAllBytes(actualPlist)),"Actual known-broken app plist unchanged");
                result.rejected_app=rejectedApp;result.rejected_plist_sha256=Sha(original);
            }
            string app = Environment.GetEnvironmentVariable("REDBOOL_PRIVACY_CHECK_APP");
            if (!String.IsNullOrEmpty(app)) { RedboolMacCameraPrivacy.ValidateApp(app); checks.Add("Supplied known-good app accepted"); }
            check(PlayerSettings.iOS.cameraUsageDescription==result.camera_usage_description,"Actual source setting unchanged");
            result.passed=true;
            Debug.Log("REDBOOL MAC CAMERA PRIVACY CHECKS PASS " + checks.Count + " camera_opened=false");
        }
        catch(Exception e) { result.error=e.ToString();throw; }
        finally
        {
            result.checks=checks.ToArray();
            string output=Environment.GetEnvironmentVariable("REDBOOL_PRIVACY_CHECK_OUTPUT");
            if(!String.IsNullOrEmpty(output)) { Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"result.json"),JsonUtility.ToJson(result,true)); }
            Directory.Delete(folder, true);
        }
    }
}
