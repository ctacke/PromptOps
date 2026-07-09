using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromptScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromptVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScoringConfigId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    OverallScore = table.Column<double>(type: "REAL", nullable: false),
                    ComponentScores = table.Column<string>(type: "TEXT", nullable: false),
                    SampleSize = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptScores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScoringConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    WeightHumanRating = table.Column<double>(type: "REAL", nullable: false),
                    WeightSonar = table.Column<double>(type: "REAL", nullable: false),
                    WeightTests = table.Column<double>(type: "REAL", nullable: false),
                    WeightBuild = table.Column<double>(type: "REAL", nullable: false),
                    WeightAcceptanceCriteria = table.Column<double>(type: "REAL", nullable: false),
                    WeightManualFixes = table.Column<double>(type: "REAL", nullable: false),
                    WeightReviewComments = table.Column<double>(type: "REAL", nullable: false),
                    WeightRegressionBugs = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoringConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromptScores_PromptVersionId",
                table: "PromptScores",
                column: "PromptVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoringConfigs_Name_Version",
                table: "ScoringConfigs",
                columns: new[] { "Name", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromptScores");

            migrationBuilder.DropTable(
                name: "ScoringConfigs");
        }
    }
}
