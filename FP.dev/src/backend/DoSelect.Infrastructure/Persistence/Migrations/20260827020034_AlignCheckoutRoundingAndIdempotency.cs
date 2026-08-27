using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignCheckoutRoundingAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Orders_CheckoutIdempotencyKey",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_GrandTotal",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CheckoutIdempotencyKey",
                table: "Orders",
                column: "CheckoutIdempotencyKey");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_GrandTotal",
                table: "Orders",
                sql: "[GrandTotal] = ROUND([MerchandiseSubtotal] - [ItemDiscountTotal] + [ShippingFee] + [AssemblyFee], 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_CheckoutIdempotencyKey",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_GrandTotal",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "UX_Orders_CheckoutIdempotencyKey",
                table: "Orders",
                column: "CheckoutIdempotencyKey",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_GrandTotal",
                table: "Orders",
                sql: "[GrandTotal] = [MerchandiseSubtotal] - [ItemDiscountTotal] + [ShippingFee] + [AssemblyFee]");
        }
    }
}
