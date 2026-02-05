using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anduin.PhotoRanking.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "FeatureVector",
                table: "Photos",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeatureVector",
                table: "Photos");
        }
    }
}
