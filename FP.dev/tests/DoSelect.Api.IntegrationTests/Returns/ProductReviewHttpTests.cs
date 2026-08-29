using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Auditing;
using DoSelect.Domain.Reviews;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Api.IntegrationTests.Returns;

[Collection(nameof(ReturnsApiCollection))]
public sealed class ProductReviewHttpTests(ReturnsApiFixture fixture)
{
    [Fact]
    public async Task IncompleteOrderItem_IsNotEligibleForReview()
    {
        var (client, orderItemPublicId) =
            await fixture.CreateAuthenticatedMemberWithPendingReviewItemAsync();

        using (var eligible = await client.GetAsync("/api/v1/reviews/eligible-order-items"))
        {
            eligible.EnsureSuccessStatusCode();
            Assert.Empty((await eligible.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        }

        using var create = await SendMemberJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/reviews",
            new
            {
                orderItemPublicId,
                rating = 5,
                title = "不能評價",
                content = "訂單尚未完成。",
                submit = true,
            });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task PurchasedReview_IsOwnerScopedModeratedRevisionedAndPublicOnlyWhenApproved()
    {
        var (memberClient, memberUserId, _, orderItemPublicId, _) =
            await fixture.CreateAuthenticatedMemberWithDeliveredOrderAsync();
        Guid productPublicId;
        await using (var context = fixture.CreateScopedContext())
        {
            productPublicId = await (
                from item in context.OrderItems
                join sku in context.Skus on item.SkuId equals sku.Id
                join product in context.Products on sku.ProductId equals product.Id
                where item.PublicId == orderItemPublicId
                select product.PublicId).SingleAsync();
        }

        using (var anonymous = fixture.CreateClient())
        using (var anonymousResponse = await anonymous.GetAsync("/api/v1/reviews/mine"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        }

        var otherMemberId = await SeedMemberAsync();
        using (var otherMember = fixture.CreateClient())
        {
            await SignInMemberAsync(otherMember, otherMemberId);
            using var forbidden = await SendMemberJsonAsync(
                otherMember,
                HttpMethod.Post,
                "/api/v1/reviews",
                new
                {
                    orderItemPublicId,
                    rating = 5,
                    title = "不屬於我的訂單",
                    content = "這個會員不能對別人的明細建立評價。",
                    submit = true,
                });
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using var create = await SendMemberJsonAsync(
            memberClient,
            HttpMethod.Post,
            "/api/v1/reviews",
            new
            {
                orderItemPublicId,
                rating = 4,
                title = "初次送審",
                content = "實際購買後的測試評價。",
                submit = true,
            });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var review = await ReadJsonAsync(create);
        var reviewPublicId = review.GetProperty("publicId").GetGuid();
        Assert.Equal("pendingReview", review.GetProperty("status").GetString());

        using (var invalidUpload = new HttpRequestMessage(
                   HttpMethod.Post,
                   $"/api/v1/reviews/{reviewPublicId}/images"))
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent("not-an-image"u8.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(file, "file", "not-an-image.txt");
            form.Add(new StringContent(review.GetProperty("rowVersion").GetString()!), "rowVersion");
            invalidUpload.Content = form;
            using var response = await ReturnsApiFixture.SendWithAntiforgeryAsync(memberClient, invalidUpload);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        await AssertPublicCountAsync(productPublicId, 0);

        var adminUserId = await SeedAdminAsync();
        using var adminClient = fixture.CreateClient();
        await SignInAdminAsync(adminClient, adminUserId);

        review = await ModerateAsync(
            adminClient,
            reviewPublicId,
            "reject",
            "content-policy",
            "請補充更具體的使用經驗",
            review.GetProperty("rowVersion").GetString()!);
        Assert.Equal("rejected", review.GetProperty("status").GetString());

        using (var update = await SendMemberJsonAsync(
                   memberClient,
                   HttpMethod.Put,
                   $"/api/v1/reviews/{reviewPublicId}",
                   new
                   {
                       rating = 5,
                       title = "修正後送審",
                       content = "補上安裝與使用細節後重新送審。",
                       rowVersion = review.GetProperty("rowVersion").GetString(),
                   }))
        {
            update.EnsureSuccessStatusCode();
            review = await ReadJsonAsync(update);
        }
        Assert.Equal("pendingReview", review.GetProperty("status").GetString());

        review = await ModerateAsync(
            adminClient,
            reviewPublicId,
            "approve",
            "content-approved",
            null,
            review.GetProperty("rowVersion").GetString()!);
        Assert.Equal("approved", review.GetProperty("status").GetString());
        await AssertPublicCountAsync(productPublicId, 1);

        using (var editPublished = await SendMemberJsonAsync(
                   memberClient,
                   HttpMethod.Put,
                   $"/api/v1/reviews/{reviewPublicId}",
                   new
                   {
                       rating = 5,
                       title = "公開後再修改",
                       content = "公開後修改的內容必須重新送審，不能短暫公開。",
                       rowVersion = review.GetProperty("rowVersion").GetString(),
                   }))
        {
            var editBody = await editPublished.Content.ReadAsStringAsync();
            Assert.True(editPublished.IsSuccessStatusCode,
                $"Expected published review edit success but received {(int)editPublished.StatusCode}: {editBody}");
            using var editDocument = JsonDocument.Parse(editBody);
            review = editDocument.RootElement.Clone();
        }
        Assert.Equal("pendingReview", review.GetProperty("status").GetString());
        await AssertPublicCountAsync(productPublicId, 0);

        using (var upload = new HttpRequestMessage(
                   HttpMethod.Post,
                   $"/api/v1/reviews/{reviewPublicId}/images"))
        {
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
            var form = new MultipartFormDataContent();
            var image = new ByteArrayContent(png);
            image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(image, "file", "verified-purchase.png");
            form.Add(new StringContent(review.GetProperty("rowVersion").GetString()!), "rowVersion");
            upload.Content = form;
            using var uploadResponse = await ReturnsApiFixture.SendWithAntiforgeryAsync(memberClient, upload);
            uploadResponse.EnsureSuccessStatusCode();
            review = await ReadJsonAsync(uploadResponse);
            Assert.Single(review.GetProperty("images").EnumerateArray());
        }

        review = await ModerateAsync(
            adminClient,
            reviewPublicId,
            "approve",
            "content-approved",
            null,
            review.GetProperty("rowVersion").GetString()!);
        var publicReview = await AssertPublicCountAsync(productPublicId, 1);
        Assert.True(publicReview.GetProperty("isVerifiedPurchase").GetBoolean());
        var imageUrl = publicReview.GetProperty("images")[0].GetProperty("url").GetString()!;
        using (var imageResponse = await fixture.CreateClient().GetAsync(imageUrl))
        {
            Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
            Assert.Equal("image/webp", imageResponse.Content.Headers.ContentType?.MediaType);
        }

        review = await ModerateAsync(
            adminClient,
            reviewPublicId,
            "hide",
            "content-hidden",
            "內容需暫時下架",
            review.GetProperty("rowVersion").GetString()!);
        Assert.Equal("hidden", review.GetProperty("status").GetString());
        await AssertPublicCountAsync(productPublicId, 0);

        review = await ModerateAsync(
            adminClient,
            reviewPublicId,
            "restore",
            "content-restored",
            "複核後恢復",
            review.GetProperty("rowVersion").GetString()!);
        Assert.Equal("approved", review.GetProperty("status").GetString());
        await AssertPublicCountAsync(productPublicId, 1);

        await using var verify = fixture.CreateScopedContext();
        var persisted = await verify.ProductReviews.AsNoTracking()
            .SingleAsync(candidate => candidate.PublicId == reviewPublicId);
        Assert.Equal(memberUserId, persisted.MemberUserId);
        Assert.Equal(2, await verify.ProductReviewRevisions.CountAsync(
            revision => revision.ProductReviewId == persisted.Id));
        Assert.Equal(5, await verify.AuditLogs.CountAsync(
            audit => audit.ResourcePublicId == reviewPublicId &&
                audit.ResourceType == AuditResourceTypes.ProductReview));
    }

    private async Task<string> SeedMemberAsync()
    {
        await using var context = fixture.CreateScopedContext();
        var member = ApplicationUser.CreateMember(
            Guid.CreateVersion7(), $"review-member-{Guid.NewGuid():N}@example.test", DateTime.UtcNow);
        context.Users.Add(member);
        await context.SaveChangesAsync();
        return member.Id;
    }

    private async Task<string> SeedAdminAsync()
    {
        await using var context = fixture.CreateScopedContext();
        var admin = ApplicationUser.CreateAdmin(
            Guid.CreateVersion7(), $"review-admin-{Guid.NewGuid():N}@example.test", DateTime.UtcNow);
        var role = new IdentityRole(DoSelectRoles.CustomerService);
        context.AddRange(admin, role);
        await context.SaveChangesAsync();
        context.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = admin.Id,
            RoleId = role.Id,
        });
        await context.SaveChangesAsync();
        return admin.Id;
    }

    private static async Task SignInMemberAsync(HttpClient client, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/member")
        {
            Content = JsonContent.Create(new
            {
                includeMfa = false,
                roles = Array.Empty<string>(),
                userId,
            }),
        };
        request.Headers.Add("X-XSRF-TOKEN", await ReturnsApiFixture.GetMemberAntiforgeryTokenAsync(client));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task SignInAdminAsync(HttpClient client, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/security/sign-in/admin")
        {
            Content = JsonContent.Create(new
            {
                includeMfa = true,
                roles = new[] { DoSelectRoles.CustomerService },
                userId,
            }),
        };
        request.Headers.Add("X-XSRF-TOKEN", await ReturnsApiFixture.GetAdminAntiforgeryTokenAsync(client));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SendMemberJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        return await ReturnsApiFixture.SendWithAntiforgeryAsync(client, request);
    }

    private static async Task<JsonElement> ModerateAsync(
        HttpClient client,
        Guid reviewPublicId,
        string action,
        string reasonCode,
        string? note,
        string rowVersion)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/reviews/{reviewPublicId}/actions/{action}")
        {
            Content = JsonContent.Create(new { reasonCode, note, rowVersion }),
        };
        using var response = await ReturnsApiFixture.SendWithAdminAntiforgeryAsync(client, request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode,
            $"Expected moderation success but received {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private async Task<JsonElement> AssertPublicCountAsync(Guid productPublicId, int expected)
    {
        using var response = await fixture.CreateClient().GetAsync(
            $"/api/v1/products/{productPublicId}/reviews");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, body.GetArrayLength());
        return expected == 0 ? default : body[0].Clone();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
