using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestCheckoutEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuestCheckoutEmailVerifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmailNormalized = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    EmailHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    GuestCartKeyHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    RequesterIpHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ProofTokenHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuestCheckoutEmailVerifications", x => x.Id);
                    table.CheckConstraint("CK_GuestCheckoutEmailVerifications_AttemptCount", "[AttemptCount] >= 0 AND [AttemptCount] <= 5");
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuestCheckoutEmailVerifications_EmailHash_CreatedAtUtc",
                table: "GuestCheckoutEmailVerifications",
                columns: new[] { "EmailHash", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GuestCheckoutEmailVerifications_ExpiresAtUtc",
                table: "GuestCheckoutEmailVerifications",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GuestCheckoutEmailVerifications_GuestCartKeyHash_CreatedAtUtc",
                table: "GuestCheckoutEmailVerifications",
                columns: new[] { "GuestCartKeyHash", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GuestCheckoutEmailVerifications_RequesterIpHash_CreatedAtUtc",
                table: "GuestCheckoutEmailVerifications",
                columns: new[] { "RequesterIpHash", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_GuestCheckoutEmailVerifications_ProofTokenHash",
                table: "GuestCheckoutEmailVerifications",
                column: "ProofTokenHash",
                unique: true,
                filter: "[ProofTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_GuestCheckoutEmailVerifications_PublicId",
                table: "GuestCheckoutEmailVerifications",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuestCheckoutEmailVerifications");
        }
    }
}
