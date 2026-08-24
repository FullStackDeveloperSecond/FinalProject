using DoSelect.Application.Members;
using DoSelect.Domain.Members;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoSelect.Infrastructure.Persistence.Identity;

public sealed class MemberPasswordResetGateway(
    UserManager<ApplicationUser> userManager) : IMemberPasswordResetGateway
{
    public async Task<RequestMemberPasswordResetOutcome> RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !IsEligibleForPasswordReset(user))
        {
            return new RequestMemberPasswordResetOutcome.NotEligible();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return new RequestMemberPasswordResetOutcome.Issued(user.PublicId, user.Email!, token);
    }

    public async Task<ResetMemberPasswordOutcome> ResetPasswordAsync(
        Guid userPublicId,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users.SingleOrDefaultAsync(
            candidate => candidate.PublicId == userPublicId,
            cancellationToken);

        // Re-check eligibility at consume time, not just at issue time: a token generated while
        // the account was eligible must not still work after the account is suspended,
        // anonymized, or disabled in the interim.
        if (user is null || !IsEligibleForPasswordReset(user))
        {
            return new ResetMemberPasswordOutcome.TokenRejected();
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded)
        {
            return new ResetMemberPasswordOutcome.Success();
        }

        if (result.Errors.Any(error => error.Code.Contains("InvalidToken", StringComparison.Ordinal)))
        {
            return new ResetMemberPasswordOutcome.TokenRejected();
        }

        return new ResetMemberPasswordOutcome.PasswordRejected(
            result.Errors.Select(error => error.Description).ToArray());
    }

    private static bool IsEligibleForPasswordReset(ApplicationUser user) =>
        user.AccountType == AccountType.Member &&
        user.AccountStatus is not (AccountStatus.Suspended or AccountStatus.Anonymized or AccountStatus.Disabled);
}
