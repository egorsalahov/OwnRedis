using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OwnRedis.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOriginalTTL : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "OriginalTTLSeconds",
                table: "CacheItems",
                type: "float",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalTTLSeconds",
                table: "CacheItems");
        }
    }
}
