using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHook.Infrastructure.Persistence.ApplicationCache.Migrations
{
    /// <inheritdoc />
    public partial class SetDbOptimizationExeution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-20000; PRAGMA mmap_size=268435456");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
