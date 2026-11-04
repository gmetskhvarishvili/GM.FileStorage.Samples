using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GM.FileStorage.Sample.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropStoredFileMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stored_file_metadata",
                schema: "file_storage");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stored_file_metadata",
                schema: "file_storage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stored_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
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
        }
    }
}
