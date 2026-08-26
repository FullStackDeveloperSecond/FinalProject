using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using DoSelect.Api.Security;
using DoSelect.Application.Files;
using DoSelect.Application.Returns;
using DoSelect.Infrastructure.Persistence.Returns;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests.Returns;

public sealed class GuestReturnAttachmentHttpTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public GuestReturnAttachmentHttpTests(WebApplicationFactory<Program> baseFactory) => _baseFactory = baseFactory;

    [Fact]
    public async Task UploadAttachment_WithValidGuestCookie_PassesGuestOrderActorToReturnService()
    {
        var returnService = DispatchProxy.Create<IReturnService, ReturnServiceFake>();
        var serviceFake = (ReturnServiceFake)(object)returnService;
        using var factory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IReturnService>();
            services.AddSingleton(returnService);
            services.RemoveAll<IGuestOrderAccessValidator>();
            services.AddSingleton<IGuestOrderAccessValidator>(new GuestOrderAccessValidatorFake(guestOrderId: 42));
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{GuestOrderAccessValidator.GuestOrderAccessCookieName}=valid-guest-token");
        using var form = new MultipartFormDataContent();
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/security/antiforgery-token");
        tokenRequest.Headers.Add(SecurityController.ClientHeaderName, "member");
        using var tokenResponse = await client.SendAsync(tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var antiforgeryToken = tokenDocument.RootElement.GetProperty("requestToken").GetString()!;

        using var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "guest-proof.pdf");

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/returns/{Guid.NewGuid()}/attachments") { Content = form };
        uploadRequest.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        using var response = await client.SendAsync(uploadRequest);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 but received {(int)response.StatusCode}: {responseBody}");
        Assert.NotNull(serviceFake.Actor);
        Assert.Null(serviceFake.Actor.MemberUserId);
        Assert.Equal(42, serviceFake.Actor.GuestOrderId);
        Assert.Equal("guest-proof.pdf", serviceFake.FileName);
    }

    private sealed class GuestOrderAccessValidatorFake(long guestOrderId) : IGuestOrderAccessValidator
    {
        public Task<long?> ValidateAsync(
            string rawToken,
            long requestedOrderId,
            DateTime nowUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult<long?>(rawToken == "valid-guest-token" && requestedOrderId == guestOrderId
                ? guestOrderId
                : null);

        public Task<long?> ResolveOrderIdAsync(
            string rawToken,
            DateTime nowUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult<long?>(rawToken == "valid-guest-token" ? guestOrderId : null);
    }

    private class ReturnServiceFake : DispatchProxy
    {
        public ReturnActor? Actor { get; private set; }
        public string? FileName { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IReturnService.UploadAttachmentAsync))
            {
                Actor = (ReturnActor)args![0]!;
                FileName = ((PrivateFileUpload)args[2]!).OriginalFileName;
                return Task.FromResult(new ReturnAttachmentDto(Guid.NewGuid(), FileName, DateTime.UtcNow));
            }

            throw new NotSupportedException($"Unexpected return-service call: {targetMethod?.Name}");
        }
    }
}
