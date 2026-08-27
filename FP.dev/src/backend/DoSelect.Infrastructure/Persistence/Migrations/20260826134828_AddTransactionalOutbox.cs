using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionalOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    PayloadVersion = table.Column<int>(type: "int", nullable: false),
                    AggregateType = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    AggregatePublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadJson = table.Column<string>(type: "varchar(8000)", unicode: false, maxLength: 8000, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    LastErrorCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_OutboxMessages_AttemptCount", "[AttemptCount] >= 0");
                    table.CheckConstraint("CK_OutboxMessages_Availability", "[AvailableAtUtc] >= [OccurredAtUtc]");
                    table.CheckConstraint("CK_OutboxMessages_PayloadJson", "ISJSON([PayloadJson]) = 1");
                    table.CheckConstraint("CK_OutboxMessages_PayloadVersion", "[PayloadVersion] > 0");
                    table.CheckConstraint("CK_OutboxMessages_ProcessedState", "([Status] = 'Processed' AND [ProcessedAtUtc] IS NOT NULL) OR ([Status] <> 'Processed' AND [ProcessedAtUtc] IS NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Aggregate_OccurredAtUtc",
                table: "OutboxMessages",
                columns: new[] { "AggregateType", "AggregatePublicId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_AvailableAtUtc",
                table: "OutboxMessages",
                columns: new[] { "Status", "AvailableAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_OutboxMessages_PublicId",
                table: "OutboxMessages",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");
        }
    }
}
