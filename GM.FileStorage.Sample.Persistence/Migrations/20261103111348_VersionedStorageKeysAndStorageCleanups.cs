using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GM.FileStorage.Sample.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VersionedStorageKeysAndStorageCleanups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "storage_key",
                schema: "file_storage",
                table: "stored_files",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            // Files uploaded before versioned storage keys live at their own key.
            migrationBuilder.Sql("UPDATE file_storage.stored_files SET storage_key = key;");

            migrationBuilder.CreateTable(
                name: "storage_cleanups",
                schema: "file_storage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    due_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_cleanups", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_storage_key",
                schema: "file_storage",
                table: "stored_files",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_storage_cleanups_due_at_utc",
                schema: "file_storage",
                table: "storage_cleanups",
                column: "due_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_storage_cleanups_storage_key",
                schema: "file_storage",
                table: "storage_cleanups",
                column: "storage_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storage_cleanups",
                schema: "file_storage");

            migrationBuilder.DropIndex(
                name: "ix_stored_files_storage_key",
                schema: "file_storage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "storage_key",
                schema: "file_storage",
                table: "stored_files");
        }
    }
}
