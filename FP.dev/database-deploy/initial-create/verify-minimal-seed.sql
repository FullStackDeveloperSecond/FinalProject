SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedRoleCount int = 10;
DECLARE @RoleCount int =
(
    SELECT COUNT(*)
    FROM dbo.AspNetRoles
    WHERE [Name] IN
    (
        N'SuperAdmin', N'CatalogManager', N'InventoryManager', N'OrderManager',
        N'FinanceManager', N'CustomerService', N'CustomerServiceSupervisor',
        N'MarketingAnalyst', N'PrivacyAdmin', N'SecurityAdmin'
    )
);

DECLARE @AdminCount int =
(
    SELECT COUNT(*)
    FROM dbo.AspNetUsers
    WHERE NormalizedEmail = N'ADMIN@DOSELECT.LOCAL'
      AND EmailConfirmed = 1
      AND AccountType = 'Admin'
      AND TwoFactorEnabled = 0
);

DECLARE @MemberCount int =
(
    SELECT COUNT(*)
    FROM dbo.AspNetUsers
    WHERE NormalizedEmail = N'MEMBER@DOSELECT.LOCAL'
      AND EmailConfirmed = 1
      AND AccountType = 'Member'
);

DECLARE @AdminRoleCount int =
(
    SELECT COUNT(*)
    FROM dbo.AspNetUserRoles userRole
    INNER JOIN dbo.AspNetUsers [user] ON [user].Id = userRole.UserId
    INNER JOIN dbo.AspNetRoles [role] ON [role].Id = userRole.RoleId
    WHERE [user].NormalizedEmail = N'ADMIN@DOSELECT.LOCAL'
      AND [role].[Name] = N'SuperAdmin'
);

DECLARE @ProfileCount int =
(
    SELECT
        (SELECT COUNT(*) FROM dbo.AdminProfiles WHERE EmployeeCode = N'DEV-ADMIN-001') +
        (SELECT COUNT(*) FROM dbo.MemberProfiles profile
         INNER JOIN dbo.AspNetUsers [user] ON [user].Id = profile.UserId
         WHERE [user].NormalizedEmail = N'MEMBER@DOSELECT.LOCAL')
);

DECLARE @CatalogCount int =
(
    SELECT
        (SELECT COUNT(*) FROM dbo.Brands WHERE Code = N'DOSELECT-DEV') +
        (SELECT COUNT(*) FROM dbo.Categories WHERE Code = N'DEV-GRAPHICS-CARDS') +
        (SELECT COUNT(*) FROM dbo.Products WHERE ProductCode = N'DEV-GPU-001') +
        (SELECT COUNT(*) FROM dbo.Skus WHERE SkuCode = N'DEV-GPU-001-16G')
);

SELECT
    @RoleCount AS FormalRoles,
    @AdminCount AS ConfirmedAdminAccounts,
    @MemberCount AS ConfirmedMemberAccounts,
    @AdminRoleCount AS SuperAdminAssignments,
    @ProfileCount AS Profiles,
    @CatalogCount AS CatalogRecords;

IF @RoleCount <> @ExpectedRoleCount
    THROW 51000, 'Minimal seed verification failed: formal roles.', 1;
IF @AdminCount <> 1
    THROW 51000, 'Minimal seed verification failed: admin account.', 1;
IF @MemberCount <> 1
    THROW 51000, 'Minimal seed verification failed: member account.', 1;
IF @AdminRoleCount <> 1
    THROW 51000, 'Minimal seed verification failed: SuperAdmin assignment.', 1;
IF @ProfileCount <> 2
    THROW 51000, 'Minimal seed verification failed: profiles.', 1;
IF @CatalogCount <> 4
    THROW 51000, 'Minimal seed verification failed: catalog records.', 1;

SELECT N'PASS' AS VerificationResult;
