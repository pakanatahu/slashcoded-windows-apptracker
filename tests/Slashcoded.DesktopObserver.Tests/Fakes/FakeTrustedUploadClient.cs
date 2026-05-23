namespace Slashcoded.DesktopObserver.Tests.Fakes;

using Slashcoded.DesktopObserver;

public sealed class FakeTrustedUploadClient : ITrustedUploadClient
{
    public List<object> Payloads { get; } = [];

    public Task PostSignedJsonAsync(HttpMethod method, string path, object payload, CancellationToken cancellationToken)
    {
        Payloads.Add(payload);
        return Task.CompletedTask;
    }
}
