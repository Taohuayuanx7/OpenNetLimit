using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace NetMeter;

/// <summary>
/// Centralized persistence layer. All state files live under
/// %ProgramData%\NetMeter\ (administrators-only write, users read-only),
/// writes are guarded against reparse-point (symlink) hijacking and are
/// performed atomically via temp-file + rename.
/// </summary>
public static class StoragePaths
{
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "NetMeter");

    public static string LimitsPath => Path.Combine(Dir, "limits.json");
    public static string SettingsPath => Path.Combine(Dir, "settings.json");
    public static string DiagPath => Path.Combine(Dir, "diag.txt");

    /// <summary>
    /// Creates the storage directory with a hardened ACL: SYSTEM and
    /// Administrators get full control, regular Users get read-only access.
    /// Also migrates legacy files from %AppData%\NetMeter if present.
    /// </summary>
    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var dir = new DirectoryInfo(Dir);
            var security = dir.GetAccessControl();
            security.SetAccessRuleProtection(true, false); // no inheritance from above
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            dir.SetAccessControl(security);
        }
        catch
        {
            // Hardening is best-effort; the app must still run on exotic setups
        }

        MigrateFromLegacy();
    }

    private static void MigrateFromLegacy()
    {
        var legacyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetMeter");
        foreach (var name in new[] { "limits.json", "settings.json" })
        {
            try
            {
                var src = Path.Combine(legacyDir, name);
                var dst = Path.Combine(Dir, name);
                if (File.Exists(src) && !File.Exists(dst))
                    File.Copy(src, dst);
            }
            catch
            {
            }
        }
    }

    /// <summary>Refuses to read or write through a reparse point (symlink hijack).</summary>
    public static void EnsureNotReparsePoint(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Refusing to touch reparse point: {path}");
        }
    }

    /// <summary>Atomic write: temp file on the same volume, then replace/rename.</summary>
    public static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Dir);
        EnsureNotReparsePoint(path);
        var tmp = path + ".tmp";
        EnsureNotReparsePoint(tmp);
        File.WriteAllText(tmp, content);
        if (File.Exists(path))
            File.Replace(tmp, path, destinationBackupFileName: null);
        else
            File.Move(tmp, path);
    }

    public static string ReadIfSafe(string path)
    {
        EnsureNotReparsePoint(path);
        return File.ReadAllText(path);
    }

    public static void SafeAppend(string path, string content)
    {
        Directory.CreateDirectory(Dir);
        EnsureNotReparsePoint(path);
        File.AppendAllText(path, content);
    }

    public static void SafeWrite(string path, string content)
    {
        Directory.CreateDirectory(Dir);
        EnsureNotReparsePoint(path);
        File.WriteAllText(path, content);
    }
}

/// <summary>
/// Minimal diagnostic logger for engine-level errors. Writes to the shared
/// diag file from a background task; never throws into the caller.
/// </summary>
public static class DiagLog
{
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERR", message);

    private static void Write(string level, string message)
    {
        try
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    StoragePaths.SafeAppend(StoragePaths.DiagPath,
                        $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}\n");
                }
                catch
                {
                }
            });
        }
        catch
        {
        }
    }
}