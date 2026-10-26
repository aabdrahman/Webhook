using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class addFunctionForUserDashboardSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                    CREATE OR REPLACE FUNCTION get_user_dashboard_summary(
                        p_user_id   uuid,
                        p_start_date date DEFAULT CURRENT_DATE,
                        p_end_date   date DEFAULT CURRENT_DATE + INTERVAL '1 day'
                    )
                    RETURNS TABLE (
                        ""SuccessRate""              numeric,
                        ""TotalDeliveries""          bigint,
                        ""ActiveSubscriptions""      bigint,
                        ""ActiveSubscribedCatalogs"" bigint,
                        ""PendingDeliveries""        bigint,
                        ""PendingDeliveriesToday""   bigint,
                        ""TotalSubscription""        bigint,
                        ""TotalDeadLetter""          bigint,
                        ""DeadLetterRetriedToday""   bigint
                    )
                    LANGUAGE sql
                    AS $$
                        WITH userSubs AS (
                            SELECT * FROM ""WebhookSubscriptions""
                            WHERE ""CreatedByUserId"" = p_user_id
                        ),
                        userEventSubscription AS (
                            SELECT * FROM ""WebhookEventSubscriptions""
                            WHERE ""WebhookSubscriptionId"" IN (SELECT ""Id"" FROM userSubs)
                        ),
                        userDeliveries AS (
                            SELECT * FROM ""WebhookDeliveries""
                            WHERE ""WebhookSubscriptionEventId"" IN (SELECT ""Id"" FROM userEventSubscription)
                        ),
                        userDeliveryAttempts AS (
                            SELECT * FROM ""WebhookDeliveryAttempts""
                            WHERE ""WebhookDeliveryId"" IN (SELECT ""Id"" FROM userDeliveries)
                        ),
                        userDeliveryDeadLetter AS (
                            SELECT * FROM ""WebhookDeadLetterQueues""
                            WHERE ""WebhookDeliveryId"" IN (SELECT ""Id"" FROM userDeliveries)
                        )
                        SELECT
                            ""wdSub"".""SuccessRate"",
                            ""wd"".""TotalDeliveries"",
                            ""us"".""ActiveSubscriptions"",
                            ""ues"".""ActiveSubscribedCatalogs"",
                            ""wd"".""PendingDeliveries"",
                            ""wd"".""PendingDeliveriesToday"",
                            ""userSub"".""TotalSubscription"",
                            ""wdd"".""TotalDeadLetter"",
                            ""wdd"".""DeadLetterRetriedToday""
                        FROM
                            (SELECT COUNT(*) AS ""ActiveSubscriptions"" FROM userSubs WHERE ""IsActive"" = true) AS ""us""
                        CROSS JOIN
                            (SELECT COUNT(*) AS ""TotalSubscription"" FROM userSubs) AS ""userSub""
                        CROSS JOIN
                            (SELECT COUNT(DISTINCT ""WebhookEventCatalogId"") AS ""ActiveSubscribedCatalogs""
                             FROM userEventSubscription WHERE ""IsActive"" = true) AS ""ues""
                        CROSS JOIN
                            (SELECT
                                 COUNT(*) AS ""TotalDeliveries"",
                                 COUNT(*) FILTER (WHERE ""DeliveryStatus"" = 'Pending') AS ""PendingDeliveries"",
                                 COUNT(*) FILTER (WHERE ""DeliveryStatus"" = 'Pending'
                                                    AND ""CreatedAt"" >= p_start_date
                                                    AND ""CreatedAt"" <  p_end_date) AS ""PendingDeliveriesToday""
                             FROM userDeliveries) AS ""wd""
                        CROSS JOIN
                            (SELECT
                                 COUNT(*) AS ""TotalDeadLetter"",
                                 COUNT(*) FILTER (WHERE ""RetriedAt"" >= p_start_date
                                                    AND ""RetriedAt"" < p_end_date) AS ""DeadLetterRetriedToday""
                             FROM userDeliveryDeadLetter) AS ""wdd""
                        CROSS JOIN
                            (SELECT
                                 ROUND(
                                     COUNT(*) FILTER (WHERE ""HttpResponseCode"" LIKE '2%')::numeric
                                     / NULLIF(COUNT(*), 0) * 100
                                 , 2) AS ""SuccessRate""
                             FROM userDeliveryAttempts) AS ""wdSub"";
                    $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS get_user_dashboard_summary");
        }
    }
}
