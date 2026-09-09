SET NOCOUNT ON;
IF DB_NAME() NOT LIKE N'DoSelectDemo[_]%' OR LEN(DB_NAME()) <> 45
   OR RIGHT(DB_NAME(), 32) LIKE N'%[^0-9A-Fa-f]%'
    THROW 51100, 'Only an isolated local Demo database is supported.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.Brands WHERE Code = N'DEMO-V3-BRAND-001')
    THROW 51101, 'Expected v3 Demo marker is missing.', 1;
IF (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory) <> N'20260909074216_AddBuildOwnedParts'
    THROW 51102, 'Unexpected migration baseline; stop and review.', 1;
IF COL_LENGTH(N'dbo.Coupons', N'MemberValidityMonths') IS NOT NULL
   OR COL_LENGTH(N'dbo.Coupons', N'MultiItemDiscountValue') IS NOT NULL
    THROW 51103, 'Target columns already exist without matching history.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.Coupons WHERE Code = N'CREATOR10')
    THROW 51104, 'Expected historical campaign is missing.', 1;
SELECT COUNT_BIG(*) AS CouponCount FROM dbo.Coupons;
