using System.Net;
using System.Text;
using Efiron.Domain.Sources;
using Efiron.Infrastructure.Sources;
using Xunit;

namespace Efiron.Infrastructure.Tests.Sources;

public sealed class BoundedSourceContentLoaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Efiron.Tests",
        Guid.NewGuid().ToString("N"));

    private string CacheDirectory => Path.Combine(_directory, "source-cache");

    [Fact]
    public async Task LoadAsync_reads_local_playlist_and_returns_file_base_uri()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "playlist.m3u");
        await File.WriteAllTextAsync(
            path,
            "#EXTM3U\n#EXTINF:-1,Channel\nhttps://example.test/live.m3u8\n",
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new StubHandler(_ =>
            throw new InvalidOperationException("HTTP must not be used.")));
        var loader = new BoundedSourceContentLoader(client, CacheDirectory);

        var result = await loader.LoadAsync(
            SourceDefinition.Create(SourceKind.Playlist, path),
            TestContext.Current.CancellationToken);

        Assert.True(result.EffectiveUri?.IsFile);
        Assert.False(result.IsCacheHit);
        Assert.Contains(
            "#EXTM3U",
            Encoding.UTF8.GetString(result.Content.Span));
    }

    [Fact]
    public async Task LoadAsync_reads_http_source_and_preserves_effective_uri()
    {
        var requestedUri = new Uri(
            $"https://provider.example/{Guid.NewGuid():N}/playlist.m3u");
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(requestedUri, request.RequestUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(
                    "#EXTM3U\n#EXTINF:-1,Channel\nhttps://example.test/live.m3u8\n",
                    Encoding.UTF8,
                    "audio/x-mpegurl"),
            };
        }));
        var loader = new BoundedSourceContentLoader(client, CacheDirectory);

        var result = await loader.LoadAsync(
            SourceDefinition.Create(SourceKind.Playlist, requestedUri.AbsoluteUri),
            TestContext.Current.CancellationToken);

        Assert.Equal(requestedUri, result.EffectiveUri);
        Assert.Equal("audio/x-mpegurl", result.ContentType);
        Assert.False(result.IsCacheHit);
        Assert.NotEmpty(result.Content.ToArray());
    }

    [Fact]
    public async Task LoadAsync_returns_cached_remote_content_without_waiting_for_refresh()
    {
        var requestedUri = new Uri(
            $"https://provider.example/{Guid.NewGuid():N}/playlist.m3u");
        var responseNumber = 0;
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(requestedUri, request.RequestUri);
            responseNumber++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(
                    responseNumber == 1
                        ? "#EXTM3U\n#EXTINF:-1,First\nhttps://example.test/first.m3u8\n"
                        : "#EXTM3U\n#EXTINF:-1,Second\nhttps://example.test/second.m3u8\n",
                    Encoding.UTF8,
                    "audio/x-mpegurl"),
            };
        }));
        var loader = new BoundedSourceContentLoader(client, CacheDirectory);
        var source = SourceDefinition.Create(
            SourceKind.Playlist,
            requestedUri.AbsoluteUri);

        var first = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);
        var second = await loader.LoadAsync(
            source,
            TestContext.Current.CancellationToken);

        Assert.False(first.IsCacheHit);
        Assert.True(second.IsCacheHit);
        Assert.Contains("First", Encoding.UTF8.GetString(second.Content.Span));
    }

    [Fact]
    public async Task LoadAsync_rejects_declared_payload_above_limit_before_reading()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent([1]),
            };
            response.Content.Headers.ContentLength =
                BoundedSourceContentLoader.MaximumPlaylistBytes + 1L;
            return response;
        }));
        var loader = new BoundedSourceContentLoader(client, CacheDirectory);
        var source = SourceDefinition.Create(
            SourceKind.Playlist,
            $"https://provider.example/{Guid.NewGuid():N}/oversized.m3u");

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await loader.LoadAsync(
                source,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_rejects_unsupported_uri_scheme()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            throw new InvalidOperationException("HTTP must not be used.")));
        var loader = new BoundedSourceContentLoader(client, CacheDirectory);
        var source = SourceDefinition.Create(
            SourceKind.Playlist,
            "ftp://provider.example/playlist.m3u");

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await loader.LoadAsync(
                source,
                TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
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
