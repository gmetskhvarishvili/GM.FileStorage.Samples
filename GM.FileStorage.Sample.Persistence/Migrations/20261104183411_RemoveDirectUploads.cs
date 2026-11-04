using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GM.FileStorage.Sample.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDirectUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "direct_uploads",
                schema: "file_storage");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "direct_uploads",
                schema: "file_storage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    user_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_direct_uploads", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_direct_uploads_expires_at_utc",
                schema: "file_storage",
                table: "direct_uploads",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_direct_uploads_storage_key",
                schema: "file_storage",
                table: "direct_uploads",
                column: "storage_key",
                unique: true);
        }
    }
}
