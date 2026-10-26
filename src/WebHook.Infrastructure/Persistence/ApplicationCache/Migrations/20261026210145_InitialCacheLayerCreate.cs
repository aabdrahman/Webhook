using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCacheLayerCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationCacheLayerEntities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CacheKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CacheValue = table.Column<string>(type: "blob", nullable: false),
                    CreatedAt = table.Column<long>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<long>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationCacheLayerEntities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationCacheLayerEntities_CacheKey",
                table: "ApplicationCacheLayerEntities",
                column: "CacheKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationCacheLayerEntities_ExpiresAt",
                table: "ApplicationCacheLayerEntities",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationCacheLayerEntities");
        }
    }
}
