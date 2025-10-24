using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class ConsentV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_consent_settings_user_id",
                table: "consent_settings");

            migrationBuilder.AddColumn<DateTime>(
                name: "consent_freetext_updated_at",
                table: "consent_settings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "profile_id",
                table: "consent_settings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "consent_freetext_read_receipt",
                columns: table => new
                {
                    consent_freetext_read_receipt_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    read_consent_settings_id = table.Column<int>(type: "integer", nullable: false),
                    reader_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consent_freetext_read_receipt", x => x.consent_freetext_read_receipt_id);
                    table.ForeignKey(
                        name: "FK_consent_freetext_read_receipt_consent_settings_read_consent~",
                        column: x => x.read_consent_settings_id,
                        principalTable: "consent_settings",
                        principalColumn: "consent_settings_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consent_settings_profile_id",
                table: "consent_settings",
                column: "profile_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_consent_settings_user_id_profile_id",
                table: "consent_settings",
                columns: new[] { "user_id", "profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_consent_freetext_read_receipt_read_consent_settings_id",
                table: "consent_freetext_read_receipt",
                column: "read_consent_settings_id");

            migrationBuilder.CreateIndex(
                name: "IX_consent_freetext_read_receipt_reader_user_id_read_consent_s~",
                table: "consent_freetext_read_receipt",
                columns: new[] { "reader_user_id", "read_consent_settings_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_consent_settings_profile_profile_id",
                table: "consent_settings",
                column: "profile_id",
                principalTable: "profile",
                principalColumn: "profile_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_consent_settings_profile_profile_id",
                table: "consent_settings");

            migrationBuilder.DropTable(
                name: "consent_freetext_read_receipt");

            migrationBuilder.DropIndex(
                name: "IX_consent_settings_profile_id",
                table: "consent_settings");

            migrationBuilder.DropIndex(
                name: "IX_consent_settings_user_id_profile_id",
                table: "consent_settings");

            migrationBuilder.DropColumn(
                name: "consent_freetext_updated_at",
                table: "consent_settings");

            migrationBuilder.DropColumn(
                name: "profile_id",
                table: "consent_settings");

            migrationBuilder.CreateIndex(
                name: "IX_consent_settings_user_id",
                table: "consent_settings",
                column: "user_id",
                unique: true);
        }
    }
}
