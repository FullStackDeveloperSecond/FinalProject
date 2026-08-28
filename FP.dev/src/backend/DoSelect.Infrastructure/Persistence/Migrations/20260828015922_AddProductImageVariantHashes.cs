using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductImageVariantHashes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "LargeSha256",
                table: "ProductImages",
                type: "binary(32)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "MediumSha256",
                table: "ProductImages",
                type: "binary(32)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SmallSha256",
                table: "ProductImages",
                type: "binary(32)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LargeSha256",
                table: "ProductImages");

            migrationBuilder.DropColumn(
                name: "MediumSha256",
                table: "ProductImages");

            migrationBuilder.DropColumn(
                name: "SmallSha256",
                table: "ProductImages");
        }
    }
}
