using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAbExplorationRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AbExplorationRate",
                table: "RefinementPolicies",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbExplorationRate",
                table: "RefinementPolicies");
        }
    }
}
