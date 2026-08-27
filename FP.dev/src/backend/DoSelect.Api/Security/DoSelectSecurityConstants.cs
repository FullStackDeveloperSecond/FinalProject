namespace DoSelect.Api.Security;

public static class DoSelectAuthenticationSchemes
{
    public const string Member = "DoSelect.Member";
    public const string Admin = "DoSelect.Admin";

    /// <summary>
    /// ⚠ 新增：密碼驗證成功、TOTP 尚未完成前的短效憑證。刻意不帶 AccountType／amr claim，
    /// 因此結構上無法通過任何 <see cref="DoSelectPolicies"/>。用來取代新建資料表表示 2FA 挑戰狀態。
    /// </summary>
    public const string AdminChallenge = "DoSelect.AdminChallenge";
}

public static class DoSelectClaimTypes
{
    public const string AccountType = "doselect:account_type";
    public const string AuthenticationMethod = "amr";
    public const string SecurityStamp = "doselect:security_stamp";

    /// <summary>⚠ 新增：AdminChallenge Cookie 專用，"totp" 或 "enroll"。</summary>
    public const string ChallengeKind = "doselect:challenge_kind";

    /// <summary>⚠ 新增：AdminChallenge Cookie 專用，對應回傳給前端的 twoFactorChallengePublicId。</summary>
    public const string ChallengeId = "doselect:challenge_id";
}

public static class DoSelectClaimValues
{
    public const string Member = "member";
    public const string Admin = "admin";
    public const string MultiFactor = "mfa";
}

public static class DoSelectRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string CatalogManager = "CatalogManager";
    public const string InventoryManager = "InventoryManager";
    public const string OrderManager = "OrderManager";
    public const string FinanceManager = "FinanceManager";
    public const string CustomerService = "CustomerService";
    public const string CustomerServiceSupervisor = "CustomerServiceSupervisor";
    public const string MarketingAnalyst = "MarketingAnalyst";
    public const string PrivacyAdmin = "PrivacyAdmin";
    public const string SecurityAdmin = "SecurityAdmin";
}

public static class DoSelectPolicies
{
    public const string Member = "Member";
    public const string Admin = "Admin";
    public const string CatalogManager = "CatalogManager";
    public const string ReturnApprove = "Return.Approve";
    public const string RefundExecute = "Refund.Execute";
    public const string InvoiceManage = "Invoice.Manage";
    public const string CouponManage = "Coupon.Manage";
    public const string ReportHighRiskReview = "Report.HighRiskReview";
    public const string RoleAssignmentManage = "RoleAssignment.Manage";
    public const string PersonalDataViewFull = "PersonalData.ViewFull";
    public const string PersonalDataExport = "PersonalData.Export";
    public const string AuditViewSecurity = "Audit.ViewSecurity";
    public const string AuditViewPrivacy = "Audit.ViewPrivacy";
    public const string AuditExport = "Audit.Export";
    public const string SupportTicketHandle = "SupportTicket.Handle";
    public const string SupportTicketSupervise = "SupportTicket.Supervise";
}
