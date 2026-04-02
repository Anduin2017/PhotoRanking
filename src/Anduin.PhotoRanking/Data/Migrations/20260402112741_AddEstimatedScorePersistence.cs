using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anduin.PhotoRanking.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimatedScorePersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "EstimatedScore",
                table: "Photos",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EstimatedScoreUpdatedAt",
                table: "Photos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SystemStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LastRatingAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastGlobalScoringAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemStates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemStates");

            migrationBuilder.DropColumn(
                name: "EstimatedScore",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "EstimatedScoreUpdatedAt",
                table: "Photos");
        }
    }
}
