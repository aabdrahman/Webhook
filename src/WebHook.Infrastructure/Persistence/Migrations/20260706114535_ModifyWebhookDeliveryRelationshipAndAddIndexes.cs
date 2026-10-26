using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModifyWebhookDeliveryRelationshipAndAddIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveries_WebHookEventCatalogs_WebhookEventCatalogId",
                table: "WebhookDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveries_WebhookSubscriptions_WebhookSubscriptionId",
                table: "WebhookDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_WebhookDeliveries_WebhookEventCatalogId",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "WebhookEventCatalogId",
                table: "WebhookDeliveries");

            migrationBuilder.RenameColumn(
                name: "WebhookSubscriptionId",
                table: "WebhookDeliveries",
                newName: "WebhookSubscriptionEventId");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDeliveries_WebhookSubscriptionId",
                table: "WebhookDeliveries",
                newName: "IX_WebhookDeliveries_WebhookSubscriptionEventId");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "WebhookEventSubscriptions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "WebhookEventSubscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "WebhookEventSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "CallBackUrl",
                table: "WebhookDeliveries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEventSubscriptions_IsActive",
                table: "WebhookEventSubscriptions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_DeliveryStatus",
                table: "WebhookDeliveries",
                column: "DeliveryStatus");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebhookDelivery_RetryCount",
                table: "WebhookDeliveries",
                sql: "\"RetryCount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebhookDelivery_Status",
                table: "WebhookDeliveries",
                sql: "\"DeliveryStatus\" IN ('Pending', 'Processing', 'Delivered', 'Failed', 'Retrying', 'DeadLetter')");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveries_WebhookEventSubscriptions_WebhookSubscrip~",
                table: "WebhookDeliveries",
                column: "WebhookSubscriptionEventId",
                principalTable: "WebhookEventSubscriptions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveries_WebhookEventSubscriptions_WebhookSubscrip~",
                table: "WebhookDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_WebhookEventSubscriptions_IsActive",
                table: "WebhookEventSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_WebhookDeliveries_DeliveryStatus",
                table: "WebhookDeliveries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WebhookDelivery_RetryCount",
                table: "WebhookDeliveries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WebhookDelivery_Status",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "WebhookEventSubscriptions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "WebhookEventSubscriptions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "WebhookEventSubscriptions");

            migrationBuilder.DropColumn(
                name: "CallBackUrl",
                table: "WebhookDeliveries");

            migrationBuilder.RenameColumn(
                name: "WebhookSubscriptionEventId",
                table: "WebhookDeliveries",
                newName: "WebhookSubscriptionId");

            migrationBuilder.RenameIndex(
                name: "IX_WebhookDeliveries_WebhookSubscriptionEventId",
                table: "WebhookDeliveries",
                newName: "IX_WebhookDeliveries_WebhookSubscriptionId");

            migrationBuilder.AddColumn<Guid>(
                name: "WebhookEventCatalogId",
                table: "WebhookDeliveries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_WebhookEventCatalogId",
                table: "WebhookDeliveries",
                column: "WebhookEventCatalogId");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveries_WebHookEventCatalogs_WebhookEventCatalogId",
                table: "WebhookDeliveries",
                column: "WebhookEventCatalogId",
                principalTable: "WebHookEventCatalogs",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveries_WebhookSubscriptions_WebhookSubscriptionId",
                table: "WebhookDeliveries",
                column: "WebhookSubscriptionId",
                principalTable: "WebhookSubscriptions",
                principalColumn: "Id");
        }
    }
}
