using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Storage;
using DoSelect.Application.Support;
using DoSelect.Domain.Members;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Support;

public sealed class SupportAttachmentUploadRoundTripTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;
    public SupportAttachmentUploadRoundTripTests(WebApplicationFactory<Program> baseFactory) => _baseFactory = baseFactory;

    [Fact]
    public async Task UploadThroughHost_ThenSecureReadService_ReturnsExactOriginalBytes()
    {
        var run = Guid.NewGuid().ToString("N");
        var taskRoot = Path.Combine(Path.GetTempPath(), $"doselect-upload-roundtrip-{run}");
        try
        {
            using var factory = _baseFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Storage:DataRoot"] = taskRoot }));
                builder.ConfigureTestServices(services =>
                {
                    TestAuthHandler.Configure(services);
                    services.RemoveAll<IFileScanner>();
                    services.AddSingleton<IFileScanner>(new CleanScanner());
                });
            });
            var (memberId, ticketId) = await SeedOwnerTicketAsync(factory, run);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.MemberHeaderName, memberId);
            var token = await GetAntiforgeryTokenAsync(client);
            var bytes = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 17, 255 };
            using var multipart = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            multipart.Add(file, "file", $"roundtrip-{run}.png");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/support-tickets/{ticketId}/attachments") { Content = multipart };
            request.Headers.Add("X-XSRF-TOKEN", token);

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var attachmentId = document.RootElement.GetProperty("publicId").GetGuid();

            using var scope = factory.Services.CreateScope();
            var readService = scope.ServiceProvider.GetRequiredService<ISupportAttachmentReadService>();
            var content = await readService.GetContentAsync(
                new(SupportAttachmentActorType.Member, memberId), attachmentId, default);
            await using (content.Content)
            {
                using var memory = new MemoryStream();
                await content.Content.CopyToAsync(memory);
                Assert.Equal(bytes, memory.ToArray());
            }
        }
        finally
        {
            if (Directory.Exists(taskRoot)) Directory.Delete(taskRoot, recursive: true);
        }
    }

    private static async Task<(string MemberId, Guid TicketPublicId)> SeedOwnerTicketAsync(
        WebApplicationFactory<Program> factory, string run)
    {
        var now = DateTime.UtcNow;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoSelectDbContext>();
        var member = ApplicationUser.CreateMember(Guid.NewGuid(), $"roundtrip-{run}@example.test", now);
        db.Users.Add(member);
        await db.SaveChangesAsync();
        var ticket = new SupportTicket(Guid.NewGuid(), $"RT-{run[..20]}", member.Id, null,
            SupportTicketCategory.Other, $"roundtrip-{run}", CasePriority.Normal,
            now.AddHours(1), now.AddHours(8), now);
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();
        return (member.Id, ticket.PublicId);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        request.Headers.Add(SecurityController.ClientHeaderName, DoSelectClaimValues.Member);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("requestToken").GetString()
            ?? throw new InvalidOperationException("The antiforgery endpoint returned an empty request token.");
    }

    private sealed class CleanScanner : IFileScanner
    {
        public Task<FileScanResult> ScanAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult(FileScanResult.Clean);
    }
}
