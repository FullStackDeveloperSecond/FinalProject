using System.Net;
using System.Net.Http.Json;
using DoSelect.Api.Contracts.Members;

namespace DoSelect.Api.IntegrationTests.Members;

/// <summary>M 會員資料／收件地址支撐（MembersController，工程包切片 2 後半）。</summary>
[Collection(nameof(MembersApiCollection))]
public sealed class MembersApiTests(MembersApiFixture fixture)
{
    [Fact]
    public async Task GetProfile_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/api/v1/members/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_ReturnsTheAuthenticatedMembersOwnProfile()
    {
        var (client, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        using var _disposeClient = client;

        using var response = await client.GetAsync("/api/v1/members/me");

        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<MemberProfileResponse>();
        Assert.Equal("測試會員", profile!.DisplayName);
        Assert.False(profile.EmailVerified);
        Assert.Equal("zh-TW", profile.Locale);
    }

    [Fact]
    public async Task UpdateProfile_PersistsDisplayNamePhoneAndLocale()
    {
        var (client, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        using var _disposeClient = client;
        var current = await (await client.GetAsync("/api/v1/members/me"))
            .Content.ReadFromJsonAsync<MemberProfileResponse>();

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/members/me")
        {
            Content = JsonContent.Create(new
            {
                displayName = "新的顯示名稱",
                phone = "0987654321",
                locale = "ja-JP",
                rowVersion = current!.RowVersion,
            }),
        };
        using var response = await MembersApiFixture.SendWithAntiforgeryAsync(client, request);

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<MemberProfileResponse>();
        Assert.Equal("新的顯示名稱", updated!.DisplayName);
        Assert.Equal("0987654321", updated.Phone);
        Assert.Equal("ja-JP", updated.Locale);
    }

    [Fact]
    public async Task UpdateProfile_WithStaleRowVersion_ReturnsConcurrencyConflict()
    {
        var (client, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        using var _disposeClient = client;
        var current = await (await client.GetAsync("/api/v1/members/me"))
            .Content.ReadFromJsonAsync<MemberProfileResponse>();

        // Win the race first so the RowVersion captured above is now stale.
        using (var firstUpdate = new HttpRequestMessage(HttpMethod.Put, "/api/v1/members/me")
        {
            Content = JsonContent.Create(new
            {
                displayName = "第一次更新",
                phone = (string?)null,
                locale = "zh-TW",
                rowVersion = current!.RowVersion,
            }),
        })
        {
            using var firstResponse = await MembersApiFixture.SendWithAntiforgeryAsync(client, firstUpdate);
            firstResponse.EnsureSuccessStatusCode();
        }

        using var staleRequest = new HttpRequestMessage(HttpMethod.Put, "/api/v1/members/me")
        {
            Content = JsonContent.Create(new
            {
                displayName = "第二次更新（用舊 RowVersion）",
                phone = (string?)null,
                locale = "zh-TW",
                rowVersion = current.RowVersion,
            }),
        };
        using var response = await MembersApiFixture.SendWithAntiforgeryAsync(client, staleRequest);

        var (status, code, _) = await MembersApiFixture.ReadProblemAsync(response);
        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", code);
    }

    [Fact]
    public async Task Addresses_CreateThenList_ReturnsTheCreatedAddress()
    {
        var (client, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        using var _disposeClient = client;

        var created = await CreateAddressAsync(client, label: "住家", isDefault: true);

        using var listResponse = await client.GetAsync("/api/v1/members/me/addresses");
        listResponse.EnsureSuccessStatusCode();
        var addresses = await listResponse.Content.ReadFromJsonAsync<List<MemberAddressResponse>>();

        var found = Assert.Single(addresses!);
        Assert.Equal(created.PublicId, found.PublicId);
        Assert.True(found.IsDefault);
    }

    [Fact]
    public async Task Addresses_CreatingASecondDefault_ClearsThePreviousDefault()
    {
        var (client, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        using var _disposeClient = client;
        var first = await CreateAddressAsync(client, label: "住家", isDefault: true);
        var second = await CreateAddressAsync(client, label: "公司", isDefault: true);

        using var listResponse = await client.GetAsync("/api/v1/members/me/addresses");
        var addresses = (await listResponse.Content.ReadFromJsonAsync<List<MemberAddressResponse>>())!;

        Assert.False(addresses.Single(a => a.PublicId == first.PublicId).IsDefault);
        Assert.True(addresses.Single(a => a.PublicId == second.PublicId).IsDefault);
    }

    [Fact]
    public async Task Addresses_UpdateForAnotherMembersAddress_ReturnsNotFound()
    {
        // Actor A/B 越權拒絕：Member B 不得更新 Member A 的地址。
        var (clientA, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        using var _disposeA = clientA;
        var (clientB, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        using var _disposeB = clientB;
        var addressA = await CreateAddressAsync(clientA, label: "A的地址", isDefault: false);

        using var request = new HttpRequestMessage(
            HttpMethod.Put, $"/api/v1/members/me/addresses/{addressA.PublicId:D}")
        {
            Content = JsonContent.Create(new
            {
                label = "被 B 竄改",
                recipientName = "B",
                phone = "0911111111",
                postalCode = "100",
                city = "台北市",
                district = "中正區",
                addressLine1 = "被竄改路 1 號",
                addressLine2 = (string?)null,
                isDefault = false,
                rowVersion = addressA.RowVersion,
            }),
        };
        using var response = await MembersApiFixture.SendWithAntiforgeryAsync(clientB, request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // 零副作用：A 的地址內容應該完全沒被動到。
        using var reread = await clientA.GetAsync("/api/v1/members/me/addresses");
        var addresses = await reread.Content.ReadFromJsonAsync<List<MemberAddressResponse>>();
        Assert.Equal("A的地址", addresses!.Single().Label);
    }

    [Fact]
    public async Task Addresses_DeleteThenDeleteAgain_IsIdempotentAndRemovesFromList()
    {
        var (client, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        using var _disposeClient = client;
        var created = await CreateAddressAsync(client, label: "要刪除", isDefault: false);

        using var firstDelete = await MembersApiFixture.SendWithAntiforgeryAsync(
            client, new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/members/me/addresses/{created.PublicId:D}"));
        using var secondDelete = await MembersApiFixture.SendWithAntiforgeryAsync(
            client, new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/members/me/addresses/{created.PublicId:D}"));

        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondDelete.StatusCode);

        using var listResponse = await client.GetAsync("/api/v1/members/me/addresses");
        var addresses = await listResponse.Content.ReadFromJsonAsync<List<MemberAddressResponse>>();
        Assert.Empty(addresses!);
    }

    private static async Task<MemberAddressResponse> CreateAddressAsync(
        HttpClient client, string label, bool isDefault)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/members/me/addresses")
        {
            Content = JsonContent.Create(new
            {
                label,
                recipientName = "測試收件人",
                phone = "0912345678",
                postalCode = "100",
                city = "台北市",
                district = "中正區",
                addressLine1 = "測試路 1 號",
                addressLine2 = (string?)null,
                isDefault,
            }),
        };
        using var response = await MembersApiFixture.SendWithAntiforgeryAsync(client, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MemberAddressResponse>())!;
    }
}
