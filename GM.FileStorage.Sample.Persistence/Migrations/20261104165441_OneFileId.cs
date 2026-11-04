using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GM.FileStorage.Sample.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneFileId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A file is its id: the key column only ever repeated it (GM.FileStorage 3.0 removes it).
            migrationBuilder.DropIndex(
                name: "ix_stored_files_key",
                schema: "file_storage",
                table: "stored_files");

            migrationBuilder.DropColumn(
                name: "key",
                schema: "file_storage",
                table: "stored_files");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "key",
                schema: "file_storage",
                table: "stored_files",
                type: "character varying(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            // The key was the id, as text.
            migrationBuilder.Sql("UPDATE file_storage.stored_files SET key = id::text;");

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_key",
                schema: "file_storage",
                table: "stored_files",
                column: "key",
                unique: true);
        }
    }
}
