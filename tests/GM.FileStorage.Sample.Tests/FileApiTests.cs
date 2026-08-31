using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace GM.FileStorage.Sample.Tests;

// Boots the sample API against a throwaway storage root — no external services needed.
public sealed class FileApiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gm-fs-sample-tests", Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public FileApiTests() =>
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("FileStorage:Local:RootPath", _root));

    private sealed record UploadResponse(string Key, long Size, string Checksum, string ContentType);

    [Fact]
    public async Task Upload_ThenDownload_RoundTripsTheFile()
    {
        var client = _factory.CreateClient();
        var bytes = Encoding.UTF8.GetBytes("passport scan bytes");

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "passport.pdf");

        var upload = await client.PostAsync("/api/v1/files?tenantId=acme&userId=u1", form);
        upload.EnsureSuccessStatusCode();
        var result = await upload.Content.ReadFromJsonAsync<UploadResponse>();

        Assert.NotNull(result);
        Assert.StartsWith("tenants/acme/users/u1/", result.Key);      // tenant/user-scoped key
        Assert.Equal(bytes.Length, result.Size);

        var download = await client.GetAsync($"/api/v1/files/{result.Key}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Download_UnknownKey_Returns404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/files/tenants/x/users/y/does-not-exist.bin");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
