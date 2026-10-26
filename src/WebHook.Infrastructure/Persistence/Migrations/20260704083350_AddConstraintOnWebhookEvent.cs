using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConstraintOnWebhookEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Webhook_Event_CorrelationId",
                table: "WebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_Webhook_Event_EventType",
                table: "WebhookEvents");

            migrationBuilder.CreateIndex(
                name: "IX_Webhook_Event_CorrelationId_EventType",
                table: "WebhookEvents",
                columns: new[] { "CorrelationId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_Webhook_Event_CreatedAt",
                table: "WebhookEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Webhook_Event_ProcessedAt",
                table: "WebhookEvents",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Webhook_Event_Status",
                table: "WebhookEvents",
                column: "Status");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebhookEvents_ProcessedAt_After_CreatedAt",
                table: "WebhookEvents",
                sql: "\"ProcessedAt\" IS NULL OR \"ProcessedAt\" >= \"CreatedAt\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebhookEvents_Status",
                table: "WebhookEvents",
                sql: "\"Status\" IN ('Pending', 'Processing', 'Processed', 'PartiallyProcessed' , 'Failed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Webhook_Event_CorrelationId_EventType",
                table: "WebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_Webhook_Event_CreatedAt",
                table: "WebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_Webhook_Event_ProcessedAt",
                table: "WebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_Webhook_Event_Status",
                table: "WebhookEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WebhookEvents_ProcessedAt_After_CreatedAt",
                table: "WebhookEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WebhookEvents_Status",
                table: "WebhookEvents");

            migrationBuilder.CreateIndex(
                name: "IX_Webhook_Event_CorrelationId",
                table: "WebhookEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Webhook_Event_EventType",
                table: "WebhookEvents",
                column: "EventType");
        }
    }
}
