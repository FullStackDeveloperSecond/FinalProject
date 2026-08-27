using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSkuStorageInterfacePorts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SkuCompatibilityAttributes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    AttributeKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    AttributeValue = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkuCompatibilityAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SkuCompatibilityAttributes_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SkuStorageInterfacePorts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    InterfaceCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PortCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkuStorageInterfacePorts", x => x.Id);
                    table.CheckConstraint("CK_SkuStorageInterfacePorts_PortCount", "[PortCount] > 0 AND [PortCount] <= 32");
                    table.ForeignKey(
                        name: "FK_SkuStorageInterfacePorts_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_SkuCompatibilityAttributes_SkuId_AttributeKey_AttributeValue",
                table: "SkuCompatibilityAttributes",
                columns: new[] { "SkuId", "AttributeKey", "AttributeValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SkuStorageInterfacePorts_SkuId_InterfaceCode",
                table: "SkuStorageInterfacePorts",
                columns: new[] { "SkuId", "InterfaceCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SkuCompatibilityAttributes");

            migrationBuilder.DropTable(
                name: "SkuStorageInterfacePorts");
        }
    }
}
