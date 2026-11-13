using System.Text.Json;
using GodotFrontend.Core.Camera;

namespace GodotFrontend.Core.Cli;

/// <summary>
/// A synthetic pointer script replayed straight into <see cref="OrbitControls"/>, bypassing the host's input
/// system, so the same script can be fed to the web app over the DevTools protocol and the two poses compared:
/// <code>{"viewport":{"w":1280,"h":800},"events":[{"t":"down","b":0,"x":400,"y":300,"mods":["shift"]},
/// {"t":"move","x":500,"y":300},{"t":"up","b":0},{"t":"wheel","dy":100}]}</code>
/// </summary>
public static class EventScript
{
    public static void Replay(string json, OrbitControls controls)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("viewport", out var vp))
            controls.SetViewport(vp.GetProperty("w").GetDouble(), vp.GetProperty("h").GetDouble());
        foreach (var e in root.GetProperty("events").EnumerateArray())
        {
            var t = e.GetProperty("t").GetString();
            switch (t)
            {
                case "down":
                {
                    var mods = OrbitModifiers.None;
                    if (e.TryGetProperty("mods", out var m))
                        foreach (var s in m.EnumerateArray())
                            mods |= s.GetString() switch
                            {
                                "ctrl" => OrbitModifiers.Ctrl,
                                "meta" => OrbitModifiers.Meta,
                                "shift" => OrbitModifiers.Shift,
                                _ => OrbitModifiers.None,
                            };
                    var b = e.TryGetProperty("b", out var bb) ? bb.GetInt32() : 0;
                    controls.BeginDrag(b switch { 0 => OrbitButton.Left, 1 => OrbitButton.Middle, 2 => OrbitButton.Right, _ => OrbitButton.Other },
                        mods, e.GetProperty("x").GetDouble(), e.GetProperty("y").GetDouble());
                    break;
                }
                case "move":
                    controls.Move(e.GetProperty("x").GetDouble(), e.GetProperty("y").GetDouble());
                    break;
                case "up":
                    controls.EndDrag();
                    break;
                case "wheel":
                    controls.Wheel(e.GetProperty("dy").GetDouble());
                    break;
                default:
                    throw new ArgumentException($"unknown event type '{t}'");
            }
        }
    }

    /// <summary>The pose as the web's getCameraPose() serialises it, so the two outputs diff directly.</summary>
    public static string PoseJson(CameraPose p)
    {
        static string V(Core.Math.Vec3 v) => $"{{\"x\":{R(v.X)},\"y\":{R(v.Y)},\"z\":{R(v.Z)}}}";
        static string R(double d) => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return $"{{\"position\":{V(p.Position)},\"target\":{V(p.Target)},\"up\":{V(p.Up)},\"fovY\":{R(p.FovY)},\"near\":{R(p.Near)},\"far\":{R(p.Far)}}}";
    }
}
