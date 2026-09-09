using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponQuantityAndMembershipRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MemberValidityMonths",
                table: "Coupons",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MultiItemDiscountValue",
                table: "Coupons",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_MemberValidity",
                table: "Coupons",
                sql: "[MemberValidityMonths] IS NULL OR ([MemberValidityMonths] = 12 AND [MemberOnly] = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_MultiItemDiscount",
                table: "Coupons",
                sql: "[MultiItemDiscountValue] IS NULL OR ([DiscountType] = 'Percentage' AND [DiscountValue] IS NOT NULL AND [DiscountValue] > 0 AND [MultiItemDiscountValue] >= [DiscountValue] AND [MultiItemDiscountValue] <= 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Coupons_MemberValidity",
                table: "Coupons");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Coupons_MultiItemDiscount",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "MemberValidityMonths",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "MultiItemDiscountValue",
                table: "Coupons");
        }
    }
}
