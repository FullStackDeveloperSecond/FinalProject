namespace DoSelect.Api.Security;

public static class DoSelectAuthenticationSchemes
{
    public const string Member = "DoSelect.Member";
    public const string Admin = "DoSelect.Admin";
}

public static class DoSelectClaimTypes
{
    public const string AccountType = "doselect:account_type";
    public const string AuthenticationMethod = "amr";
    public const string SecurityStamp = "doselect:security_stamp";
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
