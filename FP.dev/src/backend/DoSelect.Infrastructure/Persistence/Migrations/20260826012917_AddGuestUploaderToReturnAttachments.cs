using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestUploaderToReturnAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "UploadedByUserId",
                table: "ReturnAttachments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AddColumn<long>(
                name: "UploadedByGuestOrderId",
                table: "ReturnAttachments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnAttachments_UploadedByGuestOrderId",
                table: "ReturnAttachments",
                column: "UploadedByGuestOrderId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReturnAttachments_UploaderIdentity",
                table: "ReturnAttachments",
                sql: "([UploadedByUserId] IS NOT NULL AND [UploadedByGuestOrderId] IS NULL) OR ([UploadedByUserId] IS NULL AND [UploadedByGuestOrderId] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ReturnAttachments_Orders_UploadedByGuestOrderId",
                table: "ReturnAttachments",
                column: "UploadedByGuestOrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversing this migration is only safe when no row has ever recorded a guest
            // uploader: restoring the old NOT NULL UploadedByUserId column would otherwise force
            // every guest-attributed row to a fabricated empty string, silently corrupting
            // upload attribution. Fail fast — before any of the drops/alters below run — rather
            // than perform a lossy rollback. Roll forward instead of back if this fires.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [ReturnAttachments] WHERE [UploadedByGuestOrderId] IS NOT NULL)
                BEGIN
                    THROW 51000, 'Cannot roll back AddGuestUploaderToReturnAttachments: ReturnAttachments rows exist with a non-null UploadedByGuestOrderId. Reversing this migration would corrupt guest-uploaded attachment attribution. Resolve or archive those rows before rolling back, or roll forward instead.', 1;
                END
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ReturnAttachments_Orders_UploadedByGuestOrderId",
                table: "ReturnAttachments");

            migrationBuilder.DropIndex(
                name: "IX_ReturnAttachments_UploadedByGuestOrderId",
                table: "ReturnAttachments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReturnAttachments_UploaderIdentity",
                table: "ReturnAttachments");

            migrationBuilder.DropColumn(
                name: "UploadedByGuestOrderId",
                table: "ReturnAttachments");

            migrationBuilder.AlterColumn<string>(
                name: "UploadedByUserId",
                table: "ReturnAttachments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);
        }
    }
}
