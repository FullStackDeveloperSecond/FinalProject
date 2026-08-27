using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiValueSpecificationProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowsMultiple",
                table: "SpecificationDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SkuSpecificationOptionSelections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SkuId = table.Column<long>(type: "bigint", nullable: false),
                    SpecificationOptionId = table.Column<long>(type: "bigint", nullable: false),
                    SpecificationSourceId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkuSpecificationOptionSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SkuSpecificationOptionSelections_Skus_SkuId",
                        column: x => x.SkuId,
                        principalTable: "Skus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkuSpecificationOptionSelections_SpecificationOptions_SpecificationOptionId",
                        column: x => x.SpecificationOptionId,
                        principalTable: "SpecificationOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkuSpecificationOptionSelections_SpecificationSources_SpecificationSourceId",
                        column: x => x.SpecificationSourceId,
                        principalTable: "SpecificationSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SkuSpecificationOptionSelections_SpecificationOptionId",
                table: "SkuSpecificationOptionSelections",
                column: "SpecificationOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SkuSpecificationOptionSelections_SpecificationSourceId",
                table: "SkuSpecificationOptionSelections",
                column: "SpecificationSourceId");

            migrationBuilder.CreateIndex(
                name: "UX_SkuSpecificationOptionSelections_SkuId_OptionId",
                table: "SkuSpecificationOptionSelections",
                columns: new[] { "SkuId", "SpecificationOptionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SkuSpecificationOptionSelections");

            migrationBuilder.DropColumn(
                name: "AllowsMultiple",
                table: "SpecificationDefinitions");
        }
    }
}
