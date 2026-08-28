using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiSafetyConsentAndUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiConsentRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PolicyVersion = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Locale = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    WithdrawnAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Source = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConsentRecords", x => x.Id);
                    table.CheckConstraint("CK_AiConsentRecords_Locale", "[Locale] IN ('zh-TW','ja-JP','ko-KR')");
                    table.CheckConstraint("CK_AiConsentRecords_PolicyVersion", "[PolicyVersion] > 0");
                    table.CheckConstraint("CK_AiConsentRecords_Purpose", "[Purpose] IN ('Support')");
                    table.CheckConstraint("CK_AiConsentRecords_Status", "([Status] = 'Granted' AND [WithdrawnAtUtc] IS NULL) OR ([Status] = 'Withdrawn' AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawnAtUtc] >= [GrantedAtUtc])");
                    table.ForeignKey(
                        name: "FK_AiConsentRecords_AspNetUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiUsageLedger",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AnonymousSessionKeyHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    Feature = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    RequestPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageLedger", x => x.Id);
                    table.CheckConstraint("CK_AiUsageLedger_Owner", "([MemberUserId] IS NOT NULL AND [AnonymousSessionKeyHash] IS NULL) OR ([MemberUserId] IS NULL AND [AnonymousSessionKeyHash] IS NOT NULL)");
                    table.CheckConstraint("CK_AiUsageLedger_Usage", "[InputTokens] >= 0 AND [OutputTokens] >= 0 AND [EstimatedCostUsd] >= 0");
                    table.ForeignKey(
                        name: "FK_AiUsageLedger_AspNetUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiConsentRecords_MemberUserId_Purpose_PolicyVersion_CreatedAtUtc",
                table: "AiConsentRecords",
                columns: new[] { "MemberUserId", "Purpose", "PolicyVersion", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageLedger_MemberUserId_Feature_OccurredAtUtc",
                table: "AiUsageLedger",
                columns: new[] { "MemberUserId", "Feature", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_AiUsageLedger_RequestPublicId",
                table: "AiUsageLedger",
                column: "RequestPublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiConsentRecords");

            migrationBuilder.DropTable(
                name: "AiUsageLedger");
        }
    }
}
