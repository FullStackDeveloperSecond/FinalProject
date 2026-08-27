using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPackageAndSpecificationSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "Orders",
                type: "char(2)",
                unicode: false,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryNote",
                table: "Orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageDeclaredValueSnapshot",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageHeightCmSnapshot",
                table: "Orders",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageLengthCmSnapshot",
                table: "Orders",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PackageLimitVersionId",
                table: "Orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageTotalCmSnapshot",
                table: "Orders",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageWeightKgSnapshot",
                table: "Orders",
                type: "decimal(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageWidthCmSnapshot",
                table: "Orders",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecificationJsonSnapshot",
                table: "OrderItems",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecificationSchemaVersion",
                table: "OrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecificationSummarySnapshot",
                table: "OrderItems",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PackageLimitVersionId",
                table: "Orders",
                column: "PackageLimitVersionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_CountryCode",
                table: "Orders",
                sql: "[CountryCode] IS NULL OR [CountryCode] = 'TW'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_PackageSnapshot",
                table: "Orders",
                sql: "([PackageLimitVersionId] IS NULL AND [PackageWeightKgSnapshot] IS NULL AND [PackageLengthCmSnapshot] IS NULL AND [PackageWidthCmSnapshot] IS NULL AND [PackageHeightCmSnapshot] IS NULL AND [PackageTotalCmSnapshot] IS NULL AND [PackageDeclaredValueSnapshot] IS NULL) OR ([PackageLimitVersionId] IS NOT NULL AND [PackageWeightKgSnapshot] > 0 AND [PackageLengthCmSnapshot] > 0 AND [PackageWidthCmSnapshot] > 0 AND [PackageHeightCmSnapshot] > 0 AND [PackageTotalCmSnapshot] = [PackageLengthCmSnapshot] + [PackageWidthCmSnapshot] + [PackageHeightCmSnapshot] AND [PackageDeclaredValueSnapshot] >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderItems_SpecificationSnapshot",
                table: "OrderItems",
                sql: "([SpecificationSummarySnapshot] IS NULL AND [SpecificationJsonSnapshot] IS NULL AND [SpecificationSchemaVersion] IS NULL) OR ([SpecificationSummarySnapshot] IS NOT NULL AND [SpecificationJsonSnapshot] IS NOT NULL AND [SpecificationSchemaVersion] > 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_PackageLimitVersions_PackageLimitVersionId",
                table: "Orders",
                column: "PackageLimitVersionId",
                principalTable: "PackageLimitVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_PackageLimitVersions_PackageLimitVersionId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PackageLimitVersionId",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_CountryCode",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_PackageSnapshot",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItems_SpecificationSnapshot",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryNote",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PackageDeclaredValueSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PackageHeightCmSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PackageLengthCmSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PackageLimitVersionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PackageTotalCmSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PackageWeightKgSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PackageWidthCmSnapshot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SpecificationJsonSnapshot",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "SpecificationSchemaVersion",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "SpecificationSummarySnapshot",
                table: "OrderItems");
        }
    }
}
