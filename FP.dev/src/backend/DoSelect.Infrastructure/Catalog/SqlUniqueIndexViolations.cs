using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Catalog;

/// <summary>
/// 組長 PR #24 round 10 review, P2: Product/SKU code uniqueness is pre-checked with a plain
/// SELECT (AnyAsync) before the INSERT, same as Identity's own email uniqueness check in
/// MemberRegistrationGateway — two concurrent creates for the same brand-new code can both pass
/// that check, and only one INSERT actually wins; the loser hits the real unique index and must
/// still come back as the documented 409 duplicate error, not an opaque 500. Mirrors
/// MemberRegistrationGateway.IsUniqueIndexViolation's exception-chain walk, but additionally
/// requires the *specific* index name to appear in the SQL error message — a bare "was this a
/// 2601/2627" check would map every unrelated unique-index violation (a genuinely different
/// constraint, not the one this call site is guarding) onto the wrong duplicate error code.
/// </summary>
internal static class SqlUniqueIndexViolations
{
    private const int DuplicateKeyOnUniqueIndex = 2601;
    private const int DuplicateKeyOnPrimaryOrUniqueConstraint = 2627;

    public static bool Matches(DbUpdateException exception, string indexName)
    {
        for (var current = (Exception)exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlException &&
                sqlException.Errors.Cast<SqlError>().Any(error =>
                    error.Number is DuplicateKeyOnUniqueIndex or DuplicateKeyOnPrimaryOrUniqueConstraint &&
                    error.Message.Contains(indexName, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}
