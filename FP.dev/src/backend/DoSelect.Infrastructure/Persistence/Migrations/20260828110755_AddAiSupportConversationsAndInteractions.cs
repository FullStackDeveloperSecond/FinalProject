using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiSupportConversationsAndInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiConversations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SupportTicketId = table.Column<long>(type: "bigint", nullable: true),
                    Purpose = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Locale = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ConsentPolicyVersion = table.Column<int>(type: "int", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConversations", x => x.Id);
                    table.CheckConstraint("CK_AiConversations_ConsentPolicyVersion", "[ConsentPolicyVersion] > 0");
                    table.CheckConstraint("CK_AiConversations_Purpose", "[Purpose] IN ('Support')");
                    table.CheckConstraint("CK_AiConversations_Status", "[Status] IN ('Active','Closed')");
                    table.ForeignKey(
                        name: "FK_AiConversations_AspNetUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiConversations_SupportTickets_SupportTicketId",
                        column: x => x.SupportTicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiInteractions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AiConversationId = table.Column<long>(type: "bigint", nullable: true),
                    SearchPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    UserContentProtected = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    AssistantContent = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IntentJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    FallbackReason = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    LatencyMs = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiInteractions", x => x.Id);
                    table.CheckConstraint("CK_AiInteractions_Owner", "([AiConversationId] IS NOT NULL AND [SearchPublicId] IS NULL) OR ([AiConversationId] IS NULL AND [SearchPublicId] IS NOT NULL)");
                    table.CheckConstraint("CK_AiInteractions_Usage", "[Sequence] > 0 AND [InputTokens] >= 0 AND [OutputTokens] >= 0 AND [EstimatedCostUsd] >= 0 AND [LatencyMs] >= 0");
                    table.ForeignKey(
                        name: "FK_AiInteractions_AiConversations_AiConversationId",
                        column: x => x.AiConversationId,
                        principalTable: "AiConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiCitations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AiInteractionId = table.Column<long>(type: "bigint", nullable: false),
                    SourceType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    SourcePublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiCitations_AiInteractions_AiInteractionId",
                        column: x => x.AiInteractionId,
                        principalTable: "AiInteractions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AiCitations_AiInteractionId_SortOrder",
                table: "AiCitations",
                columns: new[] { "AiInteractionId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_MemberUserId_LastActivityAtUtc",
                table: "AiConversations",
                columns: new[] { "MemberUserId", "LastActivityAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_SupportTicketId",
                table: "AiConversations",
                column: "SupportTicketId");

            migrationBuilder.CreateIndex(
                name: "UX_AiConversations_PublicId",
                table: "AiConversations",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_AiInteractions_AiConversationId_Sequence",
                table: "AiInteractions",
                columns: new[] { "AiConversationId", "Sequence" },
                unique: true,
                filter: "[AiConversationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AiInteractions_PublicId",
                table: "AiInteractions",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCitations");

            migrationBuilder.DropTable(
                name: "AiInteractions");

            migrationBuilder.DropTable(
                name: "AiConversations");
        }
    }
}
