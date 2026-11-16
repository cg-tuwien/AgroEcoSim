namespace GodotFrontend.Core.Net;

/// <summary>Session-only write access. The credential has no public or serializable property.</summary>
public sealed class WriteAccess
{
    private string? password;
    private int revision;
    private readonly object gate = new();

    public bool IsUnlocked { get { lock (gate) return password != null; } }
    public event Action? Changed;

    public void Lock()
    {
        lock (gate) { ++revision; password = null; }
        Changed?.Invoke();
    }

    internal int Revision { get { lock (gate) return revision; } }
    internal string RequirePassword()
    {
        lock (gate) return password
            ?? throw new WriteAccessException("Writes are locked. Unlock writes with the shared password.");
    }

    internal void UnlockVerified(string value, int expectedRevision)
    {
        lock (gate)
        {
            if (expectedRevision != revision) throw new OperationCanceledException("Unlock was cancelled.");
            password = value;
        }
        Changed?.Invoke();
    }

    internal void HandleRejection(string message)
    {
        if (message.Contains("WRITE_ACCESS_REQUIRED", StringComparison.Ordinal)
            || message.Contains("WRITE_ACCESS_DISABLED", StringComparison.Ordinal)) Lock();
    }
}

public sealed class WriteAccessException(string message) : Exception(message);
