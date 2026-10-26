using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModifyTheReationshipOnTheWebhookSubscriptionEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookEventSubscriptions_WebHookEventCatalogs_WebhookSubsc~",
                table: "WebhookEventSubscriptions");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookEventSubscriptions_WebHookEventCatalogs_WebhookEvent~",
                table: "WebhookEventSubscriptions",
                column: "WebhookEventCatalogId",
                principalTable: "WebHookEventCatalogs",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookEventSubscriptions_WebHookEventCatalogs_WebhookEvent~",
                table: "WebhookEventSubscriptions");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookEventSubscriptions_WebHookEventCatalogs_WebhookSubsc~",
                table: "WebhookEventSubscriptions",
                column: "WebhookSubscriptionId",
                principalTable: "WebHookEventCatalogs",
                principalColumn: "Id");
        }
    }
}
