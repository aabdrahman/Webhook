using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TopEventCatalogAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE VIEW ""vw_GetEventCatalogAnalytics""
                AS
                SELECT ""wec"".""Id"", ""wec"".""NormalizedEventName"", COUNT(""we"".""Id"") AS ""TotalRaisedEvents"", COUNT(DISTINCT ""wes"".""Id"") AS ""ActiveSubscriptions"", MAX(""we"".""CreatedAt"") AS ""LastRaised""
                FROM ""WebHookEventCatalogs"" AS ""wec""
                LEFT JOIN ""WebhookEvents"" AS ""we""
                    ON ""wec"".""NormalizedEventName"" = ""we"".""EventType""
                LEFT JOIN ""WebhookEventSubscriptions"" AS ""wes""
                    ON ""wec"".""Id"" = ""wes"".""WebhookEventCatalogId""
                WHERE ""wec"".""IsActive"" = true
                GROUP BY ""wec"".""Id"", ""wec"".""NormalizedEventName""
                ORDER BY ""TotalRaisedEvents"" DESC
                LIMIT 5;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP VIEW IF EXISTS ""vw_GetEventCatalogAnalytics""");
        }
    }
}
