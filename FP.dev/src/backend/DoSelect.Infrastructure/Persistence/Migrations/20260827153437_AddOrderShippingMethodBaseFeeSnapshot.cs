using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderShippingMethodBaseFeeSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ShippingMethodBaseFeeSnapshot",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_ShippingMethodBaseFeeSnapshot",
                table: "Orders",
                sql: "[ShippingMethodBaseFeeSnapshot] IS NULL OR [ShippingMethodBaseFeeSnapshot] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_ShippingMethodBaseFeeSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingMethodBaseFeeSnapshot",
                table: "Orders");
        }
    }
}
