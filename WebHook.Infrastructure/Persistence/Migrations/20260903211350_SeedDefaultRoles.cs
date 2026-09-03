using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace WebHook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AspNetRoles",
                type: "timestamp with time zone",
                nullable: true,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Description", "IsActive", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), "ec511bd4-4853-426a-a2fc-751886560c9a", "Administrator role with full access", true, "Admin", "ADMIN" },
                    { new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"), "d1f2e3c4-b5a6-7890-cdef-123456789012", "Regular user role with limited access", true, "User", "USER" },
                    { new Guid("c3d4e5f6-a7b8-9012-cdef-123456789012"), "f1e2d3c4-b5a6-7890-cdef-123456789012", "System role with administrative privileges", true, "System", "SYSTEM" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"));

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"));

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("c3d4e5f6-a7b8-9012-cdef-123456789012"));

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AspNetRoles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldDefaultValueSql: "CURRENT_TIMESTAMP");
        }
    }
}
