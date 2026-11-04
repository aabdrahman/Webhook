using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionVerificationTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookSubscriptionVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChallengeToken = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResponseDetails = table.Column<string>(type: "text", nullable: true),
                    ResponseStatusCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptionVerifications", x => x.Id);
                    table.CheckConstraint("CK_WebhookSubscriptionVerification_Status", "\"Status\" IN ('Pending', 'Failed', 'Success')");
                    table.ForeignKey(
                        name: "FK_WebhookSubscriptionVerifications_WebhookSubscriptions_Subsc~",
                        column: x => x.SubscriptionId,
                        principalTable: "WebhookSubscriptions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptionVerifications_CreatedAt",
                table: "WebhookSubscriptionVerifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptionVerifications_Status",
                table: "WebhookSubscriptionVerifications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptionVerifications_SubscriptionId",
                table: "WebhookSubscriptionVerifications",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookSubscriptionVerifications");
        }
    }
}
