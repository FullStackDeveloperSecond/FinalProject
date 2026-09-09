BEGIN TRANSACTION;
ALTER TABLE [Coupons] ADD [MemberValidityMonths] int NULL;

ALTER TABLE [Coupons] ADD [MultiItemDiscountValue] decimal(18,2) NULL;
-- Deployment-only batch boundary: SQL Server must resolve the new columns before
-- compiling CHECK expressions. GO retains this connection and open transaction.
GO

ALTER TABLE [Coupons] ADD CONSTRAINT [CK_Coupons_MemberValidity] CHECK ([MemberValidityMonths] IS NULL OR ([MemberValidityMonths] = 12 AND [MemberOnly] = 1));

ALTER TABLE [Coupons] ADD CONSTRAINT [CK_Coupons_MultiItemDiscount] CHECK ([MultiItemDiscountValue] IS NULL OR ([DiscountType] = 'Percentage' AND [DiscountValue] IS NOT NULL AND [DiscountValue] > 0 AND [MultiItemDiscountValue] >= [DiscountValue] AND [MultiItemDiscountValue] <= 1));

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260909164147_AddCouponQuantityAndMembershipRules', N'10.0.10');

COMMIT;
GO
