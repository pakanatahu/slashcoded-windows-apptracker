using System.Security.Cryptography;
using System.Text.Json;

namespace Slashcoded.DesktopTracker;

public sealed class TrustedSourceCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _credentialFilePath;
    private readonly ILogger<TrustedSourceCredentialStore> _logger;

    public TrustedSourceCredentialStore(ILogger<TrustedSourceCredentialStore> logger)
    {
        _logger = logger;
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Slashcoded",
            "DesktopTracker");
        _credentialFilePath = Path.Combine(basePath, "trusted-source.json");
    }

    public async Task<TrustedSourceCredentials?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_credentialFilePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_credentialFilePath);
            var persisted = await JsonSerializer.DeserializeAsync<PersistedCredentials>(stream, JsonOptions, cancellationToken);
            if (persisted is null || string.IsNullOrWhiteSpace(persisted.SourceId) || string.IsNullOrWhiteSpace(persisted.ProtectedSecret))
            {
                return null;
            }

            var protectedBytes = Convert.FromBase64String(persisted.ProtectedSecret);
            var secretBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            if (secretBytes.Length == 0)
            {
                return null;
            }

            return new TrustedSourceCredentials(persisted.SourceId, secretBytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Stored trusted source credentials are no longer readable; removing and re-enrolling.");
            DeleteCredentialFileIfPresent();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read trusted source credentials");
            return null;
        }
    }

    public async Task SaveAsync(TrustedSourceCredentials credentials, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_credentialFilePath)!);
        var protectedSecret = ProtectedData.Protect(credentials.Secret, null, DataProtectionScope.CurrentUser);
        var persisted = new PersistedCredentials(
            credentials.SourceId,
            Convert.ToBase64String(protectedSecret));

        await using var stream = File.Create(_credentialFilePath);
        await JsonSerializer.SerializeAsync(stream, persisted, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public Task ClearAsync()
    {
        try
        {
            if (File.Exists(_credentialFilePath))
            {
                File.Delete(_credentialFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to clear trusted source credentials");
        }

        return Task.CompletedTask;
    }

    private void DeleteCredentialFileIfPresent()
    {
        try
        {
            if (File.Exists(_credentialFilePath))
            {
                File.Delete(_credentialFilePath);
            }
        }
        catch (Exception deleteEx)
        {
            _logger.LogWarning(deleteEx, "Unable to remove unreadable trusted source credentials");
        }
    }

    private sealed record PersistedCredentials(string SourceId, string ProtectedSecret);
}

public sealed record TrustedSourceCredentials(string SourceId, byte[] Secret);
