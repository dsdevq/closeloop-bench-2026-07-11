using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRottingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RottingThresholdDays",
                table: "pipelines");

            // Remove notifications whose trigger values are now retired (DealRotting=2, TaskDue=5).
            migrationBuilder.Sql(
                "DELETE FROM notifications WHERE \"Trigger\" IN (2, 5);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RottingThresholdDays",
                table: "pipelines",
                type: "integer",
                nullable: true);
        }
    }
}
