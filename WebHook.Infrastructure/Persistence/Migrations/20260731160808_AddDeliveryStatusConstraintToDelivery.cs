using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryStatusConstraintToDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_WebhookDelivery_DeliveredSttatus",
                table: "WebhookDeliveries",
                sql: "\"DeliveryStatus\" != 'Delivered' OR \"DeliveredAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WebhookDelivery_DeliveredSttatus",
                table: "WebhookDeliveries");
        }
    }
}
