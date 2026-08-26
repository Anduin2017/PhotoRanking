using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anduin.PhotoRanking.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersonalScoringV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCorrection",
                table: "RatingLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "PredictionAtRating",
                table: "RatingLogs",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PredictionModelVersion",
                table: "RatingLogs",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PreviousScore",
                table: "RatingLogs",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstimatedScoreModelVersion",
                table: "Photos",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PredictionNovelty",
                table: "Photos",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PredictionUncertainty",
                table: "Photos",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AverageManualScore",
                table: "Albums",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RatedPhotoCount",
                table: "Albums",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PredictionModels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EmbeddingModel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ModelData = table.Column<byte[]>(type: "BLOB", nullable: false),
                    TrainedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TrainingRatingWatermark = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TrainingPhotoCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TrainingCandidatePhotoCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CoverageTrainingPhotoCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EnsembleSize = table.Column<int>(type: "INTEGER", nullable: false),
                    CoverageCentroidCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ValidationMeanAbsoluteError = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictionModels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Photos_EstimatedScore",
                table: "Photos",
                column: "EstimatedScore");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_IndependentScore",
                table: "Photos",
                column: "IndependentScore");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_PredictionNovelty",
                table: "Photos",
                column: "PredictionNovelty");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_PredictionUncertainty",
                table: "Photos",
                column: "PredictionUncertainty");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PredictionModels");

            migrationBuilder.DropIndex(
                name: "IX_Photos_EstimatedScore",
                table: "Photos");

            migrationBuilder.DropIndex(
                name: "IX_Photos_IndependentScore",
                table: "Photos");

            migrationBuilder.DropIndex(
                name: "IX_Photos_PredictionNovelty",
                table: "Photos");

            migrationBuilder.DropIndex(
                name: "IX_Photos_PredictionUncertainty",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "IsCorrection",
                table: "RatingLogs");

            migrationBuilder.DropColumn(
                name: "PredictionAtRating",
                table: "RatingLogs");

            migrationBuilder.DropColumn(
                name: "PredictionModelVersion",
                table: "RatingLogs");

            migrationBuilder.DropColumn(
                name: "PreviousScore",
                table: "RatingLogs");

            migrationBuilder.DropColumn(
                name: "EstimatedScoreModelVersion",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "PredictionNovelty",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "PredictionUncertainty",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "AverageManualScore",
                table: "Albums");

            migrationBuilder.DropColumn(
                name: "RatedPhotoCount",
                table: "Albums");
        }
    }
}
