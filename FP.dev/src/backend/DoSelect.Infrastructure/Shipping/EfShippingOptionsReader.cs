using DoSelect.Application.Shipping;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Shipping;

public sealed class EfShippingOptionsReader : IShippingOptionsReader
{
    private readonly DoSelectDbContext _context;

    public EfShippingOptionsReader(DoSelectDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<ShippingOptionsDto> GetActiveOptionsAsync(CancellationToken cancellationToken)
    {
        var methods = await _context.ShippingMethods.AsNoTracking()
            .Where(method => method.IsActive)
            .OrderBy(method => method.SortOrder)
            .ThenBy(method => method.Code)
            .Select(method => new ShippingMethodOptionDto(
                method.Code,
                method.NameZhTw,
                method.Kind,
                method.BaseFee,
                method.FreeShippingThreshold,
                method.AllowsCod,
                method.RequiresPrepayment))
            .ToListAsync(cancellationToken);

        return new ShippingOptionsDto(methods);
    }
}
