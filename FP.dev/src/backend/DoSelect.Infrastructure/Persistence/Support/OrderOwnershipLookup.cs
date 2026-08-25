using DoSelect.Application.Support;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Support;

public sealed class OrderOwnershipLookup : IOrderOwnershipLookup
{
    private readonly DoSelectDbContext _dbContext;

    public OrderOwnershipLookup(DoSelectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<OrderOwnershipInfo?> FindByPublicIdAsync(Guid orderPublicId, CancellationToken cancellationToken) =>
        _dbContext.Orders
            .Where(o => o.PublicId == orderPublicId)
            .Select(o => new OrderOwnershipInfo(o.Id, o.PublicId, o.MemberUserId))
            .SingleOrDefaultAsync(cancellationToken);
}
