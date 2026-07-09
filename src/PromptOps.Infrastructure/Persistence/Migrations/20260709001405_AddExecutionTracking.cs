using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Executions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromptVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeveloperId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Repository = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Branch = table.Column<string>(type: "TEXT", nullable: true),
                    Commit = table.Column<string>(type: "TEXT", nullable: true),
                    TaskId = table.Column<string>(type: "TEXT", nullable: true),
                    ReferencedDocuments = table.Column<string>(type: "TEXT", nullable: false),
                    ReferencedADRs = table.Column<string>(type: "TEXT", nullable: false),
                    AcceptanceCriteria = table.Column<string>(type: "TEXT", nullable: false),
                    Inputs = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: true),
                    ExecutionTimeMs = table.Column<long>(type: "INTEGER", nullable: true),
                    AiProviderId = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    ModelParameters = table.Column<string>(type: "TEXT", nullable: true),
                    FilesChanged = table.Column<string>(type: "TEXT", nullable: false),
                    LinesAdded = table.Column<int>(type: "INTEGER", nullable: false),
                    LinesDeleted = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Executions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionToolUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionToolUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionToolUsages_Executions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "Executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionToolUsages_ExecutionId",
                table: "ExecutionToolUsages",
                column: "ExecutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionToolUsages");

            migrationBuilder.DropTable(
                name: "Executions");
        }
    }
}
