using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixAnalyticsViews2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //migrationBuilder.CreateTable(
            //    name: "UserDashboardSummaryReportAnalyticsEntity",
            //    columns: table => new
            //    {
            //        SuccessRate = table.Column<double>(type: "double precision", nullable: true),
            //        TotalDeliveries = table.Column<int>(type: "integer", nullable: false),
            //        ActiveSubscriptions = table.Column<int>(type: "integer", nullable: false),
            //        ActiveSubscribedCatalogs = table.Column<int>(type: "integer", nullable: false),
            //        PendingDeliveries = table.Column<int>(type: "integer", nullable: false),
            //        PendingDeliveriesToday = table.Column<int>(type: "integer", nullable: false),
            //        TotalSubscription = table.Column<int>(type: "integer", nullable: false),
            //        TotalDeadLetter = table.Column<int>(type: "integer", nullable: false),
            //        DeadLetterRetriedToday = table.Column<int>(type: "integer", nullable: false)
            //    },
            //    constraints: table =>
            //    {
            //    });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDashboardSummaryReportAnalyticsEntity");
        }
    }
}
