using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCentralAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ActorPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorRolesJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Action = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ResourcePublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Result = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ErrorCode = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    ChangedFieldsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ChangedFieldsSchemaVersion = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    TraceId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    JobPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MaskedIpAddress = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RetentionUntilUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    IsLegalHold = table.Column<bool>(type: "bit", nullable: false),
                    HoldReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.CheckConstraint("CK_AuditLogs_Actor", "(([ActorType] = 'System' AND [ActorPublicId] IS NULL) OR ([ActorType] IN ('Member', 'Admin', 'Guest') AND [ActorPublicId] IS NOT NULL)) AND (([ActorType] = 'Admin' AND [ActorRolesJson] <> '[]') OR ([ActorType] <> 'Admin' AND [ActorRolesJson] = '[]'))");
                    table.CheckConstraint("CK_AuditLogs_Json", "ISJSON([ActorRolesJson]) = 1 AND ISJSON([ChangedFieldsJson]) = 1");
                    table.CheckConstraint("CK_AuditLogs_LegalHold", "([IsLegalHold] = 0 AND [HoldReason] IS NULL) OR ([IsLegalHold] = 1 AND [HoldReason] IS NOT NULL)");
                    table.CheckConstraint("CK_AuditLogs_Result", "([Result] = 'Success' AND [ErrorCode] IS NULL) OR ([Result] IN ('Rejected', 'Conflict', 'Failed') AND [ErrorCode] IS NOT NULL)");
                    table.CheckConstraint("CK_AuditLogs_Retention", "[RetentionUntilUtc] >= [OccurredAtUtc]");
                    table.CheckConstraint("CK_AuditLogs_SchemaVersion", "[ChangedFieldsSchemaVersion] > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action_OccurredAtUtc",
                table: "AuditLogs",
                columns: new[] { "Action", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorPublicId_OccurredAtUtc",
                table: "AuditLogs",
                columns: new[] { "ActorPublicId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OccurredAtUtc",
                table: "AuditLogs",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Resource_OccurredAtUtc",
                table: "AuditLogs",
                columns: new[] { "ResourceType", "ResourcePublicId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Retention",
                table: "AuditLogs",
                columns: new[] { "IsLegalHold", "RetentionUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_AuditLogs_PublicId",
                table: "AuditLogs",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");
        }
    }
}
