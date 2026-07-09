using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHumanEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HumanEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EvaluatorId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Correctness = table.Column<int>(type: "INTEGER", nullable: false),
                    Helpfulness = table.Column<int>(type: "INTEGER", nullable: false),
                    Architecture = table.Column<int>(type: "INTEGER", nullable: false),
                    Readability = table.Column<int>(type: "INTEGER", nullable: false),
                    Completeness = table.Column<int>(type: "INTEGER", nullable: false),
                    Hallucinations = table.Column<bool>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<int>(type: "INTEGER", nullable: false),
                    OverallSatisfaction = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HumanEvaluations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HumanEvaluations_ExecutionId",
                table: "HumanEvaluations",
                column: "ExecutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HumanEvaluations");
        }
    }
}
