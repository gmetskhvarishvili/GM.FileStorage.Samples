using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GM.FileStorage.Sample.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tenant_id",
                schema: "file_storage",
                table: "stored_files",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // The tenant and the uploader were metadata rows. The tenant moves to the column (files without one were
            // the shared space); the uploader is the audit column created_by, filled from userId where it's empty and
            // the value is a user id. The rows then go.
            migrationBuilder.Sql("""
                UPDATE file_storage.stored_files f SET tenant_id = left(m.value, 64)
                FROM file_storage.stored_file_metadata m
                WHERE m.stored_file_id = f.id AND m.name = 'tenantId';
                UPDATE file_storage.stored_files SET tenant_id = 'public' WHERE tenant_id = '';
                UPDATE file_storage.stored_files f SET created_by = m.value::uuid
                FROM file_storage.stored_file_metadata m
                WHERE m.stored_file_id = f.id AND m.name = 'userId' AND f.created_by IS NULL
                  AND m.value ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$';
                DELETE FROM file_storage.stored_file_metadata WHERE name IN ('tenantId', 'userId');
                """);

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_tenant_id",
                schema: "file_storage",
                table: "stored_files",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to metadata rows (anon for files without an uploader).
            migrationBuilder.Sql("""
                INSERT INTO file_storage.stored_file_metadata (id, stored_file_id, name, value, created_at, is_active, is_deleted, is_hidden)
                SELECT gen_random_uuid(), id, 'tenantId', tenant_id, now(), true, false, false FROM file_storage.stored_files;
                INSERT INTO file_storage.stored_file_metadata (id, stored_file_id, name, value, created_at, is_active, is_deleted, is_hidden)
                SELECT gen_random_uuid(), id, 'userId', coalesce(created_by::text, 'anon'), now(), true, false, false FROM file_storage.stored_files;
                """);

            migrationBuilder.DropIndex(
                name: "ix_stored_files_tenant_id",
                schema: "file_storage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "file_storage",
                table: "stored_files");
        }
    }
}
