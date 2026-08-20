using DoSelect.Application.Refunds;
using DoSelect.Domain.Payments;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Tests;

public sealed class RefundExecutionReaderTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    [Fact]
    public void AddDoSelectRefunds_ResolvesTheExecuteServiceAndItsReader()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<IRefundExecutionReader>();
        var service = scope.ServiceProvider.GetRequiredService<ExecuteRefundService>();

        Assert.IsType<RefundExecutionReader>(reader);
        Assert.NotNull(service);
    }

    [Fact]
    public async Task FindAsync_ReturnsNullForAnEmptyPublicIdWithoutQuerying()
    {
        await using var context = CreateContext();
        var reader = new RefundExecutionReader(context);

        Assert.Null(await reader.FindAsync(Guid.Empty));
    }

    [Fact]
    public void ReaderQueriesAreCoveredByTheDocumentedIndexes()
    {
        using var context = CreateContext();

        var refund = context.Model.FindEntityType(typeof(Refund))!;
        var attempt = context.Model.FindEntityType(typeof(PaymentAttempt))!;

        Assert.Contains(
            refund.GetIndexes(),
            index => index.Properties.Any(property => property.Name == nameof(Refund.PublicId)));
        Assert.Contains(
            refund.GetIndexes(),
            index => index.Properties.Any(property => property.Name == nameof(Refund.OrderId)));
        Assert.Contains(
            attempt.GetIndexes(),
            index => index.GetDatabaseName() == "IX_PaymentAttempts_OrderId_CreatedAtUtc");
    }

    [Fact]
    public void TheReaderOnlyTouchesTablesThisModuleOwns()
    {
        // 可退款餘額由本模組的 Refunds 與 PaymentAttempts 推導，
        // 不查 Orders，符合工程包「不得讀取其他模組底層表」的約束。
        var source = File.ReadAllText(ReaderSourcePath());

        Assert.Contains("_context.Refunds", source, StringComparison.Ordinal);
        Assert.Contains("_context.PaymentAttempts", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.Orders", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.OrderItems", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.Skus", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.ShippingMethods", source, StringComparison.Ordinal);
    }

    private static string ReaderSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(
            directory!.FullName,
            "src", "backend", "DoSelect.Infrastructure", "Refunds", "RefundExecutionReader.cs");
    }

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = SyntheticConnectionString,
            })
            .Build();

        return new ServiceCollection()
            .AddDoSelectPersistence(configuration)
            .AddDoSelectRefunds()
            .BuildServiceProvider();
    }

    private static DoSelectDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString)
            .Options);
}
