namespace PakkaHisaab.Maui.Services;

public interface IDeviceIntegrityService
{
    /// <summary>True when the device appears rooted (Android) or jailbroken (iOS).</summary>
    bool IsCompromised();
}

/// <summary>
/// Root/Jailbreak detection with platform-specific compile-time branches.
/// Policy: warn the user (financial data lives on this device) but do not block —
/// hard blocking is both bypassable and a store-review irritant.
/// </summary>
public sealed class DeviceIntegrityService : IDeviceIntegrityService
{
    public bool IsCompromised()
    {
#if ANDROID
        return IsAndroidRooted();
#elif IOS
        return IsIosJailbroken();
#else
        return false;
#endif
    }

#if ANDROID
    static bool IsAndroidRooted()
    {
        // 1) Test-keys build fingerprint
        var buildTags = Android.OS.Build.Tags;
        if (!string.IsNullOrEmpty(buildTags) && buildTags.Contains("test-keys"))
            return true;

        // 2) Common su / root-manager binaries
        string[] suspects =
        {
            "/system/app/Superuser.apk", "/sbin/su", "/system/bin/su", "/system/xbin/su",
            "/data/local/xbin/su", "/data/local/bin/su", "/system/sd/xbin/su",
            "/system/bin/failsafe/su", "/data/local/su", "/su/bin/su"
        };
        if (suspects.Any(File.Exists))
            return true;

        // 3) su reachable on PATH
        try
        {
            using var process = Java.Lang.Runtime.GetRuntime()?.Exec(new[] { "/system/xbin/which", "su" });
            using var reader = new StreamReader(process!.InputStream!);
            if (!string.IsNullOrEmpty(reader.ReadLine()))
                return true;
        }
        catch
        {
            // which not present — fine
        }

        return false;
    }
#endif

#if IOS
    static bool IsIosJailbroken()
    {
        // Simulators report paths that confuse the checks below.
        if (ObjCRuntime.Runtime.Arch == ObjCRuntime.Arch.SIMULATOR)
            return false;

        string[] suspects =
        {
            "/Applications/Cydia.app", "/Applications/Sileo.app", "/Library/MobileSubstrate/MobileSubstrate.dylib",
            "/bin/bash", "/usr/sbin/sshd", "/etc/apt", "/private/var/lib/apt/", "/usr/bin/ssh"
        };
        if (suspects.Any(File.Exists) || Directory.Exists("/private/var/lib/apt"))
            return true;

        // Sandbox violation probe: writing outside the container succeeds only when jailbroken.
        try
        {
            const string probe = "/private/ph_jb_probe.txt";
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
#endif
}
