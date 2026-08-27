using DoSelect.Domain.Refunds;
using DoSelect.Domain.Reports;
using DoSelect.Domain.Returns;
using DoSelect.Domain.Support;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DoSelect.Infrastructure.Tests;

public sealed class KafenPersistenceModelTests
{
    private const string Connection = "Server=localhost;Database=DoSelectSynthetic;Trusted_Connection=True;TrustServerCertificate=True;";
    public static TheoryData<Type, string> Tables => new()
    {
        { typeof(SupportTicket), "SupportTickets" }, { typeof(SupportMessage), "SupportMessages" }, { typeof(SupportAttachment), "SupportAttachments" },
        { typeof(SupportAssignmentHistory), "SupportAssignmentHistories" }, { typeof(SupportStatusHistory), "SupportStatusHistories" }, { typeof(SupportSlaEvent), "SupportSlaEvents" }, { typeof(SupportSummary), "SupportSummaries" },
        { typeof(ReturnRequest), "ReturnRequests" }, { typeof(ReturnItem), "ReturnItems" }, { typeof(ReturnInspection), "ReturnInspections" }, { typeof(ReturnAttachment), "ReturnAttachments" },
        { typeof(ReturnAssignmentHistory), "ReturnAssignmentHistories" }, { typeof(ReturnStatusHistory), "ReturnStatusHistories" }, { typeof(ReturnShipment), "ReturnShipments" }, { typeof(ReturnShipmentEvent), "ReturnShipmentEvents" },
        { typeof(ReportCase), "ReportCases" }, { typeof(ReportAttachment), "ReportAttachments" }, { typeof(ReportAssignmentHistory), "ReportAssignmentHistories" }, { typeof(ReportStatusHistory), "ReportStatusHistories" },
    };

    [Theory]
    [MemberData(nameof(Tables))]
    public void Model_MapsEntityToExpectedTable(Type type, string table)
    { using var context = Create(); Assert.Equal(table, context.Model.FindEntityType(type)?.GetTableName()); }

    [Fact]
    public void CaseWorkbench_IsKeylessTwelveColumnView()
    {
        using var context = Create(); var entity = context.Model.FindEntityType(typeof(CaseWorkbenchRow))!;
        Assert.Null(entity.FindPrimaryKey()); Assert.Equal("vw_CaseWorkbench", entity.GetViewName()); Assert.Equal(12, entity.GetProperties().Count());
    }

    [Fact]
    public void ReportCase_UsesFilteredBinaryOpenCaseUniqueness()
    {
        using var context = Create(); var entity = context.Model.FindEntityType(typeof(ReportCase))!;
        Assert.Equal("binary(32)", entity.FindProperty(nameof(ReportCase.OpenCaseKeyHash))?.GetColumnType());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.GetDatabaseName() == "UX_ReportCases_OpenCaseKeyHash" && index.GetFilter() == "[OpenCaseKeyHash] IS NOT NULL");
    }

    [Fact]
    public void ReturnShipmentEvent_UsesConcurrentDuplicateBarrier()
    {
        using var context = Create(); var entity = context.Model.FindEntityType(typeof(ReturnShipmentEvent))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.GetDatabaseName() == "UX_ReturnShipmentEvents_Source_ExternalEventId");
    }

    [Fact]
    public void RefundNowHasRestrictReturnRequestForeignKey()
    {
        using var context = Create(); var entity = context.Model.FindEntityType(typeof(Refund))!;
        Assert.Contains(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(ReturnRequest) && fk.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public void ReturnRequestPriority_IsRequiredStringWithNormalDefault()
    {
        using var context = Create(); var property = context.Model.FindEntityType(typeof(ReturnRequest))!.FindProperty(nameof(ReturnRequest.Priority))!;

        Assert.False(property.IsNullable);
        Assert.Equal(16, property.GetMaxLength());
        Assert.Equal(CasePriority.Normal, property.GetDefaultValue());
    }

    [Fact]
    public void ReturnRequestRefundTrustedInputs_AreNullableAndUseSqlServerSafeTypes()
    {
        using var context = Create();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ReturnRequest))!;
        var disposition = entity.FindProperty(nameof(ReturnRequest.AssemblyFeeDisposition))!;
        var shippingCost = entity.FindProperty(nameof(ReturnRequest.ReturnShippingCost))!;

        Assert.True(disposition.IsNullable);
        Assert.Equal("varchar(32)", disposition.GetColumnType());
        Assert.True(shippingCost.IsNullable);
        Assert.Equal("decimal(18,2)", shippingCost.GetColumnType());

        var constraints = entity.GetCheckConstraints().Select(constraint => constraint.Name).ToHashSet();
        Assert.Contains("CK_ReturnRequests_RefundTrustedInputs", constraints);
        Assert.Contains("CK_ReturnRequests_ReturnShippingCost", constraints);
    }

    [Fact]
    public void KafenForeignKeys_AreAllRestrict()
    {
        using var context = Create(); var entities = Tables.Select(row => row[0]).Cast<Type>().Select(type => context.Model.FindEntityType(type)!);
        Assert.All(entities.SelectMany(entity => entity.GetForeignKeys()), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }

    private static DoSelectDbContext Create() => new(new DbContextOptionsBuilder<DoSelectDbContext>().UseSqlServer(Connection).Options);
}
