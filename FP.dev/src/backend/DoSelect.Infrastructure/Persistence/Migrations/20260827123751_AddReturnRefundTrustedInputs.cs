using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnRefundTrustedInputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssemblyFeeDisposition",
                table: "ReturnRequests",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnShippingCost",
                table: "ReturnRequests",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReturnRequests_RefundTrustedInputs",
                table: "ReturnRequests",
                sql: "([AssemblyFeeDisposition] IS NULL AND [ReturnShippingCost] IS NULL) OR ([AssemblyFeeDisposition] IS NOT NULL AND [ReturnShippingCost] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReturnRequests_ReturnShippingCost",
                table: "ReturnRequests",
                sql: "[ReturnShippingCost] IS NULL OR [ReturnShippingCost] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ReturnRequests_RefundTrustedInputs",
                table: "ReturnRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReturnRequests_ReturnShippingCost",
                table: "ReturnRequests");

            migrationBuilder.DropColumn(
                name: "AssemblyFeeDisposition",
                table: "ReturnRequests");

            migrationBuilder.DropColumn(
                name: "ReturnShippingCost",
                table: "ReturnRequests");
        }
    }
}
