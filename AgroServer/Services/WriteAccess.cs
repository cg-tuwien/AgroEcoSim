using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;

namespace AgroServer.Services;

/// <summary>Temporary shared-secret access to server mutations. Credentials never enter run data.</summary>
public sealed class WriteAccess
{
    public const string Header = "X-Agro-Write-Password";
    readonly byte[]? PasswordHash;

    public WriteAccess(IConfiguration configuration)
    {
        // The AGRO_ provider strips the prefix, but keeps single underscores: the conventional
        // AGRO_WRITE_PASSWORD becomes WRITE_PASSWORD, distinct from AGRO_WritePassword.
        // Keep the existing configuration key first for backwards-compatible precedence.
        var password = configuration["WritePassword"] ?? configuration["WRITE_PASSWORD"];
        if (!string.IsNullOrWhiteSpace(password))
            PasswordHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
    }

    public bool Configured => PasswordHash != null;

    public bool Allows(string? password) => PasswordHash != null
        && password is { Length: > 0 and <= 4096 }
        && CryptographicOperations.FixedTimeEquals(PasswordHash,
            SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    public string Error => Configured
        ? "WRITE_ACCESS_REQUIRED: Unlock writes with the shared password."
        : "WRITE_ACCESS_DISABLED: Server write access has no configured password.";
}

/// <summary>
/// Covers every hub invocation, including attachment and preview, which alter live runner state.
/// A credential is the final argument, separate from requests that are saved with a run.
/// </summary>
public sealed class WriteAccessHubFilter(WriteAccess access) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var password = context.HubMethodArguments.LastOrDefault() as string;
        if (!access.Allows(password)) throw new HubException(access.Error);
        try
        {
            var result = await next(context);
            // NativeAOT's reflection executor can return the boxed task through the filter
            // pipeline. Our RPCs return Task or Task<bool>; await them explicitly so neither a
            // task object nor an unfinished invocation becomes a JSON completion payload.
            if (result is Task<bool> boolean) return await boolean;
            if (result is Task task) { await task; return null; }
            return result;
        }
        catch (StorageException error) { throw new HubException(error.Message); }
    }
}
