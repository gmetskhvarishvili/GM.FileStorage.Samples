using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GM.FileStorage.Sample.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "domain_events");

            migrationBuilder.EnsureSchema(
                name: "file_storage");

            migrationBuilder.CreateTable(
                name: "domain_events",
                schema: "domain_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_type = table.Column<string>(type: "text", nullable: true),
                    aggregate_id = table.Column<string>(type: "text", nullable: true),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    occurred_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel_id = table.Column<string>(type: "text", nullable: true),
                    culture = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_domain_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stored_files",
                schema: "file_storage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false),
                    key = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stored_file_metadata",
                schema: "file_storage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false),
                    stored_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_file_metadata", x => x.id);
                    table.ForeignKey(
                        name: "fk_stored_file_metadata_stored_file_stored_file_id",
                        column: x => x.stored_file_id,
                        principalSchema: "file_storage",
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_domain_events_aggregate_type_aggregate_id_occurred_on",
                schema: "domain_events",
                table: "domain_events",
                columns: new[] { "aggregate_type", "aggregate_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_domain_events_correlation_id",
                schema: "domain_events",
                table: "domain_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_domain_events_tenant_id",
                schema: "domain_events",
                table: "domain_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_domain_events_user_id",
                schema: "domain_events",
                table: "domain_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stored_file_metadata_name_value",
                schema: "file_storage",
                table: "stored_file_metadata",
                columns: new[] { "name", "value" });

            migrationBuilder.CreateIndex(
                name: "ix_stored_file_metadata_stored_file_id_name",
                schema: "file_storage",
                table: "stored_file_metadata",
                columns: new[] { "stored_file_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_key",
                schema: "file_storage",
                table: "stored_files",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_uploaded_at_utc",
                schema: "file_storage",
                table: "stored_files",
                column: "uploaded_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "domain_events",
                schema: "domain_events");

            migrationBuilder.DropTable(
                name: "stored_file_metadata",
                schema: "file_storage");

            migrationBuilder.DropTable(
                name: "stored_files",
                schema: "file_storage");
        }
    }
}
