using System.Net;
using System.Text;
using RaiderLite.Core.Definitions;

namespace RaiderLite.Tests;

public class DefinitionDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RaiderLiteTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private const string Listing = """
        [
          {"name": "B_defs.xml", "type": "file", "download_url": "https://raw.example/B_defs.xml"},
          {"name": "README.md", "type": "file", "download_url": "https://raw.example/README.md"},
          {"name": "defs.inc", "type": "file", "download_url": "https://raw.example/defs.inc"},
          {"name": "sub", "type": "dir", "download_url": null},
          {"name": "nested.xml", "type": "dir", "download_url": null},
          {"name": "A_defs.XML", "type": "file", "download_url": "https://raw.example/A_defs.XML"},
          {"name": "..\\evil.xml", "type": "file", "download_url": "https://raw.example/evil.xml"},
          {"name": "plain.xml", "type": "file", "download_url": "http://raw.example/plain.xml"}
        ]
        """;

    /// <summary>Serves the listing and files from a dictionary; anything else is a 404.</summary>
    private sealed class FakeHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            return Task.FromResult(responses.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static Dictionary<string, string> Server(string bDefs = "<roms><rom/></roms>") => new()
    {
        [DefinitionDownloader.ListingUrl] = Listing,
        ["https://raw.example/A_defs.XML"] = "<roms/>",
        ["https://raw.example/B_defs.xml"] = bDefs,
    };

    [Fact]
    public void ListingKeepsOnlyTopLevelXmlFiles()
    {
        var entries = DefinitionDownloader.ParseListing(Listing);
        Assert.Equal(["A_defs.XML", "B_defs.xml"], entries.Select(e => e.Name));
    }

    [Fact]
    public async Task DownloadsXmlFilesAndReplacesEarlierCopies()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "B_defs.xml"), "old");
        File.WriteAllText(Path.Combine(_dir, "mine.xml"), "untouched");

        var handler = new FakeHandler(Server());
        var paths = await new DefinitionDownloader(new HttpClient(handler)).DownloadAsync(_dir);

        Assert.Equal([Path.Combine(_dir, "A_defs.XML"), Path.Combine(_dir, "B_defs.xml")], paths);
        Assert.Equal("<roms><rom/></roms>", File.ReadAllText(Path.Combine(_dir, "B_defs.xml")));
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(_dir, "mine.xml")));
        Assert.DoesNotContain(handler.Requested, u => u.EndsWith(".md") || u.EndsWith(".inc") || u.Contains("evil") || u.Contains("plain"));
        Assert.Equal(["A_defs.XML", "B_defs.xml", "mine.xml"], Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task InvalidXmlLeavesEarlierDownloadsUntouched()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "A_defs.XML"), "old");

        var downloader = new DefinitionDownloader(new HttpClient(new FakeHandler(Server(bDefs: "<html>oops"))));
        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(_dir));
        Assert.Equal("old", File.ReadAllText(Path.Combine(_dir, "A_defs.XML")));
    }

    [Fact]
    public async Task MissingFileFailsWithoutWriting()
    {
        var server = Server();
        server.Remove("https://raw.example/B_defs.xml");
        var downloader = new DefinitionDownloader(new HttpClient(new FakeHandler(server)));
        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadAsync(_dir));
        Assert.False(Directory.Exists(_dir));
    }
}
