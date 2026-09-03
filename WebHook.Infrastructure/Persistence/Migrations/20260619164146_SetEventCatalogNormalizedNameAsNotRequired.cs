using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SetEventCatalogNormalizedNameAsNotRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEventName",
                table: "WebHookEventCatalogs",
                type: "text",
                nullable: true,
                computedColumnSql: "UPPER(\"EventName\")",
                stored: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldComputedColumnSql: "UPPER(\"EventName\")",
                oldStored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEventName",
                table: "WebHookEventCatalogs",
                type: "text",
                nullable: false,
                computedColumnSql: "UPPER(\"EventName\")",
                stored: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComputedColumnSql: "UPPER(\"EventName\")",
                oldStored: true);
        }
    }
}
