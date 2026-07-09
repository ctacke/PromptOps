using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Prompts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromptMetadata",
                columns: table => new
                {
                    PromptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Categories = table.Column<string>(type: "TEXT", nullable: false),
                    Owners = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalRefs = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptMetadata", x => x.PromptId);
                    table.ForeignKey(
                        name: "FK_PromptMetadata_Prompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "Prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParentVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChangelogEntry = table.Column<string>(type: "TEXT", nullable: true),
                    TemplateVariables = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptVersions_Prompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "Prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptDependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromptVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetPromptVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Relationship = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptDependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptDependencies_PromptVersions_PromptVersionId",
                        column: x => x.PromptVersionId,
                        principalTable: "PromptVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromptDependencies_PromptVersionId",
                table: "PromptDependencies",
                column: "PromptVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptVersions_PromptId_VersionNumber",
                table: "PromptVersions",
                columns: new[] { "PromptId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromptDependencies");

            migrationBuilder.DropTable(
                name: "PromptMetadata");

            migrationBuilder.DropTable(
                name: "PromptVersions");

            migrationBuilder.DropTable(
                name: "Prompts");
        }
    }
}
