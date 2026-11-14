namespace AgroServer.Services;

/// <summary>
/// Where the run store's bytes actually land, and how much room is left there.
///
/// Written because the deployed store was not where anyone thought it was. The image wrote to
/// /data/runstore while docker-compose mounted the named volume at /data/AgroEcoStorage, so every
/// run went to the anonymous volume Docker creates for the image's VOLUME /data - on the host's
/// root disk, against a retention budget of a terabyte. Nothing could notice: the paths were all
/// valid, the writes all succeeded, until the disk was full.
///
/// The kernel knows the answer, and this asks it. /proc/self/mountinfo says which mount a path is
/// on, which filesystem that mount is, and what it is a mount OF - for a Docker volume, a directory
/// ending /volumes/&lt;name&gt;/_data, where an anonymous volume's name is 64 hex characters.
/// </summary>
public sealed class StoreVolume
{
    /// <summary>The store root as an absolute path.</summary>
    public string Root { get; private init; } = "";
    /// <summary>Free bytes available to this process on the root's filesystem; -1 when unknown.</summary>
    public long FreeBytes { get; private init; } = -1;
    public long TotalBytes { get; private init; } = -1;
    /// <summary>The mount point containing the root. Empty off Linux.</summary>
    public string MountPoint { get; private init; } = "";
    public string FileSystem { get; private init; } = "";
    /// <summary>The Docker volume behind that mount, or empty when it is not one.</summary>
    public string VolumeName { get; private init; } = "";
    public bool AnonymousVolume { get; private init; }
    public bool InContainer { get; private init; }
    /// <summary>Directory mounts inside the root's own mount that do not contain the root - a
    /// volume mounted next to the store rather than under it.</summary>
    public IReadOnlyList<string> NestedMounts { get; private init; } = [];

    /// <summary>statvfs on the root: what a write there can still use. -1 when it cannot be read.</summary>
    public static long FreeSpace(string root)
    {
        try { return new DriveInfo(root).AvailableFreeSpace; }
        catch { return -1; }
    }

    /// <summary>
    /// Never throws. Anything it cannot determine is left at its "unknown" value.
    ///
    /// <c>mountInfo</c> replaces /proc/self/mountinfo, so the Docker layouts this exists to
    /// recognise can be checked on a machine that is not running in one.
    /// </summary>
    public static StoreVolume Probe(string root, IEnumerable<string>? mountInfo = null)
    {
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); }
        catch { full = root; }

        long free = -1, total = -1;
        string format = "";
        try
        {
            var drive = new DriveInfo(full);
            free = drive.AvailableFreeSpace;
            total = drive.TotalSize;
            format = drive.DriveFormat;
        }
        catch { /* the root may not exist yet, or the filesystem may refuse statvfs */ }

        var inContainer = File.Exists("/.dockerenv") || File.Exists("/run/.containerenv");
        if (mountInfo == null && !OperatingSystem.IsLinux())
            return new() { Root = full, FreeBytes = free, TotalBytes = total, FileSystem = format, InContainer = inContainer };

        var mounts = ReadMounts(mountInfo);
        //The LAST entry wins among equals: a later mount on the same point shadows the earlier one.
        (string Point, string FsType, string Source)? best = null;
        foreach (var m in mounts)
            if (Contains(m.Point, full) && (best == null || m.Point.Length >= best.Value.Point.Length))
                best = m;

        var point = best?.Point ?? "";
        var volume = "";
        if (best?.Source is { } source && source.EndsWith("/_data", StringComparison.Ordinal))
        {
            var at = source.LastIndexOf("/volumes/", StringComparison.Ordinal);
            if (at >= 0)
            {
                var name = source[(at + "/volumes/".Length)..^"/_data".Length];
                if (name.Length > 0 && !name.Contains('/')) volume = name;
            }
        }

        var nested = new List<string>();
        if (point.Length > 0)
            foreach (var m in mounts)
            {
                if (m.Point == point || !Contains(point, m.Point) || Contains(m.Point, full)) continue;
                //Files are mounted too - /etc/hosts and friends in every container - and a store
                //cannot live in one.
                if (IsSystemPath(m.Point) || File.Exists(m.Point)) continue;
                if (!nested.Contains(m.Point)) nested.Add(m.Point);
            }

        return new()
        {
            Root = full,
            FreeBytes = free,
            TotalBytes = total,
            MountPoint = point,
            FileSystem = best?.FsType ?? format,
            VolumeName = volume,
            AnonymousVolume = volume.Length == 64 && volume.All(char.IsAsciiHexDigitLower),
            InContainer = inContainer,
            NestedMounts = nested,
        };
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="mount"/> or lies under it.</summary>
    static bool Contains(string mount, string path) =>
        mount == "/" || path == mount || path.StartsWith(mount + "/", StringComparison.Ordinal);

    /// <summary>Mounts a container always has and a store never lives in.</summary>
    static bool IsSystemPath(string point) =>
        point is "/proc" or "/sys" or "/dev" or "/run" or "/etc"
        || point.StartsWith("/proc/", StringComparison.Ordinal) || point.StartsWith("/sys/", StringComparison.Ordinal)
        || point.StartsWith("/dev/", StringComparison.Ordinal) || point.StartsWith("/run/", StringComparison.Ordinal)
        || point.StartsWith("/etc/", StringComparison.Ordinal);

    /// <summary>
    /// The mount table: point, filesystem type, and the mount's root within its source.
    ///
    /// Format, per proc(5): <c>id parent major:minor root point options [optional...] - type source super</c>.
    /// Spaces and a few other characters are octal-escaped (<c>\040</c>).
    /// </summary>
    static List<(string Point, string FsType, string Source)> ReadMounts(IEnumerable<string>? mountInfo)
    {
        var result = new List<(string, string, string)>();
        try
        {
            foreach (var line in mountInfo ?? File.ReadLines("/proc/self/mountinfo"))
            {
                var fields = line.Split(' ');
                var dash = Array.IndexOf(fields, "-");
                if (fields.Length < 7 || dash < 6 || dash + 1 >= fields.Length) continue;
                result.Add((Unescape(fields[4]), fields[dash + 1], Unescape(fields[3])));
            }
        }
        catch { /* not Linux after all, or /proc is not mounted */ }
        return result;
    }

    static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var output = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; ++i)
        {
            if (s[i] == '\\' && i + 3 < s.Length && IsOctal(s[i + 1]) && IsOctal(s[i + 2]) && IsOctal(s[i + 3]))
            {
                output.Append((char)((s[i + 1] - '0') * 64 + (s[i + 2] - '0') * 8 + (s[i + 3] - '0')));
                i += 3;
            }
            else output.Append(s[i]);
        }
        return output.ToString();
    }

    static bool IsOctal(char c) => c is >= '0' and <= '7';
}
