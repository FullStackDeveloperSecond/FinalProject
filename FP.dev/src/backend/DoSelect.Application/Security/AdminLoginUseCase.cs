using DoSelect.Domain.Members;

namespace DoSelect.Application.Security;

/// <summary>
/// 管理員登入的第一階段（帳密）結果：失敗、成功但需綁定 TOTP、或成功且需輸入 TOTP。
/// 成功不代表建立完整管理 Session——完整 Session 只在 2FA 完成後才簽發。
/// </summary>
public sealed class AdminLoginResult
{
    private AdminLoginResult(
        string? errorCode,
        bool requiresEnrollment,
        bool requiresTwoFactor,
        string? userId,
        Guid? publicId,
        Guid? lockoutAuditResourcePublicId)
    {
        ErrorCode = errorCode;
        RequiresEnrollment = requiresEnrollment;
        RequiresTwoFactor = requiresTwoFactor;
        UserId = userId;
        PublicId = publicId;
        LockoutAuditResourcePublicId = lockoutAuditResourcePublicId;
    }

    public bool IsSuccess => ErrorCode is null;

    public string? ErrorCode { get; }

    public bool RequiresEnrollment { get; }

    public bool RequiresTwoFactor { get; }

    public string? UserId { get; }

    public Guid? PublicId { get; }

    /// <summary>
    /// ⚠ alex review：帳號鎖定的公開回應永遠是 <see cref="AdminAuthErrorCodes.InvalidCredentials"/>
    /// （避免帳號枚舉），真正的鎖定狀態只在這次呼叫「剛好觸發」鎖定時透過這個欄位往上帶，讓
    /// Controller 能寫一筆中央 Audit（見 AdminAuthController.Login）。已經處於鎖定狀態的後續嘗試
    /// 不會再帶這個欄位——那次鎖定在第一次觸發時就已經有稽核紀錄了。
    /// 只帶 PublicId（被鎖定帳號當 Audit 的 Resource），不帶整個使用者快照——匿名密碼嘗試
    /// 造成的鎖定，Actor 必須是 System，不能把被鎖定的管理員自己當成施暴的 Actor（見
    /// AdminAuthController.RecordSystemAdminAudit；alex review 最新一輪 P1#2）。
    /// </summary>
    public Guid? LockoutAuditResourcePublicId { get; }

    public static AdminLoginResult Failure(string errorCode) => new(errorCode, false, false, null, null, null);

    public static AdminLoginResult FailureWithLockoutAudit(AdminAuthUserSnapshot user) =>
        new(AdminAuthErrorCodes.InvalidCredentials, false, false, user.UserId, user.PublicId, user.PublicId);

    public static AdminLoginResult NeedsEnrollment(string userId, Guid publicId) =>
        new(null, true, false, userId, publicId, null);

    public static AdminLoginResult NeedsTwoFactor(string userId, Guid publicId) =>
        new(null, false, true, userId, publicId, null);
}

/// <summary>
/// 管理員帳密登入：驗證密碼、判斷鎖定與停用、依是否已綁定 TOTP 決定下一步。
/// 呼叫端（Api 層）負責簽發 AdminChallenge Cookie；本類別只做純決策，不碰 Cookie／DbContext。
/// </summary>
public sealed class AdminLoginUseCase
{
    // DEC-P269：Member／Admin 依 AccountType 各自 15／30 分鐘，不依賴單一全域
    // IdentityOptions.Lockout.DefaultLockoutTimeSpan——見 IAdminAuthGateway.RegisterFailedAttemptAsync，
    // 該方法在同一交易內把這個時長原子寫入，取代 Identity 內建、無法設定 AccountType 差異的全域鎖定。
    // 「幾次失敗才鎖」則沿用 Member／Admin 共用的全域 MaxFailedAccessAttempts（見
    // PersistenceServiceCollectionExtensions），不在這裡重複維護一份門檻常數。
    private static readonly TimeSpan AdminLockoutDuration = TimeSpan.FromMinutes(30);

    private readonly IAdminAuthGateway _gateway;
    private readonly TimeProvider _timeProvider;

    public AdminLoginUseCase(IAdminAuthGateway gateway, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _gateway = gateway;
        _timeProvider = timeProvider;
    }

    public async Task<AdminLoginResult> ExecuteAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return AdminLoginResult.Failure(AdminAuthErrorCodes.InvalidCredentials);
        }

        // ⚠ alex review：帳號狀態列舉——未知帳號、密碼錯誤、已鎖定帳號在密碼驗證完成前必須
        // 回同一種公開結果（invalid_credentials），停權狀態則要等密碼驗證通過後才判斷（見下方）。
        // 未知帳號與已鎖定帳號都會略過真正的密碼雜湊驗證，所以各自補一次假驗證，讓回應延遲
        // 不會變成「這個帳號存不存在／是否被鎖定」的旁路訊號（跟 MemberLoginGateway 同一套手法）。
        var user = await _gateway.FindAdminByEmailAsync(email.Trim(), cancellationToken);
        if (user is null)
        {
            await _gateway.PerformDummyPasswordVerificationAsync(password, cancellationToken);
            return AdminLoginResult.Failure(AdminAuthErrorCodes.InvalidCredentials);
        }

        // ⚠ alex review：已鎖定帳號原本回 account_locked，跟「密碼錯誤」的 invalid_credentials
        // 可被攻擊者區分——對任意 Email 連續送錯密碼，存在的管理員帳號最後會變成
        // account_locked，不存在帳號仍是 invalid_credentials，因此仍能枚舉帳號。真正的鎖定
        // 狀態只留在中央 Audit（見 RegisterFailedAttemptAsync 觸發鎖定當下那一次），不透過這裡
        // 的公開回應揭露。
        var lockoutEnd = await _gateway.GetLockoutEndAsync(user.UserId, cancellationToken);
        if (lockoutEnd is { } end && end > _timeProvider.GetUtcNow())
        {
            await _gateway.PerformDummyPasswordVerificationAsync(password, cancellationToken);
            return AdminLoginResult.Failure(AdminAuthErrorCodes.InvalidCredentials);
        }

        if (!await _gateway.CheckPasswordAsync(user.UserId, password, cancellationToken))
        {
            var newLockoutEnd = await _gateway.RegisterFailedAttemptAsync(
                user.UserId, AdminLockoutDuration, cancellationToken);

            // 同上：公開回應一律 invalid_credentials。這次呼叫剛好把帳號鎖起來時，把使用者快照
            // 往上帶給 Controller，讓它在同一個交易內補一筆中央 Audit（alex review）。
            return newLockoutEnd is not null
                ? AdminLoginResult.FailureWithLockoutAudit(user)
                : AdminLoginResult.Failure(AdminAuthErrorCodes.InvalidCredentials);
        }

        await _gateway.ResetAccessFailedCountAsync(user.UserId, cancellationToken);

        // 帳號生命週期（停權／移除管理員資格）只在密碼已確認正確後才揭露——沒有正確密碼的人
        // 無法用這個狀態差異來探測帳號是否存在或目前的狀態（alex review：帳號狀態列舉）。
        if (user.AccountStatus != AccountStatus.Active || !user.IsAdminProfileActive)
        {
            return AdminLoginResult.Failure(AdminAuthErrorCodes.AccountSuspended);
        }

        return user.TwoFactorEnabled
            ? AdminLoginResult.NeedsTwoFactor(user.UserId, user.PublicId)
            : AdminLoginResult.NeedsEnrollment(user.UserId, user.PublicId);
    }
}
