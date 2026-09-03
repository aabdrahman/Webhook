using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDadLetterManualRetryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryCycle",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "WebhookDeadLetterQueues",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetriedAt",
                table: "WebhookDeadLetterQueues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetriedBy",
                table: "WebhookDeadLetterQueues",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetryJustification",
                table: "WebhookDeadLetterQueues",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryCycle",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "RetriedAt",
                table: "WebhookDeadLetterQueues");

            migrationBuilder.DropColumn(
                name: "RetriedBy",
                table: "WebhookDeadLetterQueues");

            migrationBuilder.DropColumn(
                name: "RetryJustification",
                table: "WebhookDeadLetterQueues");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "WebhookDeadLetterQueues",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);
        }
    }
}
