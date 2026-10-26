using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.ApplicationCache.Migrations
{
    /// <inheritdoc />
    public partial class AddKeyExpiresAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CacheKey_ExpiresAt",
                table: "ApplicationCacheLayerEntities",
                columns: new[] { "CacheKey", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CacheKey_ExpiresAt",
                table: "ApplicationCacheLayerEntities");
        }
    }
}
