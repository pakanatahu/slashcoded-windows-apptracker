namespace Slashcoded.DesktopObserver.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slashcoded.DesktopObserver;
using Xunit;

public sealed class TrustedUploadClientTests
{
    [Fact]
    public async Task PostSignedJsonAsync_EnrollsAsDesktopObserver()
    {
        var handler = new CaptureRegistrationHandler();
        var credentialPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}", "trusted-source.json");
        try
        {
            var client = new TrustedUploadClient(
                new StaticHttpClientFactory(new HttpClient(handler)),
                new TrustedSourceCredentialStore(credentialPath, NullLogger<TrustedSourceCredentialStore>.Instance),
                Options.Create(new ObserverOptions { ApiBaseUrl = "http://127.0.0.1:5292" }),
                NullLogger<TrustedUploadClient>.Instance);

            await client.PostSignedJsonAsync(HttpMethod.Post, "/api/upload", new { ok = true }, CancellationToken.None);

            Assert.NotNull(handler.RegistrationJson);
            using var document = JsonDocument.Parse(handler.RegistrationJson);
            Assert.Equal("desktop-observer", document.RootElement.GetProperty("clientId").GetString());
            Assert.Equal("Windows App Observer", document.RootElement.GetProperty("displayName").GetString());
        }
        finally
        {
            var credentialDirectory = Path.GetDirectoryName(credentialPath)!;
            if (Directory.Exists(credentialDirectory))
            {
                Directory.Delete(credentialDirectory, recursive: true);
            }
        }
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CaptureRegistrationHandler : HttpMessageHandler
    {
        public string? RegistrationJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/api/security/sources/register")
            {
                RegistrationJson = await request.Content!.ReadAsStringAsync(cancellationToken);
                return JsonResponse("""{"sourceId":"source-1","secret":"secret-1"}""");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }
}
