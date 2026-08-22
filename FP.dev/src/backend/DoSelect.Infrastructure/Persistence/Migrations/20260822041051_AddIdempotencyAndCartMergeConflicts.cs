using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyAndCartMergeConflicts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CartMergeConflicts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberCartId = table.Column<long>(type: "bigint", nullable: false),
                    GuestCartId = table.Column<long>(type: "bigint", nullable: false),
                    GuestItemPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SkuPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GuestQuantity = table.Column<int>(type: "int", nullable: false),
                    MemberQuantity = table.Column<int>(type: "int", nullable: false),
                    AcceptedQuantity = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ResolutionCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartMergeConflicts", x => x.Id);
                    table.CheckConstraint("CK_CartMergeConflicts_DifferentCarts", "[MemberCartId] <> [GuestCartId]");
                    table.CheckConstraint("CK_CartMergeConflicts_Quantities", "[GuestQuantity] >= 1 AND [GuestQuantity] <= 99 AND [MemberQuantity] >= 0 AND [MemberQuantity] <= 99 AND [AcceptedQuantity] >= 0 AND [AcceptedQuantity] <= 99");
                    table.CheckConstraint("CK_CartMergeConflicts_Resolution", "([ResolvedAtUtc] IS NULL AND [ResolutionCode] IS NULL) OR ([ResolvedAtUtc] IS NOT NULL AND [ResolutionCode] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CartMergeConflicts_Carts_GuestCartId",
                        column: x => x.GuestCartId,
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CartMergeConflicts_Carts_MemberCartId",
                        column: x => x.MemberCartId,
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorScopeHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Operation = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    Key = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseHeadersJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ResponseSummary = table.Column<string>(type: "nvarchar(max)", maxLength: 32768, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                    table.CheckConstraint("CK_IdempotencyRecords_CompletedResponse", "([Status] = 'Processing' AND [ResponseStatusCode] IS NULL AND [ResponseHeadersJson] IS NULL AND [ResponseSummary] IS NULL) OR ([Status] IN ('Succeeded', 'Failed') AND [ResponseStatusCode] IS NOT NULL AND [ResponseHeadersJson] IS NOT NULL AND [ResponseSummary] IS NOT NULL)");
                    table.CheckConstraint("CK_IdempotencyRecords_ResponseStatusCode", "[ResponseStatusCode] IS NULL OR ([ResponseStatusCode] >= 100 AND [ResponseStatusCode] <= 599)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CartMergeConflicts_GuestCartId",
                table: "CartMergeConflicts",
                column: "GuestCartId");

            migrationBuilder.CreateIndex(
                name: "IX_CartMergeConflicts_MemberCart_ResolvedAtUtc",
                table: "CartMergeConflicts",
                columns: new[] { "MemberCartId", "ResolvedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CartMergeConflicts_MemberCart_GuestItem_Unresolved",
                table: "CartMergeConflicts",
                columns: new[] { "MemberCartId", "GuestItemPublicId" },
                unique: true,
                filter: "[ResolvedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CartMergeConflicts_PublicId",
                table: "CartMergeConflicts",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAtUtc",
                table: "IdempotencyRecords",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_IdempotencyRecords_ActorScope_Operation_Key",
                table: "IdempotencyRecords",
                columns: new[] { "ActorScopeHash", "Operation", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CartMergeConflicts");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");
        }
    }
}
