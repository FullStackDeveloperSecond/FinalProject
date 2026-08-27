using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveryInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotificationPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RecipientEmailNormalized = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    TemplateCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    TemplateVersion = table.Column<int>(type: "int", nullable: false),
                    RecipientPurpose = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    LastErrorCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDeliveries", x => x.Id);
                    table.CheckConstraint("CK_EmailDeliveries_AttemptCount", "[AttemptCount] >= 0");
                    table.CheckConstraint("CK_EmailDeliveries_State", "([Status] = 'Pending' AND [NextAttemptAtUtc] IS NOT NULL AND [SentAtUtc] IS NULL) OR ([Status] = 'Processing' AND [NextAttemptAtUtc] IS NULL AND [SentAtUtc] IS NULL) OR ([Status] = 'Sent' AND [NextAttemptAtUtc] IS NULL AND [SentAtUtc] IS NOT NULL) OR ([Status] IN ('Suppressed', 'Failed') AND [NextAttemptAtUtc] IS NULL AND [SentAtUtc] IS NULL AND [FailedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_EmailDeliveries_TemplateVersion", "[TemplateVersion] > 0");
                    table.ForeignKey(
                        name: "FK_EmailDeliveries_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipientUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Type = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ResourceType = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ResourcePublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_ExpiresAtUtc", "[ExpiresAtUtc] IS NULL OR [ExpiresAtUtc] > [CreatedAtUtc]");
                    table.CheckConstraint("CK_Notifications_Resource", "([ResourceType] IS NULL AND [ResourcePublicId] IS NULL) OR ([ResourceType] IS NOT NULL AND [ResourcePublicId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveries_RecipientUserId",
                table: "EmailDeliveries",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveries_Status_NextAttemptAtUtc",
                table: "EmailDeliveries",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_EmailDeliveries_NotificationPublicId",
                table: "EmailDeliveries",
                column: "NotificationPublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_ReadAtUtc_CreatedAtUtc",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "ReadAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Notifications_PublicId",
                table: "Notifications",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailDeliveries");

            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
