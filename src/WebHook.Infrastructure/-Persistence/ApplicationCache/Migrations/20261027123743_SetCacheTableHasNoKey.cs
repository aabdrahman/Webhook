using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.ApplicationCache.Migrations
{
    /// <inheritdoc />
    public partial class SetCacheTableHasNoKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ApplicationCacheLayerEntities",
                table: "ApplicationCacheLayerEntities");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "ApplicationCacheLayerEntities");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "ApplicationCacheLayerEntities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ApplicationCacheLayerEntities",
                table: "ApplicationCacheLayerEntities",
                column: "Id");
        }
    }
}
