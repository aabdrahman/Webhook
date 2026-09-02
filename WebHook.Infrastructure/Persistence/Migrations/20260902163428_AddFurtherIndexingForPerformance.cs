using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFurtherIndexingForPerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Webhook_Event_CorrelationId_EventType",
                table: "WebhookEvents");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookServiceClientEventCatalogs_DeactivatedAt",
                table: "WebhookServiceClientEventCatalogs",
                column: "DeactivatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Webhook_Event_CorrelationId_EventType",
                table: "WebhookEvents",
                columns: new[] { "CorrelationId", "EventType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_CorrelationId",
                table: "WebhookEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_EventType",
                table: "WebhookEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_OtpVerifications_ConsumedAt",
                table: "OtpVerifications",
                column: "ConsumedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OtpVerifications_Purpose",
                table: "OtpVerifications",
                column: "Purpose");

            migrationBuilder.CreateIndex(
                name: "IX_OtpVerifications_ValidatedAt",
                table: "OtpVerifications",
                column: "ValidatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OtpOperationTokens_ConsumedAt",
                table: "OtpOperationTokens",
                column: "ConsumedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookServiceClientEventCatalogs_DeactivatedAt",
                table: "WebhookServiceClientEventCatalogs");

            migrationBuilder.DropIndex(
                name: "IX_Webhook_Event_CorrelationId_EventType",
                table: "WebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_WebhookEvents_CorrelationId",
                table: "WebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_WebhookEvents_EventType",
                table: "WebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_OtpVerifications_ConsumedAt",
                table: "OtpVerifications");

            migrationBuilder.DropIndex(
                name: "IX_OtpVerifications_Purpose",
                table: "OtpVerifications");

            migrationBuilder.DropIndex(
                name: "IX_OtpVerifications_ValidatedAt",
                table: "OtpVerifications");

            migrationBuilder.DropIndex(
                name: "IX_OtpOperationTokens_ConsumedAt",
                table: "OtpOperationTokens");

            migrationBuilder.CreateIndex(
                name: "IX_Webhook_Event_CorrelationId_EventType",
                table: "WebhookEvents",
                columns: new[] { "CorrelationId", "EventType" });
        }
    }
}
