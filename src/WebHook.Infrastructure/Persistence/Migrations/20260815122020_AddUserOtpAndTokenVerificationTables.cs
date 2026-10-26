using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserOtpAndTokenVerificationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OtpVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    OtpHash = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    IsConsumed = table.Column<bool>(type: "boolean", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OtpVerifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OtpOperationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Jti = table.Column<Guid>(type: "uuid", nullable: false),
                    OtpVerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OtpVerificationId1 = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpOperationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OtpOperationTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OtpOperationTokens_OtpVerifications_OtpVerificationId",
                        column: x => x.OtpVerificationId,
                        principalTable: "OtpVerifications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OtpOperationTokens_OtpVerifications_OtpVerificationId1",
                        column: x => x.OtpVerificationId1,
                        principalTable: "OtpVerifications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OtpOperationTokens_CreatedAt",
                table: "OtpOperationTokens",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OtpOperationTokens_ExpiresAt",
                table: "OtpOperationTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OtpOperationTokens_Jti",
                table: "OtpOperationTokens",
                column: "Jti",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OtpOperationTokens_OtpVerificationId",
                table: "OtpOperationTokens",
                column: "OtpVerificationId");

            migrationBuilder.CreateIndex(
                name: "IX_OtpOperationTokens_OtpVerificationId1",
                table: "OtpOperationTokens",
                column: "OtpVerificationId1");

            migrationBuilder.CreateIndex(
                name: "IX_OtpOperationTokens_UserId",
                table: "OtpOperationTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OtpVerifications_CreatedAt",
                table: "OtpVerifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OtpVerifications_ExpiresAt",
                table: "OtpVerifications",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OtpVerifications_UserId",
                table: "OtpVerifications",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OtpOperationTokens");

            migrationBuilder.DropTable(
                name: "OtpVerifications");
        }
    }
}
