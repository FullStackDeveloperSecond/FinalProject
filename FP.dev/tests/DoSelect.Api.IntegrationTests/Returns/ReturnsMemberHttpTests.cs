using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Returns;

/// <summary>
/// A1 review finding: the opportunistic Member-authentication middleware in Program.cs (which
/// populates HttpContext.User with the Member principal BEFORE GlobalAntiforgeryFilter's
/// Authorization-stage check runs) used to cover only /api/v1/cart. Since
/// ReturnActorResolver's own AuthenticateAsync call happens inside the controller action — after
/// the antiforgery filter already ran — a signed-in member's Returns/Orders write would fail
/// antiforgery validation even with a correctly-minted token, because the antiforgery service's
/// identity-bound token comparison would see an anonymous HttpContext.User. These tests go
/// through the REAL ASP.NET Core pipeline (real cookie auth, real IAntiforgery validation, real
/// SQL Server, real disk-backed file storage) end to end and verify the actual DB/filesystem
/// effect, not just "the response wasn't 400".
/// </summary>
[Collection(nameof(ReturnsApiCollection))]
public sealed class ReturnsMemberHttpTests
{
    private readonly ReturnsApiFixture _fixture;

    public ReturnsMemberHttpTests(ReturnsApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateReturn_AsAuthenticatedMemberWithCookieAndAntiforgery_PersistsReturnRequestAndItems()
    {
        var (client, memberUserId, orderPublicId, orderItemPublicId, orderRowVersion) =
            await _fixture.CreateAuthenticatedMemberWithDeliveredOrderAsync(returnableQuantity: 2);

        var body = new
        {
            items = new[]
            {
                new { orderItemPublicId = orderItemPublicId, quantity = 1, reasonCode = "Defective", description = (string?)null },
            },
            requestReason = "面板有亮點",
            orderRowVersion = Convert.ToBase64String(orderRowVersion),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/{orderPublicId}/returns")
        {
            Content = JsonContent.Create(body),
        };

        using var response = await ReturnsApiFixture.SendWithAntiforgeryAsync(client, request);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected 201 but received {(int)response.StatusCode}: {responseBody}");

        using var document = JsonDocument.Parse(responseBody);
        var returnPublicId = document.RootElement.GetProperty("publicId").GetGuid();

        // Confirm the action actually executed and the data landed — not just a non-400 response.
        await using var context = _fixture.CreateScopedContext();
        var persisted = await context.ReturnRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.PublicId == returnPublicId);
        Assert.NotNull(persisted);
        Assert.Equal(memberUserId, persisted!.RequesterUserId);
        Assert.Equal("面板有亮點", persisted.Description);

        var items = await context.ReturnItems
            .AsNoTracking()
            .Where(i => i.ReturnRequestId == persisted.Id)
            .ToListAsync();
        Assert.Single(items);
        Assert.Equal(1, items[0].Quantity);
    }

    [Fact]
    public async Task UploadAttachment_AsAuthenticatedMemberWithCookieAndAntiforgery_PersistsAttachmentOnDisk()
    {
        var (client, memberUserId, orderPublicId, orderItemPublicId, orderRowVersion) =
            await _fixture.CreateAuthenticatedMemberWithDeliveredOrderAsync();

        var createBody = new
        {
            items = new[]
            {
                new { orderItemPublicId = orderItemPublicId, quantity = 1, reasonCode = "Defective", description = (string?)null },
            },
            requestReason = "面板有亮點",
            orderRowVersion = Convert.ToBase64String(orderRowVersion),
        };
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/{orderPublicId}/returns")
        {
            Content = JsonContent.Create(createBody),
        };
        using var createResponse = await ReturnsApiFixture.SendWithAntiforgeryAsync(client, createRequest);
        createResponse.EnsureSuccessStatusCode();
        using var createdDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var returnPublicId = createdDocument.RootElement.GetProperty("publicId").GetGuid();

        // Real PNG magic bytes — the real disk-backed storage runs genuine format validation.
        byte[] pngBytes = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 17, 255];
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "member-proof.png");

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/returns/{returnPublicId}/attachments")
        {
            Content = form,
        };
        using var uploadResponse = await ReturnsApiFixture.SendWithAntiforgeryAsync(client, uploadRequest);
        var uploadResponseBody = await uploadResponse.Content.ReadAsStringAsync();
        Assert.True(uploadResponse.StatusCode == HttpStatusCode.OK, $"Expected 200 but received {(int)uploadResponse.StatusCode}: {uploadResponseBody}");

        using var uploadDocument = JsonDocument.Parse(uploadResponseBody);
        var attachmentPublicId = uploadDocument.RootElement.GetProperty("publicId").GetGuid();

        await using var context = _fixture.CreateScopedContext();
        var persisted = await context.ReturnAttachments
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.PublicId == attachmentPublicId);
        Assert.NotNull(persisted);
        Assert.Equal(memberUserId, persisted!.UploadedByUserId);
        Assert.Null(persisted.UploadedByGuestOrderId);
        Assert.Null(persisted.DeletedAtUtc);
    }
}
