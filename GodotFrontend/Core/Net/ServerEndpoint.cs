namespace GodotFrontend.Core.Net;

/// <summary>Where AgroServer is. Resolution order: command line, AGRO_SERVER, saved settings, the default.</summary>
public sealed class ServerEndpoint
{
    public const string Default = "http://localhost:7215";
    public Uri Base { get; }
    public Uri Hub => new(Base, "SimSocket");
    public Uri Rest(string path) => new(Base, "Simulation/" + path.TrimStart('/'));

    public ServerEndpoint(string url)
    {
        var s = url.Trim();
        if (!s.Contains("://")) s = "http://" + s;
        if (!s.EndsWith('/')) s += "/";
        Base = new Uri(s, UriKind.Absolute);
    }

    public static ServerEndpoint Resolve(string? cli, string? env, string? saved)
    {
        foreach (var c in new[] { cli, env, saved })
            if (!string.IsNullOrWhiteSpace(c)) return new ServerEndpoint(c!);
        return new ServerEndpoint(Default);
    }

    public override string ToString() => Base.ToString().TrimEnd('/');
}
