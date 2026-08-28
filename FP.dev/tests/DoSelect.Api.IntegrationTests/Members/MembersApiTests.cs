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
            client,
            new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/members/me/addresses/{created.PublicId:D}")
            {
                Content = JsonContent.Create(new { rowVersion = created.RowVersion }),
            });
        using var secondDelete = await MembersApiFixture.SendWithAntiforgeryAsync(
            client,
            new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/members/me/addresses/{created.PublicId:D}")
            {
                Content = JsonContent.Create(new { rowVersion = created.RowVersion }),
            });

        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondDelete.StatusCode);

        using var listResponse = await client.GetAsync("/api/v1/members/me/addresses");
        var addresses = await listResponse.Content.ReadFromJsonAsync<List<MemberAddressResponse>>();
        Assert.Empty(addresses!);
    }

    [Fact]
    public async Task Addresses_ConcurrentDefaultCreation_AtMostOneWinsAndNoRequestFails500()
    {
        // Alex review, 2026-08-28 (P2-5): 兩個併發請求各自把不同地址設成預設會撞
        // UX_MemberAddresses_MemberUserId_Default——輸家必須是可重試的 409，不是未處理的 500。
        var (client, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        using var _disposeClient = client;

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async i =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/members/me/addresses")
            {
                Content = JsonContent.Create(new
                {
                    label = $"併發地址{i}",
                    recipientName = "測試收件人",
                    phone = "0912345678",
                    postalCode = "100",
                    city = "台北市",
                    district = "中正區",
                    addressLine1 = "測試路 1 號",
                    addressLine2 = (string?)null,
                    isDefault = true,
                }),
            };
            return await MembersApiFixture.SendWithAntiforgeryAsync(client, request);
        }));

        var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
        Assert.All(responses.Zip(bodies), pair => Assert.True(
            pair.First.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Unexpected status {pair.First.StatusCode}: {pair.Second}"));
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);

        using var listResponse = await client.GetAsync("/api/v1/members/me/addresses");
        var addresses = (await listResponse.Content.ReadFromJsonAsync<List<MemberAddressResponse>>())!;
        Assert.Single(addresses, address => address.IsDefault);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Addresses_DeleteWithStaleRowVersion_ReturnsConcurrencyConflict()
    {
        var (client, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        using var _disposeClient = client;
        var created = await CreateAddressAsync(client, label: "會被改過", isDefault: false);

        // 模擬「刪除前有另一個請求先更新了這筆地址」——直接透過 Update 端點推進 RowVersion，
        // 讓 created.RowVersion 變成過期值。
        using var updateRequest = new HttpRequestMessage(
            HttpMethod.Put, $"/api/v1/members/me/addresses/{created.PublicId:D}")
        {
            Content = JsonContent.Create(new
            {
                label = "已被更新",
                recipientName = "測試收件人",
                phone = "0912345678",
                postalCode = "100",
                city = "台北市",
                district = "中正區",
                addressLine1 = "測試路 1 號",
                addressLine2 = (string?)null,
                isDefault = false,
                rowVersion = created.RowVersion,
            }),
        };
        using var updateResponse = await MembersApiFixture.SendWithAntiforgeryAsync(client, updateRequest);
        updateResponse.EnsureSuccessStatusCode();

        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/v1/members/me/addresses/{created.PublicId:D}")
        {
            Content = JsonContent.Create(new { rowVersion = created.RowVersion }),
        };
        using var deleteResponse = await MembersApiFixture.SendWithAntiforgeryAsync(client, deleteRequest);

        var (status, code, _) = await MembersApiFixture.ReadProblemAsync(deleteResponse);
        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", code);
    }

    [Fact]
    public async Task UpdateProfile_WhenPhoneWasChangedByAnotherFlow_ReturnsConcurrencyConflictInsteadOfOverwriting()
    {
        // Alex review, 2026-08-28: rowVersion 必須涵蓋整個聚合（MemberProfile + ApplicationUser
        // 的 Phone／Locale），不能只保護 MemberProfile 自己的欄位。這裡先讀一次拿到「舊」
        // rowVersion，接著透過同一個 Update 端點模擬「另一個流程」已經改過 Phone，
        // 最後用最早那個 rowVersion 送出更新，必須被拒絕，不能把 Phone 蓋回去。
        var (client, _) = await fixture.CreateAuthenticatedMemberClientAsync();
        using var _disposeClient = client;
        var original = (await (await client.GetAsync("/api/v1/members/me"))
            .Content.ReadFromJsonAsync<MemberProfileResponse>())!;

        using var firstUpdate = new HttpRequestMessage(HttpMethod.Put, "/api/v1/members/me")
        {
            Content = JsonContent.Create(new
            {
                displayName = original.DisplayName,
                phone = "0900000001",
                locale = "zh-TW",
                rowVersion = original.RowVersion,
            }),
        };
        using var firstResponse = await MembersApiFixture.SendWithAntiforgeryAsync(client, firstUpdate);
        firstResponse.EnsureSuccessStatusCode();

        using var staleUpdate = new HttpRequestMessage(HttpMethod.Put, "/api/v1/members/me")
        {
            Content = JsonContent.Create(new
            {
                displayName = "被舊畫面覆寫",
                phone = "0900000002",
                locale = "zh-TW",
                rowVersion = original.RowVersion,
            }),
        };
        using var staleResponse = await MembersApiFixture.SendWithAntiforgeryAsync(client, staleUpdate);

        var (status, code, _) = await MembersApiFixture.ReadProblemAsync(staleResponse);
        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", code);

        using var reread = await client.GetAsync("/api/v1/members/me");
        var current = await reread.Content.ReadFromJsonAsync<MemberProfileResponse>();
        Assert.Equal("0900000001", current!.Phone);
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
