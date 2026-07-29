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
        var loader = new BoundedSourceContentLoader(client);

        var result = await loader.LoadAsync(
            SourceDefinition.Create(SourceKind.Playlist, path),
            TestContext.Current.CancellationToken);

        Assert.True(result.EffectiveUri?.IsFile);
        Assert.Contains(
            "#EXTM3U",
            Encoding.UTF8.GetString(result.Content.Span));
    }

    [Fact]
    public async Task LoadAsync_reads_http_source_and_preserves_effective_uri()
    {
        var requestedUri = new Uri("https://provider.example/playlist.m3u");
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
        var loader = new BoundedSourceContentLoader(client);

        var result = await loader.LoadAsync(
            SourceDefinition.Create(SourceKind.Playlist, requestedUri.AbsoluteUri),
            TestContext.Current.CancellationToken);

        Assert.Equal(requestedUri, result.EffectiveUri);
        Assert.Equal("audio/x-mpegurl", result.ContentType);
        Assert.NotEmpty(result.Content.ToArray());
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
        var loader = new BoundedSourceContentLoader(client);
        var source = SourceDefinition.Create(
            SourceKind.Playlist,
            "https://provider.example/oversized.m3u");

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
        var loader = new BoundedSourceContentLoader(client);
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
