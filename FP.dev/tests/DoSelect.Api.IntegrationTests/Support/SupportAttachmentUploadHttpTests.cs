using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Support;
using DoSelect.Application.Support.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Support;

public sealed class SupportAttachmentUploadHttpTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly RecordingUploadService _service = new();
    private readonly WebApplicationFactory<Program> _factory;

    public SupportAttachmentUploadHttpTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            TestAuthHandler.Configure(services);
            services.RemoveAll<ISupportAttachmentUploadService>();
            services.AddSingleton<ISupportAttachmentUploadService>(_service);
        }));
    }

    [Fact]
    public async Task AnonymousMultipartUpload_Returns401WithoutCallingApplication()
    {
        using var client = _factory.CreateClient();
        using var response = await PostMultipartAsync(client, Guid.NewGuid(), includeFile: true, authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_service.Calls);
    }

    [Fact]
    public async Task MultipartFile_IsAdaptedAnd201ResponseIsPublicSafe()
    {
        var id = Guid.NewGuid(); using var client = _factory.CreateClient();
        using var response = await PostMultipartAsync(client, id, includeFile: true, authenticated: true);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var call = Assert.Single(_service.Calls);
        Assert.Equal(("member-http", id, "photo.png", "image/png", 10L),
            (call.Member, call.Ticket, call.File.OriginalFileName, call.File.ClaimedContentType, call.File.DeclaredLength));
        Assert.Equal(5, json.EnumerateObject().Count());
        Assert.False(json.TryGetProperty("storageKey", out _)); Assert.False(json.TryGetProperty("sha256", out _));
        Assert.False(json.TryGetProperty("uploadedByUserId", out _));
    }

    [Fact]
    public async Task MissingFilePart_ReturnsValidationFailedWithoutCallingApplication()
    {
        using var client = _factory.CreateClient();
        using var response = await PostMultipartAsync(client, Guid.NewGuid(), includeFile: false, authenticated: true);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", json.RootElement.GetProperty("code").GetString());
        Assert.Empty(_service.Calls);
    }

    private static async Task<HttpResponseMessage> PostMultipartAsync(HttpClient client, Guid id, bool includeFile, bool authenticated)
    {
        if (authenticated) client.DefaultRequestHeaders.Add(TestAuthHandler.MemberHeaderName, "member-http");
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        tokenRequest.Headers.Add(SecurityController.ClientHeaderName, DoSelectClaimValues.Member);
        using var tokenResponse = await client.SendAsync(tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("requestToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        var multipart = new MultipartFormDataContent();
        if (includeFile)
        {
            var content = new ByteArrayContent([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2]);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            multipart.Add(content, "file", "photo.png");
        }
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/support-tickets/{id}/attachments") { Content = multipart };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private sealed class RecordingUploadService : ISupportAttachmentUploadService
    {
        public List<(string Member, Guid Ticket, IncomingAttachmentFile File)> Calls { get; } = [];
        public Task<SupportAttachmentDto> UploadAsync(string member, Guid ticket, IncomingAttachmentFile file, CancellationToken ct)
        {
            Calls.Add((member, ticket, file));
            return Task.FromResult(new SupportAttachmentDto(Guid.NewGuid(), "photo.png", "image/png", 10, DateTime.UtcNow));
        }
    }
}
