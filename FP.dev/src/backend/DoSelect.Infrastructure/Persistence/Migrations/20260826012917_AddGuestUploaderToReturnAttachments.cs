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
