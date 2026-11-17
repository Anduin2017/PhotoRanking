using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anduin.PhotoRanking.Data.Migrations
{
    /// <inheritdoc />
    public partial class SummaryCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SummaryCheckpointPromptsModifiedAt",
                table: "Photos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SummaryCompletedRounds",
                table: "Photos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SummaryCheckpointPromptsModifiedAt",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "SummaryCompletedRounds",
                table: "Photos");
        }
    }
}
