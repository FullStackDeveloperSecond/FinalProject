using System.Net;
using System.Text.Json;
using DoSelect.Api.Common;
using DoSelect.Api.Observability;
using DoSelect.Application.Notifications;
using DoSelect.Infrastructure.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DoSelect.Api.IntegrationTests;

public sealed class ObservabilityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ObservabilityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetLive_WhenApplicationIsRunning_ReturnsSafeHealthyResponse()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");
        using var document = await ReadHealthResponseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Single(document.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task GetReady_WhenStorageIsWritable_ReturnsSafeHealthyResponse()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        using var document = await ReadHealthResponseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Single(document.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task GetReady_WhenStoragePathIsAFile_ReturnsSafeUnhealthyResponse()
    {
        var filePath = Path.GetTempFileName();

        try
        {
            using var factory = CreateFactory(new Dictionary<string, string?>
            {
                ["Storage:DataRoot"] = filePath,
            });
            using var client = factory.CreateClient();

            using var response = await client.GetAsync("/health/ready");
            var responseBody = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseBody);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
            Assert.Single(document.RootElement.EnumerateObject());
            Assert.DoesNotContain(filePath, responseBody, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task GetReady_WhenDatabaseReadFails_ReturnsSafeUnhealthyResponse()
    {
        using var factory = CreateFactory(databaseReady: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var responseBody = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseBody);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Single(document.RootElement.EnumerateObject());
        Assert.DoesNotContain("database", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Start_WhenOptionalFeaturesAreDisabled_DoesNotRequireSecrets()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Features:AiEnabled"] = "false",
            ["Features:EmailEnabled"] = "false",
            ["OpenAI:ApiKey"] = null,
            ["Email:Password"] = null,
        });

        using var client = factory.CreateClient();
    }

    [Fact]
    public async Task EmailSender_WhenEmailFeatureIsDisabled_IsExplicitlySuppressed()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Features:EmailEnabled"] = "false",
        });
        using var client = factory.CreateClient();
        var sender = factory.Services.GetRequiredService<IEmailSender>();

        var result = await sender.SendAsync(new EmailMessage(
            "member@example.test",
            "Synthetic subject",
            "Synthetic body"));

        Assert.Equal(EmailDeliveryStatus.Suppressed, result.Status);
        Assert.Equal(EmailDeliveryErrorCodes.Suppressed, result.ErrorCode);
    }

    [Fact]
    public void EmailSender_WhenEmailFeatureIsEnabled_RegistersSmtpAdapterWithoutSending()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Features:EmailEnabled"] = "true",
            ["Email:SmtpHost"] = "smtp.example.test",
            ["Email:SmtpPort"] = "587",
            ["Email:UserName"] = "smtp-user",
            ["Email:Password"] = "synthetic-test-password",
            ["Email:SenderName"] = "DoSelect Test",
            ["Email:SenderAddress"] = "sender@example.test",
            ["Email:TimeoutMilliseconds"] = "15000",
        });
        using var client = factory.CreateClient();

        var sender = factory.Services.GetRequiredService<IEmailSender>();

        Assert.IsType<SmtpEmailSender>(sender);
    }

    [Fact]
    public void Start_WhenAiIsEnabledWithoutApiKey_FailsWithConfigurationKeyOnly()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Features:AiEnabled"] = "true",
            ["OpenAI:ApiKey"] = string.Empty,
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var message = exception.ToString();

        Assert.Contains("OpenAI:ApiKey", message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Start_WhenConfigurationValidationFails_DoesNotEchoConfiguredSecret()
    {
        const string secret = "observability-test-secret-must-not-leak";
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Features:AiEnabled"] = "true",
            ["OpenAI:ApiKey"] = secret,
            ["OpenAI:SupportModel"] = string.Empty,
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var message = exception.ToString();

        Assert.Contains("OpenAI:SupportModel", message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_WhenAiSupportTimeoutIsOutsideAllowedRange_FailsWithConfigurationKey()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Features:AiEnabled"] = "true",
            ["OpenAI:ApiKey"] = "synthetic-openai-key",
            ["OpenAI:SupportTimeoutMilliseconds"] = "999",
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "OpenAI:SupportTimeoutMilliseconds",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Start_WhenGuestOrderAccessRateLimitIsNotPositive_FailsWithConfigurationKey()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["GuestOrderAccess:Pepper"] = "integration-test-guest-order-pepper-32-bytes",
            ["RateLimiting:GuestOrderAccessWindowMinutes"] = "0",
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "RateLimiting:GuestOrderAccessWindowMinutes",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Start_WhenEmailIsEnabledWithoutCredentials_FailsWithConfigurationKeysOnly()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Features:EmailEnabled"] = "true",
            ["Email:SmtpHost"] = string.Empty,
            ["Email:UserName"] = string.Empty,
            ["Email:Password"] = string.Empty,
            ["Email:SenderAddress"] = string.Empty,
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var message = exception.ToString();

        Assert.Contains("Email:SmtpHost", message, StringComparison.Ordinal);
        Assert.Contains("Email:UserName", message, StringComparison.Ordinal);
        Assert.Contains("Email:Password", message, StringComparison.Ordinal);
        Assert.Contains("Email:SenderAddress", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_WhenStoragePathIsRelative_FailsWithConfigurationKeyOnly()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Storage:DataRoot"] = "relative-data",
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var message = exception.ToString();

        Assert.Contains("Storage:DataRoot", message, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath(), message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Start_WhenSimulationEndpointsAreEnabledOutsideDemo_FailsFast()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Demo:SimulationEndpointsEnabled"] = "true",
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "Demo:SimulationEndpointsEnabled",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Start_WhenCheckoutPolicyVersionIsNotPositive_FailsFast()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["CheckoutPolicy:ShippingConstraintVersion"] = "0",
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "CheckoutPolicy:ShippingConstraintVersion",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestLog_WhenRequestCompletes_ContainsRequiredStructuredProperties()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            "DoSelectTests",
            Guid.NewGuid().ToString("N"));
        const string correlationId = "observability-log-test";

        try
        {
            var factory = CreateFactory(new Dictionary<string, string?>
            {
                ["Storage:DataRoot"] = dataRoot,
                ["Observability:FileLoggingEnabled"] = "true",
            });
            var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
            request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            client.Dispose();
            await factory.DisposeAsync();

            var logFile = Assert.Single(
                Directory.GetFiles(Path.Combine(dataRoot, "logs"), "doselect-api-*.json"));
            var logLines = ReadSharedLines(logFile);
            var matchingLog = logLines
                .Select(line => JsonDocument.Parse(line))
                .FirstOrDefault(document =>
                    document.RootElement.TryGetProperty("Properties", out var properties) &&
                    properties.TryGetProperty("CorrelationId", out var property) &&
                    property.GetString() == correlationId &&
                    properties.TryGetProperty("SourceContext", out var sourceContext) &&
                    sourceContext.GetString() ==
                        "Serilog.AspNetCore.RequestLoggingMiddleware");

            Assert.True(
                matchingLog is not null,
                $"No matching request log was found.{Environment.NewLine}{string.Join(Environment.NewLine, logLines)}");
            using (matchingLog)
            {
                var properties = matchingLog.RootElement.GetProperty("Properties");
                Assert.Equal("/health/live", properties.GetProperty("PathTemplate").GetString());
                Assert.Equal("Anonymous", properties.GetProperty("UserType").GetString());
                Assert.False(
                    string.IsNullOrWhiteSpace(properties.GetProperty("TraceId").GetString()));
                Assert.Equal(200, properties.GetProperty("StatusCode").GetInt32());
                Assert.Equal("GET", properties.GetProperty("RequestMethod").GetString());
            }
        }
        finally
        {
            await TryDeleteTestDirectoryAsync(dataRoot);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(
        IReadOnlyDictionary<string, string?>? settings = null,
        bool databaseReady = true)
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            "DoSelectTests",
            Guid.NewGuid().ToString("N"));
        var defaults = new Dictionary<string, string?>
        {
            ["Storage:DataRoot"] = dataRoot,
            ["Observability:FileLoggingEnabled"] = "false",
            ["Features:AiEnabled"] = "false",
            ["Features:EmailEnabled"] = "false",
            ["Demo:SimulationEndpointsEnabled"] = "false",
        };

        if (settings is not null)
        {
            foreach (var setting in settings)
            {
                defaults[setting.Key] = setting.Value;
            }
        }

        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(defaults));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseReadinessProbe>();
                services.AddSingleton<IDatabaseReadinessProbe>(
                    new StubDatabaseReadinessProbe(databaseReady));
            });
        });
    }

    private static async Task<JsonDocument> ReadHealthResponseAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    private static async Task TryDeleteTestDirectoryAsync(string directoryPath)
    {
        for (var attempt = 0; attempt < 5 && Directory.Exists(directoryPath); attempt++)
        {
            try
            {
                Directory.Delete(directoryPath, recursive: true);
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
            catch (IOException)
            {
                return;
            }
        }
    }

    private static IReadOnlyList<string> ReadSharedLines(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();

        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private sealed class StubDatabaseReadinessProbe(bool isReady)
        : IDatabaseReadinessProbe
    {
        public Task<bool> CanReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(isReady);
    }
}
