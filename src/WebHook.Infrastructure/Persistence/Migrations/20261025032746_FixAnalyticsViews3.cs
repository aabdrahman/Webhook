using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixAnalyticsViews3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE VIEW ""vw_GetEventCatalogAnalytics""
                    AS
                    SELECT ""wec"".""Id"",
                           ""wec"".""NormalizedEventName"",
                           ""ev"".""TotalRaisedEvents"",
                           COALESCE(""sub"".""ActiveSubscriptions"", 0) AS ""ActiveSubscriptions"",
                           ""ev"".""LastRaised""
                    FROM ""WebHookEventCatalogs"" AS ""wec""
                    INNER JOIN (
                        SELECT ""we"".""EventType"",
                               COUNT(*)              AS ""TotalRaisedEvents"",
                               MAX(""we"".""CreatedAt"") AS ""LastRaised""
                        FROM ""WebhookEvents"" AS ""we""
                        GROUP BY ""we"".""EventType""
                    ) AS ""ev"" ON ""ev"".""EventType"" = ""wec"".""NormalizedEventName""
                    INNER JOIN (
                        SELECT ""wes"".""WebhookEventCatalogId"",
                               COUNT(*) AS ""ActiveSubscriptions""
                        FROM ""WebhookEventSubscriptions"" AS ""wes""
                        INNER JOIN ""WebhookSubscriptions"" AS ""ws"" ON ""ws"".""Id"" = ""wes"".""WebhookSubscriptionId""
                        WHERE ""wes"".""IsActive"" = true
                          AND ""ws"".""IsActive"" = true
                        GROUP BY ""wes"".""WebhookEventCatalogId""
                    ) AS ""sub"" ON ""sub"".""WebhookEventCatalogId"" = ""wec"".""Id""
                    WHERE ""wec"".""IsActive"" = true
                    ORDER BY ""TotalRaisedEvents"" DESC, ""wec"".""NormalizedEventName""
                    LIMIT 5;

            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE VIEW ""vw_GetEventCatalogAnalytics""
                AS
                SELECT ""wec"".""Id"",
                       ""wec"".""NormalizedEventName"",
                       ""ev"".""TotalRaisedEvents"",
                       COALESCE(""sub"".""ActiveSubscriptions"", 0) AS ""ActiveSubscriptions"",
                       ""ev"".""LastRaised""
                FROM ""WebHookEventCatalogs"" AS ""wec""
                INNER JOIN (
                    SELECT ""we"".""EventType"",
                           COUNT(*)              AS ""TotalRaisedEvents"",
                           MAX(""we"".""CreatedAt"") AS ""LastRaised""
                    FROM ""WebhookEvents"" AS ""we""
                    GROUP BY ""we"".""EventType""
                ) AS ""ev"" ON ""ev"".""EventType"" = ""wec"".""NormalizedEventName""
                LEFT JOIN (
                    SELECT ""wes"".""WebhookEventCatalogId"",
                           COUNT(*) AS ""ActiveSubscriptions""
                    FROM ""WebhookEventSubscriptions"" AS ""wes""
                    INNER JOIN ""WebhookSubscriptions"" AS ""ws"" ON ""ws"".""Id"" = ""wes"".""WebhookSubscriptionId""
                    WHERE ""wes"".""IsActive"" = true
                      AND ""ws"".""IsActive"" = true
                    GROUP BY ""wes"".""WebhookEventCatalogId""
                ) AS ""sub"" ON ""sub"".""WebhookEventCatalogId"" = ""wec"".""Id""
                WHERE ""wec"".""IsActive"" = true
                ORDER BY ""TotalRaisedEvents"" DESC, ""wec"".""NormalizedEventName""
                LIMIT 5;
            ");
        }
    }
}
