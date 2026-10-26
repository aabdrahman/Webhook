using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientIdUniquenessOnDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookServiceClients_ClientId",
                table: "WebhookServiceClients");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookServiceClients_ClientId",
                table: "WebhookServiceClients",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookServiceClientEventCatalogs_EventCatalogId_ServiceCli~",
                table: "WebhookServiceClientEventCatalogs",
                columns: new[] { "EventCatalogId", "ServiceClientId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookServiceClients_ClientId",
                table: "WebhookServiceClients");

            migrationBuilder.DropIndex(
                name: "IX_WebhookServiceClientEventCatalogs_EventCatalogId_ServiceCli~",
                table: "WebhookServiceClientEventCatalogs");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookServiceClients_ClientId",
                table: "WebhookServiceClients",
                column: "ClientId");
        }
    }
}
