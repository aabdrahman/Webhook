using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceClientTableImplementatation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookServiceClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ServiceClientName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ClientKey = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    DeactivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeactivatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookServiceClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookServiceClientEventCatalogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventCatalogId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeactivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeactivatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookServiceClientEventCatalogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookServiceClientEventCatalogs_WebHookEventCatalogs_Even~",
                        column: x => x.EventCatalogId,
                        principalTable: "WebHookEventCatalogs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WebhookServiceClientEventCatalogs_WebhookServiceClients_Ser~",
                        column: x => x.ServiceClientId,
                        principalTable: "WebhookServiceClients",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreatedAt",
                table: "WebhookServiceClientEventCatalogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceClient_CatalogId",
                table: "WebhookServiceClientEventCatalogs",
                column: "EventCatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceClient_ServiceCleintId",
                table: "WebhookServiceClientEventCatalogs",
                column: "ServiceClientId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookServiceClients_ClientId",
                table: "WebhookServiceClients",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookServiceClients_CreatedAt",
                table: "WebhookServiceClients",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookServiceClients_ServiceClientName",
                table: "WebhookServiceClients",
                column: "ServiceClientName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookServiceClientEventCatalogs");

            migrationBuilder.DropTable(
                name: "WebhookServiceClients");
        }
    }
}
