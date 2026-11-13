using System.Globalization;

namespace GodotFrontend.Core.Cli;

/// <summary>
/// The app's own command line, parsed from the arguments after Godot's "--" separator. Both "--key value" and
/// "--key=value" are accepted. Unknown keys are kept in <see cref="Unknown"/> so a typo is reported rather than
/// silently ignored.
/// </summary>
public sealed class DebugArgs
{
    public string? Server;
    public string? Load;
    /// <summary>A single stored frame (.g zlib or raw scene bytes) to show without a session or a server.</summary>
    public string? FrameFile;
    /// <summary>Timeline index to display after loading; -1 means the last frame.</summary>
    public int? Frame;
    /// <summary>px,py,pz,tx,ty,tz merged into the camera pose, as still.mjs CAMERA= does.</summary>
    public double[]? Camera;
    public int? Width, Height;
    public int Ssaa = 1;
    public string? Screenshot;
    /// <summary>Capture the whole window (side panel included) after --settle frames, then quit.</summary>
    public string? UiScreenshot;
    public bool DumpPose;
    public string? Replay;
    /// <summary>Like --replay, but injected as real input events through the viewport (exercises the input routing too); quits when done.</summary>
    public string? ReplayInput;
    public bool NoUi;
    public bool WheelAccumulate;
    public int Settle = 3;
    public double TimeoutSeconds = 120;
    public bool Quit;
    /// <summary>Comma-separated lighting switches to force: daylight, shadows, occlusion, indirect, bounce, translucency, sky (or "off").</summary>
    public string? Lighting;
    /// <summary>A visual mapping to force (natural, water, energy, ...).</summary>
    public string? Mapping;
    /// <summary>Start a run of this many simulated hours on startup (with --screenshot: capture its last frame).</summary>
    public int? Simulate;
    public readonly List<string> Unknown = new();

    public static DebugArgs Parse(IReadOnlyList<string> args)
    {
        var a = new DebugArgs();
        for (var i = 0; i < args.Count; ++i)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) { a.Unknown.Add(arg); continue; }
            string key, value = "";
            var eq = arg.IndexOf('=');
            var hasValue = false;
            if (eq >= 0)
            {
                key = arg[2..eq];
                value = arg[(eq + 1)..];
                hasValue = true;
            }
            else
            {
                key = arg[2..];
            }

            string Next()
            {
                if (hasValue) return value;
                if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) return args[++i];
                return "";
            }

            switch (key)
            {
                case "server": a.Server = Next(); break;
                case "load": a.Load = Next(); break;
                case "frame-file": a.FrameFile = Next(); break;
                case "frame": a.Frame = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "camera":
                    a.Camera = Next().Split(',').Select(s => double.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
                    if (a.Camera.Length != 6) throw new ArgumentException("--camera expects px,py,pz,tx,ty,tz");
                    break;
                case "size":
                {
                    var parts = Next().ToLowerInvariant().Split('x');
                    if (parts.Length != 2) throw new ArgumentException("--size expects WxH");
                    a.Width = int.Parse(parts[0], CultureInfo.InvariantCulture);
                    a.Height = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    break;
                }
                case "ssaa": a.Ssaa = System.Math.Clamp(int.Parse(Next(), CultureInfo.InvariantCulture), 1, 4); break;
                case "screenshot": a.Screenshot = Next(); break;
                case "ui-screenshot": a.UiScreenshot = Next(); break;
                case "dump-pose": a.DumpPose = true; break;
                case "replay": a.Replay = Next(); break;
                case "replay-input": a.ReplayInput = Next(); break;
                case "no-ui": a.NoUi = true; break;
                case "wheel-accumulate": a.WheelAccumulate = true; break;
                case "settle": a.Settle = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "timeout": a.TimeoutSeconds = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "quit": a.Quit = true; break;
                case "lighting": a.Lighting = Next(); break;
                case "mapping": a.Mapping = Next(); break;
                case "debug-lighting": break;
                case "debug-picking": break;
                case "simulate": a.Simulate = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                default: a.Unknown.Add(arg); break;
            }
        }
        return a;
    }
}
