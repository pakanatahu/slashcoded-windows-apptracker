using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Slashcoded.DesktopTracker;

public sealed class TrustedUploadClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxUploadAttempts = 3;
    private const int MaxRequestBytes = 16 * 1024;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TrustedSourceCredentialStore _credentialStore;
    private readonly TrackerOptions _options;
    private readonly ILogger<TrustedUploadClient> _logger;
    private readonly SemaphoreSlim _credentialsGate = new(1, 1);
    private TrustedSourceCredentials? _cachedCredentials;

    public TrustedUploadClient(
        IHttpClientFactory httpClientFactory,
        TrustedSourceCredentialStore credentialStore,
        IOptions<TrackerOptions> options,
        ILogger<TrustedUploadClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credentialStore = credentialStore;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PostSignedJsonAsync(HttpMethod method, string path, object payload, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizePath(path);
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

        if (bodyBytes.Length > MaxRequestBytes)
        {
            throw new InvalidOperationException($"Upload payload exceeds {MaxRequestBytes} bytes limit.");
        }

        var client = _httpClientFactory.CreateClient();
        for (var attempt = 1; attempt <= MaxUploadAttempts; attempt++)
        {
            var credentials = await EnsureCredentialsAsync(cancellationToken);
            using var request = BuildSignedRequest(method, normalizedPath, bodyBytes, credentials);
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Trusted upload rejected with {Status}. Re-enrolling source before retry.", response.StatusCode);
                await ResetCredentialsAsync(cancellationToken);
                if (attempt < MaxUploadAttempts)
                {
                    continue;
                }
            }
            else if (response.StatusCode == HttpStatusCode.Conflict && attempt < MaxUploadAttempts)
            {
                _logger.LogWarning("Trusted upload nonce replay detected (409). Retrying with fresh signature.");
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                continue;
            }
            else if ((int)response.StatusCode >= 500 && attempt < MaxUploadAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                continue;
            }

            response.EnsureSuccessStatusCode();
        }
    }

    private HttpRequestMessage BuildSignedRequest(HttpMethod method, string path, byte[] bodyBytes, TrustedSourceCredentials credentials)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = GenerateNonce();
        var bodyHash = Convert.ToBase64String(SHA256.HashData(bodyBytes));
        var signatureBase = string.Concat(
            method.Method.ToUpperInvariant(), "\n",
            path, "\n",
            timestamp, "\n",
            nonce, "\n",
            bodyHash);
        var signature = Convert.ToBase64String(HMACSHA256.HashData(credentials.Secret, Encoding.UTF8.GetBytes(signatureBase)));

        var request = new HttpRequestMessage(method, $"{_options.ApiBaseUrl.TrimEnd('/')}{path}")
        {
            Content = new ByteArrayContent(bodyBytes)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation("X-Sc-Source-Id", credentials.SourceId);
        request.Headers.TryAddWithoutValidation("X-Sc-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-Sc-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("X-Sc-Signature", signature);
        return request;
    }

    private async Task<TrustedSourceCredentials> EnsureCredentialsAsync(CancellationToken cancellationToken)
    {
        if (_cachedCredentials is not null)
        {
            return _cachedCredentials;
        }

        await _credentialsGate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedCredentials is not null)
            {
                return _cachedCredentials;
            }

            var stored = await _credentialStore.LoadAsync(cancellationToken);
            if (stored is not null)
            {
                _cachedCredentials = stored;
                return stored;
            }

            var enrolled = await RegisterSourceAsync(cancellationToken);
            await _credentialStore.SaveAsync(enrolled, cancellationToken);
            _cachedCredentials = enrolled;
            return enrolled;
        }
        finally
        {
            _credentialsGate.Release();
        }
    }

    private async Task ResetCredentialsAsync(CancellationToken cancellationToken)
    {
        await _credentialsGate.WaitAsync(cancellationToken);
        try
        {
            _cachedCredentials = null;
            await _credentialStore.ClearAsync();
        }
        finally
        {
            _credentialsGate.Release();
        }
    }

    private async Task<TrustedSourceCredentials> RegisterSourceAsync(CancellationToken cancellationToken)
    {
        var request = new
        {
            clientId = "desktop-tracker",
            clientType = "desktop",
            machineId = ResolveMachineId(),
            displayName = "Windows App Tracker"
        };

        var client = _httpClientFactory.CreateClient();
        using var response = await client.PostAsJsonAsync($"{_options.ApiBaseUrl.TrimEnd('/')}/api/security/sources/register", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RegisterResponse>(cancellationToken: cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.SourceId) || string.IsNullOrWhiteSpace(payload.Secret))
        {
            throw new InvalidOperationException("Source registration response did not include source credentials.");
        }

        return new TrustedSourceCredentials(payload.SourceId, Encoding.UTF8.GetBytes(payload.Secret));
    }

    private static string GenerateNonce()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/api/upload";
        }

        return path.StartsWith('/') ? path : "/" + path;
    }

    private static string ResolveMachineId()
    {
        var machineGuid = ReadMachineGuid(RegistryView.Registry64) ?? ReadMachineGuid(RegistryView.Registry32);
        if (!string.IsNullOrWhiteSpace(machineGuid))
        {
            return machineGuid;
        }

        return Environment.MachineName;
    }

    private static string? ReadMachineGuid(RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }

    private sealed record RegisterResponse(string SourceId, string Secret);
}
