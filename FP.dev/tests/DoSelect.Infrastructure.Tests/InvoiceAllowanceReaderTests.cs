using DoSelect.Application.Invoicing;
using DoSelect.Domain.Invoicing;
using DoSelect.Domain.Refunds;
using DoSelect.Infrastructure.Invoicing;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoSelect.Infrastructure.Tests;

public sealed class InvoiceAllowanceReaderTests
{
    private const string SyntheticConnectionString =
        "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;" +
        "TrustServerCertificate=True;";

    [Fact]
    public void AddDoSelectInvoicing_ResolvesTheAllowanceServiceAndItsReader()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<IInvoiceAllowanceReader>();
        var service = scope.ServiceProvider.GetRequiredService<IssueInvoiceAllowanceService>();

        Assert.IsType<InvoiceAllowanceReader>(reader);
        Assert.NotNull(service);
    }

    [Fact]
    public async Task FindByRefundAsync_ReturnsNullForAnEmptyPublicIdWithoutQuerying()
    {
        await using var context = CreateContext();
        var reader = new InvoiceAllowanceReader(context);

        Assert.Null(await reader.FindByRefundAsync(Guid.Empty));
    }

    [Fact]
    public async Task NextAllowanceSequenceAsync_RejectsNonUtcInput()
    {
        await using var context = CreateContext();
        var reader = new InvoiceAllowanceReader(context);

        await Assert.ThrowsAsync<ArgumentException>(() => reader.NextAllowanceSequenceAsync(
            new DateTime(2026, 8, 19, 2, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void ReaderQueriesAreCoveredByTheDocumentedIndexes()
    {
        using var context = CreateContext();

        var allowance = context.Model.FindEntityType(typeof(SimulatedInvoiceAllowance))!;
        var invoice = context.Model.FindEntityType(typeof(SimulatedInvoice))!;
        var allocation = context.Model.FindEntityType(typeof(RefundAllocation))!;

        Assert.Contains(
            allowance.GetIndexes(),
            index => index.Properties.Any(p => p.Name == nameof(SimulatedInvoiceAllowance.RefundId)));
        Assert.Contains(
            invoice.GetIndexes(),
            index => index.Properties.Any(p => p.Name == nameof(SimulatedInvoice.OrderId)));
        Assert.Contains(
            allocation.GetIndexes(),
            index => index.Properties.Any(p => p.Name == nameof(RefundAllocation.RefundId)));
    }

    [Fact]
    public void TheReaderOnlyTouchesTablesThisModuleOwns()
    {
        // OrderItemId 只當對應鍵使用，不查 OrderItems，符合工程包「不得讀取其他模組底層表」。
        var source = File.ReadAllText(ReaderSourcePath());

        Assert.Contains("_context.SimulatedInvoices", source, StringComparison.Ordinal);
        Assert.Contains("_context.RefundAllocations", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.Orders", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.OrderItems", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.ReturnItems", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.Skus", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProvisionalQuantityRuleIsDocumentedAtItsSource()
    {
        // RefundAllocations 沒有數量欄位，折讓數量目前以金額比例推導。
        // 這是待 alex 裁定的暫行規則，必須留在原始碼上讓接手的人看得到。
        var source = File.ReadAllText(ReaderSourcePath());

        Assert.Contains("RefundAllocations.Quantity", source, StringComparison.Ordinal);
        Assert.Contains("暫行推導規則", source, StringComparison.Ordinal);
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
            "src", "backend", "DoSelect.Infrastructure", "Invoicing", "InvoiceAllowanceReader.cs");
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
            .AddDoSelectInvoicing()
            .BuildServiceProvider();
    }

    private static DoSelectDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(SyntheticConnectionString)
            .Options);
}
