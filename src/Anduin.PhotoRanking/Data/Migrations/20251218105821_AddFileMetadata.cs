using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anduin.PhotoRanking.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "Photos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModified",
                table: "Photos",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "Photos");
        }
    }
}
