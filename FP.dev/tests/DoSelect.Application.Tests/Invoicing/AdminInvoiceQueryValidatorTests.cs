using DoSelect.Application.Invoicing;

namespace DoSelect.Application.Tests.Invoicing;

public sealed class AdminInvoiceQueryValidatorTests
{
    [Fact]
    public void SupportedSortOptionsAreAccepted()
    {
        var query = new AdminInvoiceQuery(
            Statuses: null,
            FromUtc: null,
            ToUtc: null,
            Q: null,
            PageNumber: 1,
            PageSize: 20,
            Sort: AdminInvoiceSortOptions.OrderNumberDesc);

        AdminInvoiceQueryValidator.RequireValid(query);
    }

    [Fact]
    public void AnUnknownSortOptionIsRejected()
    {
        var query = new AdminInvoiceQuery(
            Statuses: null,
            FromUtc: null,
            ToUtc: null,
            Q: null,
            PageNumber: 1,
            PageSize: 20,
            Sort: "not-a-sort");

        Assert.ThrowsAny<Exception>(() => AdminInvoiceQueryValidator.RequireValid(query));
    }
}
