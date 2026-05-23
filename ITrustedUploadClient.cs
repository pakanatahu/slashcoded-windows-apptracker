namespace Slashcoded.DesktopObserver;

public interface ITrustedUploadClient
{
    Task PostSignedJsonAsync(HttpMethod method, string path, object payload, CancellationToken cancellationToken);
}
