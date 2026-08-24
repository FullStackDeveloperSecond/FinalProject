using DoSelect.Application.Members;
using DoSelect.Application.Notifications;
using DoSelect.Domain.Members;
using DoSelect.Infrastructure.Persistence;
using DoSelect.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DoSelect.Infrastructure.Members;

/// <summary>
/// 管理員發起的密碼重設：只寄送重設連結 Email，不由管理員直接設定明文密碼。
/// ⚠ 跨模組假設：連結指向 customer-web 的 <c>/reset-password</c> 頁面，
/// 該頁面本身不在這次的範圍內建置（M-01 會員自助密碼重設尚未開工）。
/// </summary>
public sealed class AdminMemberPasswordResetInitiator : IAdminMemberPasswordResetInitiator
{
    private const string DefaultMemberBaseUrl = "http://localhost:5173";

    private readonly DoSelectDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;

    public AdminMemberPasswordResetInitiator(
        DoSelectDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(emailSender);
        ArgumentNullException.ThrowIfNull(configuration);

        _dbContext = dbContext;
        _userManager = userManager;
        _emailSender = emailSender;
        _configuration = configuration;
    }

    public async Task<bool> SendResetPasswordEmailAsync(
        Guid publicId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(
            u => u.PublicId == publicId && u.AccountType == AccountType.Member, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return false;
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var baseUrl = _configuration["Frontend:MemberBaseUrl"] ?? DefaultMemberBaseUrl;
        var resetLink =
            $"{baseUrl.TrimEnd('/')}/reset-password" +
            $"?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}";

        var message = new EmailMessage(
            user.Email,
            "重設您的 DoSelect 密碼",
            $"管理員已為您的帳號發起密碼重設。請於 1 小時內點擊以下連結重設密碼：\n{resetLink}\n\n若非您本人操作，請忽略此信。");

        var result = await _emailSender.SendAsync(message, cancellationToken);
        return result.WasDelivered;
    }
}
