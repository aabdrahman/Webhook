using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConstraintOnDeliveryAttemptDurationAndAttemptedCountAlsoChangeDurationToDouble : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "Duration",
                table: "WebhookDeliveryAttempts",
                type: "double precision",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryAttempt_AttemptedCountGreaterThanZero",
                table: "WebhookDeliveryAttempts",
                sql: "\"AttemptedCount\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryAttempt_DurationGreaterThanZero",
                table: "WebhookDeliveryAttempts",
                sql: "\"Duration\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryAttempt_AttemptedCountGreaterThanZero",
                table: "WebhookDeliveryAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryAttempt_DurationGreaterThanZero",
                table: "WebhookDeliveryAttempts");

            migrationBuilder.AlterColumn<long>(
                name: "Duration",
                table: "WebhookDeliveryAttempts",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "double precision");
        }
    }
}
