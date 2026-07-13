using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSyntheticBenchmark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MinQualityDelta",
                table: "RefinementPolicies",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "SyntheticSampleSize",
                table: "RefinementPolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RefinementCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DraftVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActiveVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ActiveScore = table.Column<double>(type: "REAL", nullable: true),
                    CandidateScore = table.Column<double>(type: "REAL", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefinementCandidates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefinementCandidates_ActiveVersionId",
                table: "RefinementCandidates",
                column: "ActiveVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RefinementCandidates_DraftVersionId",
                table: "RefinementCandidates",
                column: "DraftVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefinementCandidates");

            migrationBuilder.DropColumn(
                name: "MinQualityDelta",
                table: "RefinementPolicies");

            migrationBuilder.DropColumn(
                name: "SyntheticSampleSize",
                table: "RefinementPolicies");
        }
    }
}
