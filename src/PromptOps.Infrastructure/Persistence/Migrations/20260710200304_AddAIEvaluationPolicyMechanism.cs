using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAIEvaluationPolicyMechanism : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mechanism",
                table: "AIEvaluationPolicies",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "Daemon");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mechanism",
                table: "AIEvaluationPolicies");
        }
    }
}
