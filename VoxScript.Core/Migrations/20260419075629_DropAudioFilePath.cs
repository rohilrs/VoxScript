using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxScript.Core.Migrations
{
    /// <inheritdoc />
    public partial class DropAudioFilePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioFilePath",
                table: "Transcriptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudioFilePath",
                table: "Transcriptions",
                type: "TEXT",
                nullable: true);
        }
    }
}
