using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DoSelect.Api.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DoSelect.Api.IntegrationTests;

public sealed class ApiFoundationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string CorrelationId = "api-foundation-test-001";
    private readonly WebApplicationFactory<Program> _factory;

    public ApiFoundationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSuccess_WhenCorrelationIdIsValid_EchoesCorrelationId()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__tests/api-foundation/success");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, CorrelationId);

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal(
            CorrelationId,
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task PostValidation_WhenBodyIsInvalid_ReturnsValidationProblemDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/__tests/api-foundation/validation")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, CorrelationId);

        using var response = await client.SendAsync(request);
        using var document = await ReadProblemDetailsAsync(response);
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ApiErrorCodes.ValidationFailed, root.GetProperty("code").GetString());
        Assert.Equal(CorrelationId, root.GetProperty("correlationId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.True(root.GetProperty("errors").TryGetProperty("name", out var nameErrors));
        Assert.NotEmpty(nameErrors.EnumerateArray());
    }

    [Fact]
    public async Task PostValidation_WhenJsonIsMalformed_ReturnsValidationProblemDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("{\"name\":", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/__tests/api-foundation/validation", content);
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.ValidationFailed,
            document.RootElement.GetProperty("code").GetString());
        Assert.True(document.RootElement.GetProperty("errors").EnumerateObject().Any());
    }

    [Fact]
    public async Task GetException_WhenUnhandled_ReturnsSafeProblemDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/__tests/api-foundation/exception");
        var responseBody = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ApiErrorCodes.UnexpectedError, root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(
            ApiFoundationTestConstants.SensitiveExceptionMessage,
            responseBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("stack", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUnknownRoute_ReturnsNotFoundProblemDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/__tests/route-does-not-exist");
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.ResourceNotFound,
            document.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(401, ApiErrorCodes.AuthenticationRequired)]
    [InlineData(403, ApiErrorCodes.AuthorizationForbidden)]
    [InlineData(409, ApiErrorCodes.RequestConflict)]
    [InlineData(503, ApiErrorCodes.ServiceUnavailable)]
    public async Task GetStatus_WhenResponseHasNoBody_ReturnsCodedProblemDetails(
        int statusCode,
        string expectedCode)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/__tests/api-foundation/status/{statusCode}");
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(statusCode, (int)response.StatusCode);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PutValidation_WhenMethodIsNotAllowed_ReturnsMethodProblemDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            "/__tests/api-foundation/validation",
            new { name = "valid" });
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.RequestMethodNotAllowed,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostValidation_WhenContentTypeIsUnsupported_ReturnsContentTypeProblemDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("{}", Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync("/__tests/api-foundation/validation", content);
        using var document = await ReadProblemDetailsAsync(response);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(
            ApiErrorCodes.RequestContentTypeUnsupported,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetUnknownRoute_WhenCorrelationIdIsInvalid_ReplacesCorrelationId()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__tests/route-does-not-exist");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, new string('a', 65));

        using var response = await client.SendAsync(request);
        using var document = await ReadProblemDetailsAsync(response);
        var responseCorrelationId = response.Headers
            .GetValues(CorrelationIdMiddleware.HeaderName)
            .Single();

        Assert.Equal(32, responseCorrelationId.Length);
        Assert.All(responseCorrelationId, character => Assert.True(char.IsAsciiHexDigit(character)));
        Assert.Equal(
            responseCorrelationId,
            document.RootElement.GetProperty("correlationId").GetString());
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
            });
            builder.ConfigureServices(services =>
            {
                services
                    .AddControllers()
                    .AddApplicationPart(typeof(ApiFoundationTestController).Assembly);
            });
        });
    }

    private static async Task<JsonDocument> ReadProblemDetailsAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

}

internal static class ApiFoundationTestConstants
{
    public const string SensitiveExceptionMessage = "sensitive-internal-exception-message";
}

[ApiController]
[Route("__tests/api-foundation")]
public sealed class ApiFoundationTestController : ControllerBase
{
    [HttpGet("success")]
    public IActionResult GetSuccess() => Ok(new { status = "ok" });

    [HttpPost("validation")]
    public IActionResult Validate(ApiFoundationValidationRequest request) => Ok(request);

    [HttpGet("exception")]
    public IActionResult GetException() => throw new InvalidOperationException(
        ApiFoundationTestConstants.SensitiveExceptionMessage);

    [HttpGet("status/{statusCode:int}")]
    public IActionResult GetStatus(int statusCode) => StatusCode(statusCode);
}

public sealed class ApiFoundationValidationRequest
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(20, MinimumLength = 2)]
    public string? Name { get; init; }
}
