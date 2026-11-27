using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PixoraStudio.App.Services;

public sealed record ExtractedMedia(
    string Platform,
    string Title,
    Uri DownloadUri);

public interface IMediaPlatformExtractor
{
    string PlatformName { get; }
    bool CanHandle(Uri pageUri);
    Task<ExtractedMedia?> ExtractAsync(Uri pageUri, CancellationToken cancellationToken);
}

public sealed class MediaPlatformExtractorRegistry(IEnumerable<IMediaPlatformExtractor> extractors)
{
    private readonly IMediaPlatformExtractor[] _extractors = extractors.ToArray();

    public IMediaPlatformExtractor? Find(Uri pageUri) =>
        _extractors.FirstOrDefault(extractor => extractor.CanHandle(pageUri));
}

public sealed class RedditMediaPlatformExtractor(HttpClient httpClient) : IMediaPlatformExtractor
{
    private const string RedditDomain = "reddit.com";
    private const string VideoDomain = "v.redd.it";

    public string PlatformName => "Reddit";

    public bool CanHandle(Uri pageUri) =>
        pageUri.Scheme == Uri.UriSchemeHttps &&
        (pageUri.Host.Equals(RedditDomain, StringComparison.OrdinalIgnoreCase) ||
         pageUri.Host.EndsWith("." + RedditDomain, StringComparison.OrdinalIgnoreCase) ||
         pageUri.Host.Equals("redd.it", StringComparison.OrdinalIgnoreCase));

    public async Task<ExtractedMedia?> ExtractAsync(Uri pageUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pageUri);
        if (!CanHandle(pageUri))
            return null;

        var jsonUri = BuildJsonUri(pageUri);
        using var request = new HttpRequestMessage(HttpMethod.Get, jsonUri);
        request.Headers.UserAgent.ParseAdd("PixoraStudio/1.0 (public media link resolver)");
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("Reddit requires access that PixoraStudio cannot provide for this post.");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidDataException("The Reddit post could not be found.");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var post = FindVideoPost(document.RootElement);
        if (post is null)
            return null;

        var title = post.Value.TryGetProperty("title", out var titleElement) &&
                    titleElement.ValueKind == JsonValueKind.String
            ? titleElement.GetString()
            : null;
        var videoUri = FindFallbackVideoUri(post.Value);
        if (videoUri is null)
            return null;

        return new ExtractedMedia(
            PlatformName,
            string.IsNullOrWhiteSpace(title) ? "Reddit video" : title,
            videoUri);
    }

    private static Uri BuildJsonUri(Uri pageUri)
    {
        var builder = new UriBuilder(pageUri)
        {
            Query = "raw_json=1"
        };
        var path = builder.Path.TrimEnd('/');
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            builder.Path = path + ".json";
        return builder.Uri;
    }

    private static JsonElement? FindVideoPost(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var found = FindVideoPost(item);
                if (found is not null)
                    return found;
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (FindFallbackVideoUri(root) is not null)
                return root;

            foreach (var property in root.EnumerateObject())
            {
                var found = FindVideoPost(property.Value);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    private static Uri? FindFallbackVideoUri(JsonElement post)
    {
        if (post.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var key in new[] { "secure_media", "media" })
        {
            if (!post.TryGetProperty(key, out var media) ||
                media.ValueKind != JsonValueKind.Object ||
                !media.TryGetProperty("reddit_video", out var video) ||
                video.ValueKind != JsonValueKind.Object ||
                !video.TryGetProperty("fallback_url", out var fallback) ||
                fallback.ValueKind != JsonValueKind.String)
                continue;

            var value = fallback.GetString();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                uri.Host.Equals(VideoDomain, StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                return uri;
        }

        return null;
    }
}
