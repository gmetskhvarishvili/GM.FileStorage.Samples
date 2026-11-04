using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GM.FileStorage.Sample.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoredFileIntegrityAndDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                schema: "file_storage",
                table: "stored_files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "integrity_problem",
                schema: "file_storage",
                table: "stored_files",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "verified_at_utc",
                schema: "file_storage",
                table: "stored_files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_deleted_at_utc",
                schema: "file_storage",
                table: "stored_files",
                column: "deleted_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_verified_at_utc",
                schema: "file_storage",
                table: "stored_files",
                column: "verified_at_utc");

            // Files already deleted: their last update is the closest record of when, so retention counts from it.
            migrationBuilder.Sql(
                "UPDATE file_storage.stored_files SET deleted_at_utc = COALESCE(updated_at, created_at) WHERE is_deleted;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stored_files_deleted_at_utc",
                schema: "file_storage",
                table: "stored_files");

            migrationBuilder.DropIndex(
                name: "ix_stored_files_verified_at_utc",
                schema: "file_storage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                schema: "file_storage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "integrity_problem",
                schema: "file_storage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "verified_at_utc",
                schema: "file_storage",
                table: "stored_files");
        }
    }
}
