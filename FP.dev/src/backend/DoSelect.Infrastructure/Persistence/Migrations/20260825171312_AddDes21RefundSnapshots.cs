using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDes21RefundSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [OrderItems])
                    THROW 51020, 'DES-21 requires the test order data set to be empty; historical coupon eligibility must not be guessed.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderCoupons_Amounts",
                table: "OrderCoupons");

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "RefundAllocations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingFreeThresholdSnapshot",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCouponEligible",
                table: "OrderItems",
                type: "bit",
                nullable: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumSpendAmount",
                table: "OrderCoupons",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.Sql(
                """
                IF EXISTS
                (
                    SELECT 1
                    FROM [RefundAllocations]
                    WHERE [AllocationType] NOT IN
                        ('ItemRefund', 'OriginalShipping', 'ShippingClawback', 'DiscountClawback', 'AssemblyFee', 'ReturnShipping')
                       OR ([AllocationType] = 'ItemRefund' AND ([OrderItemId] IS NULL OR [Quantity] IS NULL OR [Quantity] <= 0))
                       OR ([AllocationType] <> 'ItemRefund' AND ([OrderItemId] IS NOT NULL OR [Quantity] IS NOT NULL))
                )
                    THROW 51021, 'DES-21 cannot infer trusted RefundAllocations.Quantity or repair an invalid allocation shape.', 1;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RefundAllocations_TypeAndShape",
                table: "RefundAllocations",
                sql: "[AllocationType] IN ('ItemRefund', 'OriginalShipping', 'ShippingClawback', 'DiscountClawback', 'AssemblyFee', 'ReturnShipping') AND (([AllocationType] = 'ItemRefund' AND [OrderItemId] IS NOT NULL AND [Quantity] > 0) OR ([AllocationType] <> 'ItemRefund' AND [OrderItemId] IS NULL AND [Quantity] IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_ShippingFreeThresholdSnapshot",
                table: "Orders",
                sql: "[ShippingFreeThresholdSnapshot] IS NULL OR [ShippingFreeThresholdSnapshot] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderCoupons_Amounts",
                table: "OrderCoupons",
                sql: "([DiscountValue] IS NULL OR [DiscountValue] >= 0) AND ([MinimumSpendAmount] IS NULL OR [MinimumSpendAmount] >= 0) AND [AppliedAmount] >= 0 AND [EligibleSubtotal] >= 0 AND [RuleVersion] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RefundAllocations_TypeAndShape",
                table: "RefundAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_ShippingFreeThresholdSnapshot",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderCoupons_Amounts",
                table: "OrderCoupons");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "RefundAllocations");

            migrationBuilder.DropColumn(
                name: "ShippingFreeThresholdSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsCouponEligible",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "MinimumSpendAmount",
                table: "OrderCoupons");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderCoupons_Amounts",
                table: "OrderCoupons",
                sql: "([DiscountValue] IS NULL OR [DiscountValue] >= 0) AND [AppliedAmount] >= 0 AND [EligibleSubtotal] >= 0 AND [RuleVersion] > 0");
        }
    }
}
