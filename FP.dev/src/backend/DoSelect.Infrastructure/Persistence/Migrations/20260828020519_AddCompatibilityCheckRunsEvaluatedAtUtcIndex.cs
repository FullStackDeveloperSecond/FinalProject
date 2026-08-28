using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoSelect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompatibilityCheckRunsEvaluatedAtUtcIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CompatibilityCheckRuns_EvaluatedAtUtc_Id",
                table: "CompatibilityCheckRuns",
                columns: new[] { "EvaluatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompatibilityCheckRuns_EvaluatedAtUtc_Id",
                table: "CompatibilityCheckRuns");
        }
    }
}
