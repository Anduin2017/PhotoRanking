using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anduin.PhotoRanking.Data.Migrations
{
    /// <inheritdoc />
    public partial class PhotoSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SummaryPrompt1",
                table: "SystemStates",
                type: "TEXT",
                maxLength: 8000,
                nullable: false,
                defaultValue: "Describe what is visible in this image for a blind person.");

            migrationBuilder.AddColumn<string>(
                name: "SummaryPrompt2",
                table: "SystemStates",
                type: "TEXT",
                maxLength: 8000,
                nullable: false,
                defaultValue: "What does this photograph express? Discuss its composition, mood and quality. Distinguish observation from interpretation.");

            migrationBuilder.AddColumn<string>(
                name: "SummaryPrompt3",
                table: "SystemStates",
                type: "TEXT",
                maxLength: 8000,
                nullable: false,
                defaultValue: "Write a poem in the voice of a person or subject in this photograph. Treat their thoughts as creative imagination. Return only the poem.");

            migrationBuilder.AddColumn<DateTime>(
                name: "SummaryPromptsModifiedAt",
                table: "SystemStates",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<double>(
                name: "SummaryTopPercent",
                table: "SystemStates",
                type: "REAL",
                nullable: false,
                defaultValue: 3.0);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Photos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SummaryGeneratedAt",
                table: "Photos",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SummaryPrompt1",
                table: "SystemStates");

            migrationBuilder.DropColumn(
                name: "SummaryPrompt2",
                table: "SystemStates");

            migrationBuilder.DropColumn(
                name: "SummaryPrompt3",
                table: "SystemStates");

            migrationBuilder.DropColumn(
                name: "SummaryPromptsModifiedAt",
                table: "SystemStates");

            migrationBuilder.DropColumn(
                name: "SummaryTopPercent",
                table: "SystemStates");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "SummaryGeneratedAt",
                table: "Photos");
        }
    }
}
