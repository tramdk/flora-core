using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FloraCore.Migrations
{
    /// <inheritdoc />
    public partial class AddContentConfigToWebsiteInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentConfig",
                table: "WebsiteInfos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentConfig",
                table: "WebsiteInfos");
        }
    }
}
