using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WebHook.Infrastructure.Data_Persistence;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Fixes the counts and ordering of the dashboard analytics views. Column names, order and types are
    /// unchanged, so the keyless entities and the model snapshot are not affected.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>vw_GetEventCatalogAnalytics</c> joined events and subscriptions in one query, multiplying
    ///     <c>TotalRaisedEvents</c> by the number of subscriptions. Each count is now aggregated separately,
    ///     and <c>ActiveSubscriptions</c> counts only active links on active subscriptions. Catalogs with no
    ///     raised events are excluded.
    ///   </description></item>
    ///   <item><description>
    ///     <c>vw_getmostrecentevents</c> grouped by delivery (so <c>DeliveryCount</c> was always 1) and sorted
    ///     oldest first. It now returns the five newest events that have deliveries, one row per event, with
    ///     their delivery counts.
    ///   </description></item>
    /// </list>
    /// </remarks>
    [DbContext(typeof(RepositoryContext))]
    [Migration("20261025120000_FixAnalyticsViews")]
    public partial class FixAnalyticsViews : Migration
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

            migrationBuilder.Sql(@"
                CREATE OR REPLACE VIEW vw_GetMostRecentEvents AS
                SELECT ""we"".""CreatedAt"", ""we"".""CorrelationId"", ""we"".""EventType"", ""we"".""Source"", COUNT(""wd"".""Id"") AS ""DeliveryCount""
                FROM ""WebhookEvents"" AS ""we""
                INNER JOIN ""WebhookDeliveries"" AS ""wd""
                    ON ""wd"".""WebhookEventId"" = ""we"".""Id""
                GROUP BY ""we"".""Id"", ""we"".""CreatedAt"", ""we"".""CorrelationId"", ""we"".""EventType"", ""we"".""Source""
                ORDER BY ""we"".""CreatedAt"" DESC
                LIMIT 5;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the definitions from TopEventCatalogAnalytics and CreateEventAnalyticsView.
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

            migrationBuilder.Sql(@"
                CREATE OR REPLACE VIEW vw_GetMostRecentEvents AS
                SELECT ""we"".""CreatedAt"", ""we"".""CorrelationId"", ""we"".""EventType"", ""we"".""Source"", COUNT(""wd"".""Id"") AS ""DeliveryCount""
                FROM ""WebhookDeliveries"" AS ""wd""
                INNER JOIN ""WebhookEvents"" AS ""we""
                ON ""wd"".""WebhookEventId"" = ""we"".""Id""
                GROUP BY ""wd"".""Id"",""we"".""CreatedAt"",""we"".""CorrelationId"", ""we"".""EventType"", ""we"".""Source""
                ORDER BY ""we"".""CreatedAt""
                LIMIT 5;
            ");
        }
    }
}
