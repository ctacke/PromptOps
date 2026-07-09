using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAIEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AIEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JudgeProviderId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    JudgeModel = table.Column<string>(type: "TEXT", nullable: true),
                    SatisfiesAcceptanceCriteria = table.Column<bool>(type: "INTEGER", nullable: true),
                    AdrViolations = table.Column<string>(type: "TEXT", nullable: false),
                    IgnoredRequirements = table.Column<string>(type: "TEXT", nullable: false),
                    UnnecessaryComplexityNotes = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestedPromptImprovements = table.Column<string>(type: "TEXT", nullable: false),
                    RawResponse = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIEvaluations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AIEvaluations_ExecutionId",
                table: "AIEvaluations",
                column: "ExecutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AIEvaluations");
        }
    }
}
