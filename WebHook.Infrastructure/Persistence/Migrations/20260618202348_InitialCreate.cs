using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    PayLoad = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebHookEventCatalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NormalizedEventName = table.Column<string>(type: "text", nullable: false, computedColumnSql: "UPPER([EventName])"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Description = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableFields = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebHookEventCatalog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookSubscription",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CallbackUrl = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SecretKey = table.Column<string>(type: "text", nullable: false),
                    SubscribedFields = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscription", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDelivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RequestPayload = table.Column<string>(type: "text", nullable: false),
                    DeliveryStatus = table.Column<string>(type: "text", nullable: false),
                    WebhookSubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WebhookEventCatalogId = table.Column<Guid>(type: "uuid", nullable: false),
                    WebhookEventId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDelivery", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDelivery_WebHookEventCatalog_WebhookEventCatalogId",
                        column: x => x.WebhookEventCatalogId,
                        principalTable: "WebHookEventCatalog",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WebhookDelivery_WebhookEvent_WebhookEventId",
                        column: x => x.WebhookEventId,
                        principalTable: "WebhookEvent",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WebhookDelivery_WebhookSubscription_WebhookSubscriptionId",
                        column: x => x.WebhookSubscriptionId,
                        principalTable: "WebhookSubscription",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WebhookSubscriptionEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WebhookSubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WebhookEventCatalogId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptionEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookSubscriptionEvent_WebHookEventCatalog_WebhookSubscri~",
                        column: x => x.WebhookSubscriptionId,
                        principalTable: "WebHookEventCatalog",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WebhookSubscriptionEvent_WebhookSubscription_WebhookSubscri~",
                        column: x => x.WebhookSubscriptionId,
                        principalTable: "WebhookSubscription",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeadLetterQueue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    WebhookDeliveryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeadLetterQueue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeadLetterQueue_WebhookDelivery_WebhookDeliveryId",
                        column: x => x.WebhookDeliveryId,
                        principalTable: "WebhookDelivery",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveryAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HttpResponse = table.Column<string>(type: "text", nullable: false),
                    HttpResponseCode = table.Column<string>(type: "text", nullable: false),
                    Duration = table.Column<long>(type: "bigint", nullable: false),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptedCount = table.Column<int>(type: "integer", nullable: false),
                    WebhookDeliveryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveryAttempt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveryAttempt_WebhookDelivery_WebhookDeliveryId",
                        column: x => x.WebhookDeliveryId,
                        principalTable: "WebhookDelivery",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Webhook_DeadLetter_DeliveryId",
                table: "WebhookDeadLetterQueue",
                column: "WebhookDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDelivery_WebhookEventCatalogId",
                table: "WebhookDelivery",
                column: "WebhookEventCatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDelivery_WebhookEventId",
                table: "WebhookDelivery",
                column: "WebhookEventId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDelivery_WebhookSubscriptionId",
                table: "WebhookDelivery",
                column: "WebhookSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveryAttempt_WebhookDeliveryId",
                table: "WebhookDeliveryAttempt",
                column: "WebhookDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_Webhook_Event_CorrelationId",
                table: "WebhookEvent",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Webhook_Event_EventType",
                table: "WebhookEvent",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_WebHookEventCatalog_NormalizedEventName",
                table: "WebHookEventCatalog",
                column: "NormalizedEventName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Webhook_Event_Subscription",
                table: "WebhookSubscriptionEvent",
                columns: new[] { "WebhookEventCatalogId", "WebhookSubscriptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptionEvent_WebhookSubscriptionId",
                table: "WebhookSubscriptionEvent",
                column: "WebhookSubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookDeadLetterQueue");

            migrationBuilder.DropTable(
                name: "WebhookDeliveryAttempt");

            migrationBuilder.DropTable(
                name: "WebhookSubscriptionEvent");

            migrationBuilder.DropTable(
                name: "WebhookDelivery");

            migrationBuilder.DropTable(
                name: "WebHookEventCatalog");

            migrationBuilder.DropTable(
                name: "WebhookEvent");

            migrationBuilder.DropTable(
                name: "WebhookSubscription");
        }
    }
}
