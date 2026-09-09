SET NOCOUNT ON;
IF DB_NAME() NOT LIKE N'DoSelectDemo[_]%' OR LEN(DB_NAME()) <> 45
   OR RIGHT(DB_NAME(), 32) LIKE N'%[^0-9A-Fa-f]%'
    THROW 51100, 'Only an isolated local Demo database is supported.', 1;
IF (SELECT MAX(MigrationId) FROM dbo.__EFMigrationsHistory) <> N'20260909164147_AddCouponQuantityAndMembershipRules'
    THROW 51105, 'Expected coupon migration is missing or superseded.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Coupons')
    AND name = N'MemberValidityMonths' AND TYPE_NAME(user_type_id) = N'int' AND is_nullable = 1)
    THROW 51106, 'Member validity column does not match the reviewed model.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Coupons')
    AND name = N'MultiItemDiscountValue' AND TYPE_NAME(user_type_id) = N'decimal'
    AND precision = 18 AND scale = 2 AND is_nullable = 1)
    THROW 51107, 'Quantity discount column does not match the reviewed model.', 1;
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Coupons')
    AND name IN (N'CK_Coupons_MemberValidity', N'CK_Coupons_MultiItemDiscount')
    AND is_disabled = 0 AND is_not_trusted = 0) <> 2
    THROW 51108, 'Expected trusted coupon constraints are missing.', 1;
SELECT COUNT_BIG(*) AS CouponCount FROM dbo.Coupons;
