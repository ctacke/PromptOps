using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEngineeringMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EngineeringMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CollectedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    BuildSuccess = table.Column<bool>(type: "INTEGER", nullable: true),
                    TestSuccess = table.Column<bool>(type: "INTEGER", nullable: true),
                    Coverage = table.Column<double>(type: "REAL", nullable: true),
                    SonarIssues = table.Column<int>(type: "INTEGER", nullable: true),
                    Warnings = table.Column<int>(type: "INTEGER", nullable: true),
                    CodeSmells = table.Column<int>(type: "INTEGER", nullable: true),
                    SecurityFindings = table.Column<int>(type: "INTEGER", nullable: true),
                    Duplication = table.Column<double>(type: "REAL", nullable: true),
                    CyclomaticComplexity = table.Column<double>(type: "REAL", nullable: true),
                    ReviewComments = table.Column<int>(type: "INTEGER", nullable: true),
                    ReviewIterations = table.Column<int>(type: "INTEGER", nullable: true),
                    MergeTimeMinutes = table.Column<double>(type: "REAL", nullable: true),
                    RollbackNeeded = table.Column<bool>(type: "INTEGER", nullable: true),
                    ManualEdits = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringMetrics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringMetrics_ExecutionId",
                table: "EngineeringMetrics",
                column: "ExecutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EngineeringMetrics");
        }
    }
}
