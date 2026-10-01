using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateEventAnalyticsView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP VIEW IF EXISTS vw_GetMostRecentEvents");
        }
    }
}
