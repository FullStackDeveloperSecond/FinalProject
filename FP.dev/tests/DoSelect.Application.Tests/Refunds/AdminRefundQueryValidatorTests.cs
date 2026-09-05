using DoSelect.Application.Refunds;
using DoSelect.Domain.Refunds;

namespace DoSelect.Application.Tests.Refunds;

public sealed class AdminRefundQueryValidatorTests
{
    [Fact]
    public void AValidQueryIsAccepted()
    {
        var query = new AdminRefundQuery(
            [RefundStatus.Approved],
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            "RF-202608",
            PageNumber: 1,
            PageSize: 20);

        AdminRefundQueryValidator.RequireValid(query);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void InvalidPaginationIsRejected(int pageNumber, int pageSize)
    {
        var query = new AdminRefundQuery(
            Statuses: null,
            FromUtc: null,
            ToUtc: null,
            Q: null,
            pageNumber,
            pageSize);

        Assert.ThrowsAny<Exception>(() => AdminRefundQueryValidator.RequireValid(query));
    }

    [Fact]
    public void AnInvertedDateRangeIsRejected()
    {
        var query = new AdminRefundQuery(
            Statuses: null,
            FromUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Q: null,
            PageNumber: 1,
            PageSize: 20);

        Assert.ThrowsAny<Exception>(() => AdminRefundQueryValidator.RequireValid(query));
    }
}
