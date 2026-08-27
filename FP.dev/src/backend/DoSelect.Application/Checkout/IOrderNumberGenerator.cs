namespace DoSelect.Application.Checkout;

/// <summary>
/// Produces the next Taiwan-business-date order number inside the caller-owned SQL transaction.
/// Implementations must serialize allocation and must not commit or replace that transaction.
/// </summary>
public interface IOrderNumberGenerator
{
    Task<string> NextAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
