namespace AgroServer.Services;

/// <summary>A requested operation cannot be kept durably by the run store.</summary>
public sealed class StorageException : IOException
{
    public bool InsufficientSpace { get; }
    public string? RunId { get; }

    public StorageException(string message, bool insufficientSpace = false, string? runId = null,
                            Exception? innerException = null) : base(message, innerException)
    {
        InsufficientSpace = insufficientSpace;
        RunId = runId;
    }

    public static StorageException From(string message, Exception error, string? runId = null) =>
        new(message, IsInsufficientSpace(error), runId, error);

    static bool IsInsufficientSpace(Exception error) =>
        error is StorageException { InsufficientSpace: true }
        || error.HResult is 28 or 122 or unchecked((int)0x80070070) or unchecked((int)0x80070027)
        || error.Message.Contains("No space", StringComparison.OrdinalIgnoreCase)
        || error.Message.Contains("quota", StringComparison.OrdinalIgnoreCase)
        || error.InnerException is { } inner && IsInsufficientSpace(inner);
}

/// <summary>The outcome after all queued frames and checkpoints have reached the store.</summary>
public sealed record RunCompletion(bool Aborted, string? Reason, bool StorageFailed = false);
