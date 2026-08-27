using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Domain.Returns;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Returns;

[Collection(nameof(ReturnsApiCollection))]
public sealed class AdminReturnDetailHttpTests
{
    private readonly ReturnsApiFixture _fixture;

    public AdminReturnDetailHttpTests(ReturnsApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetDetail_NewReturnWithoutInspections_Returns200WithEmptyInspections()
    {
        var (returnPublicId, _, _, _) = await _fixture.SeedReturnAsync(ReturnRequestStatus.Requested);
        using var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();

        using var response = await client.GetAsync($"/api/v1/admin/returns/{returnPublicId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("inspections").EnumerateArray());
    }

    [Fact]
    public async Task GetDetail_MultipleInspections_Returns200OrderedByTimestampThenIdentity()
    {
        var (returnPublicId, _, _, _) = await _fixture.SeedReturnAsync(
            ReturnRequestStatus.Received,
            itemCount: 3);
        await SeedInspectionsAsync(returnPublicId);
        using var client = await _fixture.CreateAuthenticatedOrderManagerClientAsync();

        using var response = await client.GetAsync($"/api/v1/admin/returns/{returnPublicId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var conditionCodes = body.GetProperty("inspections")
            .EnumerateArray()
            .Select(row => row.GetProperty("conditionCode").GetString()!)
            .ToArray();
        Assert.Equal(["EARLY", "SAME-FIRST", "SAME-SECOND"], conditionCodes);
    }

    private async Task SeedInspectionsAsync(Guid returnPublicId)
    {
        await using var context = _fixture.CreateScopedContext();
        var nowUtc = DateTime.UtcNow;
        var admin = ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(),
            $"{Guid.NewGuid():N}@doselect.test",
            nowUtc);
        context.Users.Add(admin);
        await context.SaveChangesAsync();

        var returnRequestId = await context.ReturnRequests
            .Where(request => request.PublicId == returnPublicId)
            .Select(request => request.Id)
            .SingleAsync();
        var itemIds = await context.ReturnItems
            .Where(item => item.ReturnRequestId == returnRequestId)
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToListAsync();

        var sharedTimestamp = nowUtc.AddMinutes(-1);
        var rows = new[]
        {
            new ReturnInspection(Guid.CreateVersion7(), itemIds[0], "Accepted", "SAME-FIRST", null, admin.Id, sharedTimestamp),
            new ReturnInspection(Guid.CreateVersion7(), itemIds[1], "Accepted", "SAME-SECOND", null, admin.Id, sharedTimestamp),
            new ReturnInspection(Guid.CreateVersion7(), itemIds[2], "Accepted", "EARLY", null, admin.Id, nowUtc.AddMinutes(-2)),
        };

        foreach (var row in rows)
        {
            context.ReturnInspections.Add(row);
            await context.SaveChangesAsync();
        }
    }
}
