using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModifyTableNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeadLetterQueue_WebhookDelivery_WebhookDeliveryId",
                table: "WebhookDeadLetterQueue");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDelivery_WebHookEventCatalog_WebhookEventCatalogId",
                table: "WebhookDelivery");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDelivery_WebhookEvent_WebhookEventId",
                table: "WebhookDelivery");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDelivery_WebhookSubscription_WebhookSubscriptionId",
                table: "WebhookDelivery");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveryAttempt_WebhookDelivery_WebhookDeliveryId",
                table: "WebhookDeliveryAttempt");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookSubscriptionEvent_WebHookEventCatalog_WebhookSubscri~",
                table: "WebhookSubscriptionEvent");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookSubscriptionEvent_WebhookSubscription_WebhookSubscri~",
                table: "WebhookSubscriptionEvent");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookSubscriptionEvent",
                table: "WebhookSubscriptionEvent");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookSubscription",
                table: "WebhookSubscription");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebHookEventCatalog",
                table: "WebHookEventCatalog");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookEvent",
                table: "WebhookEvent");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookDeliveryAttempt",
                table: "WebhookDeliveryAttempt");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookDelivery",
                table: "WebhookDelivery");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookDeadLetterQueue",
                table: "WebhookDeadLetterQueue");

            migrationBuilder.RenameTable(
                name: "WebhookSubscriptionEvent",
                newName: "WebhookEventSubscriptions");

            migrationBuilder.RenameTable(
                name: "WebhookSubscription",
                newName: "WebhookSubscriptions");

            migrationBuilder.RenameTable(
                name: "WebHookEventCatalog",
                newName: "WebHookEventCatalogs");

            migrationBuilder.RenameTable(
                name: "WebhookEvent",
                newName: "WebhookEvents");

            migrationBuilder.RenameTable(
                name: "WebhookDeliveryAttempt",
                newName: "WebhookDeliveryAttempts");

            migrationBuilder.RenameTable(
                name: "WebhookDelivery",
                newName: "WebhookDeliveries");

            migrationBuilder.RenameTable(
                name: "WebhookDeadLetterQueue",
                newName: "WebhookDeadLetterQueues");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookSubscriptionEvent_WebhookSubscriptionId",
                table: "WebhookEventSubscriptions",
                newName: "IX_WebhookEventSubscriptions_WebhookSubscriptionId");

            migrationBuilder.RenameIndex(
                name: "IX_WebHookEventCatalog_NormalizedEventName",
                table: "WebHookEventCatalogs",
                newName: "IX_WebHookEventCatalogs_NormalizedEventName");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDeliveryAttempt_WebhookDeliveryId",
                table: "WebhookDeliveryAttempts",
                newName: "IX_WebhookDeliveryAttempts_WebhookDeliveryId");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDelivery_WebhookSubscriptionId",
                table: "WebhookDeliveries",
                newName: "IX_WebhookDeliveries_WebhookSubscriptionId");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDelivery_WebhookEventId",
                table: "WebhookDeliveries",
                newName: "IX_WebhookDeliveries_WebhookEventId");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDelivery_WebhookEventCatalogId",
                table: "WebhookDeliveries",
                newName: "IX_WebhookDeliveries_WebhookEventCatalogId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookEventSubscriptions",
                table: "WebhookEventSubscriptions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookSubscriptions",
                table: "WebhookSubscriptions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebHookEventCatalogs",
                table: "WebHookEventCatalogs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookEvents",
                table: "WebhookEvents",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookDeliveryAttempts",
                table: "WebhookDeliveryAttempts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookDeliveries",
                table: "WebhookDeliveries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookDeadLetterQueues",
                table: "WebhookDeadLetterQueues",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeadLetterQueues_WebhookDeliveries_WebhookDeliveryId",
                table: "WebhookDeadLetterQueues",
                column: "WebhookDeliveryId",
                principalTable: "WebhookDeliveries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveries_WebHookEventCatalogs_WebhookEventCatalogId",
                table: "WebhookDeliveries",
                column: "WebhookEventCatalogId",
                principalTable: "WebHookEventCatalogs",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveries_WebhookEvents_WebhookEventId",
                table: "WebhookDeliveries",
                column: "WebhookEventId",
                principalTable: "WebhookEvents",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveries_WebhookSubscriptions_WebhookSubscriptionId",
                table: "WebhookDeliveries",
                column: "WebhookSubscriptionId",
                principalTable: "WebhookSubscriptions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveryAttempts_WebhookDeliveries_WebhookDeliveryId",
                table: "WebhookDeliveryAttempts",
                column: "WebhookDeliveryId",
                principalTable: "WebhookDeliveries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookEventSubscriptions_WebHookEventCatalogs_WebhookSubsc~",
                table: "WebhookEventSubscriptions",
                column: "WebhookSubscriptionId",
                principalTable: "WebHookEventCatalogs",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookEventSubscriptions_WebhookSubscriptions_WebhookSubsc~",
                table: "WebhookEventSubscriptions",
                column: "WebhookSubscriptionId",
                principalTable: "WebhookSubscriptions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeadLetterQueues_WebhookDeliveries_WebhookDeliveryId",
                table: "WebhookDeadLetterQueues");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveries_WebHookEventCatalogs_WebhookEventCatalogId",
                table: "WebhookDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveries_WebhookEvents_WebhookEventId",
                table: "WebhookDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveries_WebhookSubscriptions_WebhookSubscriptionId",
                table: "WebhookDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveryAttempts_WebhookDeliveries_WebhookDeliveryId",
                table: "WebhookDeliveryAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookEventSubscriptions_WebHookEventCatalogs_WebhookSubsc~",
                table: "WebhookEventSubscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookEventSubscriptions_WebhookSubscriptions_WebhookSubsc~",
                table: "WebhookEventSubscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookSubscriptions",
                table: "WebhookSubscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookEventSubscriptions",
                table: "WebhookEventSubscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookEvents",
                table: "WebhookEvents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebHookEventCatalogs",
                table: "WebHookEventCatalogs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookDeliveryAttempts",
                table: "WebhookDeliveryAttempts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookDeliveries",
                table: "WebhookDeliveries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookDeadLetterQueues",
                table: "WebhookDeadLetterQueues");

            migrationBuilder.RenameTable(
                name: "WebhookSubscriptions",
                newName: "WebhookSubscription");

            migrationBuilder.RenameTable(
                name: "WebhookEventSubscriptions",
                newName: "WebhookSubscriptionEvent");

            migrationBuilder.RenameTable(
                name: "WebhookEvents",
                newName: "WebhookEvent");

            migrationBuilder.RenameTable(
                name: "WebHookEventCatalogs",
                newName: "WebHookEventCatalog");

            migrationBuilder.RenameTable(
                name: "WebhookDeliveryAttempts",
                newName: "WebhookDeliveryAttempt");

            migrationBuilder.RenameTable(
                name: "WebhookDeliveries",
                newName: "WebhookDelivery");

            migrationBuilder.RenameTable(
                name: "WebhookDeadLetterQueues",
                newName: "WebhookDeadLetterQueue");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookEventSubscriptions_WebhookSubscriptionId",
                table: "WebhookSubscriptionEvent",
                newName: "IX_WebhookSubscriptionEvent_WebhookSubscriptionId");

            migrationBuilder.RenameIndex(
                name: "IX_WebHookEventCatalogs_NormalizedEventName",
                table: "WebHookEventCatalog",
                newName: "IX_WebHookEventCatalog_NormalizedEventName");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDeliveryAttempts_WebhookDeliveryId",
                table: "WebhookDeliveryAttempt",
                newName: "IX_WebhookDeliveryAttempt_WebhookDeliveryId");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDeliveries_WebhookSubscriptionId",
                table: "WebhookDelivery",
                newName: "IX_WebhookDelivery_WebhookSubscriptionId");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDeliveries_WebhookEventId",
                table: "WebhookDelivery",
                newName: "IX_WebhookDelivery_WebhookEventId");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDeliveries_WebhookEventCatalogId",
                table: "WebhookDelivery",
                newName: "IX_WebhookDelivery_WebhookEventCatalogId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookSubscription",
                table: "WebhookSubscription",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookSubscriptionEvent",
                table: "WebhookSubscriptionEvent",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookEvent",
                table: "WebhookEvent",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebHookEventCatalog",
                table: "WebHookEventCatalog",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookDeliveryAttempt",
                table: "WebhookDeliveryAttempt",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookDelivery",
                table: "WebhookDelivery",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookDeadLetterQueue",
                table: "WebhookDeadLetterQueue",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeadLetterQueue_WebhookDelivery_WebhookDeliveryId",
                table: "WebhookDeadLetterQueue",
                column: "WebhookDeliveryId",
                principalTable: "WebhookDelivery",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDelivery_WebHookEventCatalog_WebhookEventCatalogId",
                table: "WebhookDelivery",
                column: "WebhookEventCatalogId",
                principalTable: "WebHookEventCatalog",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDelivery_WebhookEvent_WebhookEventId",
                table: "WebhookDelivery",
                column: "WebhookEventId",
                principalTable: "WebhookEvent",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDelivery_WebhookSubscription_WebhookSubscriptionId",
                table: "WebhookDelivery",
                column: "WebhookSubscriptionId",
                principalTable: "WebhookSubscription",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveryAttempt_WebhookDelivery_WebhookDeliveryId",
                table: "WebhookDeliveryAttempt",
                column: "WebhookDeliveryId",
                principalTable: "WebhookDelivery",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookSubscriptionEvent_WebHookEventCatalog_WebhookSubscri~",
                table: "WebhookSubscriptionEvent",
                column: "WebhookSubscriptionId",
                principalTable: "WebHookEventCatalog",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookSubscriptionEvent_WebhookSubscription_WebhookSubscri~",
                table: "WebhookSubscriptionEvent",
                column: "WebhookSubscriptionId",
                principalTable: "WebhookSubscription",
                principalColumn: "Id");
        }
    }
}
