using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminDashboardSummaryView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql
            (
                @"
                    CREATE OR REPLACE VIEW ""vw_AdminDashboardSummary"" 
                    AS
                        SELECT ""ws"".""SubscriptionCount"", ""wec"".""ActiveCatalogs"", ""wsc"".""TotalServiceClients"", ""anu"".""TotalOnboardedUsers"", 
                        ""wd"".""TotalPendingDeliveries"", ""wdl"".""TotalPendingDeadLetter"", ""wes"".""TotalRaisedEvent"", ""wdSub"".""SuccessRate""
                        FROM (SELECT COUNT(*) AS ""SubscriptionCount"" FROM ""WebhookSubscriptions"" WHERE ""IsActive"" = true) AS ""ws""
                        CROSS JOIN (SELECT COUNT(*) AS ""ActiveCatalogs"" FROM ""WebHookEventCatalogs"" WHERE ""IsActive"" = true) AS ""wec""
                        CROSS JOIN (SELECT COUNT(*) AS ""TotalServiceClients"" FROM ""WebhookServiceClients"" WHERE ""IsActive"" = true) AS ""wsc""
                        CROSS JOIN (SELECT COUNT(*) AS ""TotalOnboardedUsers"" FROM ""AspNetUsers"" WHERE ""IsActive"" = true) AS ""anu""
                        CROSS JOIN (SELECT COUNT(*) AS ""TotalPendingDeliveries"" FROM ""WebhookDeliveries"" WHERE ""DeliveryStatus"" = 'Pending') AS ""wd""
                        CROSS JOIN (SELECT COUNT(*) AS ""TotalPendingDeadLetter"" FROM ""WebhookDeadLetterQueues"" WHERE ""RetriedAt"" IS NULL) AS ""wdl""
                        CROSS JOIN (SELECT COUNT(*) AS ""TotalRaisedEvent"" FROM ""WebhookEvents"" WHERE ""CreatedAt"" >= CURRENT_DATE AND  ""CreatedAt"" < CURRENT_DATE + INTERVAL '1 day') AS ""wes""
                        CROSS JOIN (
	                        SELECT
                            COUNT(*) FILTER (WHERE ""wda"".""HttpResponseCode"" NOT LIKE '2%')::int AS ""FailedCount"",
                            COUNT(*) FILTER (WHERE ""wda"".""HttpResponseCode"" LIKE '2%')::int AS ""SuccessCount"",
                            COUNT(*)::int  AS ""TotalCount"",
                            ROUND(
                                COUNT(*) FILTER (WHERE ""wda"".""HttpResponseCode"" LIKE '2%')::numeric
                                / NULLIF(COUNT(*), 0) * 100
                            , 2) AS ""SuccessRate""
                        FROM ""WebhookDeliveryAttempts"" AS ""wda""
                        ) AS ""wdSub"";
                "
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP VIEW IF EXISTS ""vw_AdminDashboardSummary""");
        }
    }
}
