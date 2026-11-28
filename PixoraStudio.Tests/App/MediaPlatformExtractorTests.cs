using System.Net;
using PixoraStudio.App.Services;

namespace PixoraStudio.Tests.App;

public sealed class MediaPlatformExtractorTests
{
    [Fact]
    public void Registry_FindsOnlyMatchingExtractor()
    {
        var extractor = new RedditMediaPlatformExtractor(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        var registry = new MediaPlatformExtractorRegistry([extractor]);

        Assert.Same(extractor, registry.Find(new Uri("https://www.reddit.com/r/videos/comments/abc123/example/")));
        Assert.Null(registry.Find(new Uri("https://www.youtube.com/watch?v=abc123")));
    }

    [Fact]
    public async Task RedditExtractor_ResolvesPublicVideoPostToDirectMediaUrl()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("/r/videos/comments/abc123/example.json", request.RequestUri!.AbsolutePath);
            Assert.Equal("?raw_json=1", request.RequestUri.Query);
            Assert.Contains("PixoraStudio", request.Headers.UserAgent.ToString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                [
                  {
                    "data": {
                      "children": [
                        {
                          "data": {
                            "title": "A public clip",
                            "secure_media": {
                              "reddit_video": {
                                "fallback_url": "https://v.redd.it/clipid/DASH_720.mp4?source=fallback"
                              }
                            }
                          }
                        }
                      ]
                    }
                  }
                ]
                """)
            };
        });
        using var client = new HttpClient(handler);
        var extractor = new RedditMediaPlatformExtractor(client);

        var result = await extractor.ExtractAsync(
            new Uri("https://www.reddit.com/r/videos/comments/abc123/example/"),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Reddit", result.Platform);
        Assert.Equal("A public clip", result.Title);
        Assert.Equal("https://v.redd.it/clipid/DASH_720.mp4?source=fallback", result.DownloadUri.AbsoluteUri);
    }

    [Fact]
    public async Task RedditExtractor_ReturnsNullWhenPostHasNoSupportedVideo()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"data":{"children":[{"data":{"title":"Text only"}}]}}]""")
            }));
        var extractor = new RedditMediaPlatformExtractor(client);

        var result = await extractor.ExtractAsync(
            new Uri("https://www.reddit.com/r/example/comments/abc123/text/"),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RedditExtractor_DoesNotTreatAccessDeniedAsAnExtractionFailureToRetry()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var extractor = new RedditMediaPlatformExtractor(client);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => extractor.ExtractAsync(
            new Uri("https://www.reddit.com/r/example/comments/abc123/restricted/"),
            CancellationToken.None));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory(request));
        }
    }
}
