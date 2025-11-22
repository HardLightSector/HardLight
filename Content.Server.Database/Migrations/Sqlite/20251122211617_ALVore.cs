using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ALVore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "al_content_preferences",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    preference_id = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_al_content_preferences", x => new { x.player_id, x.preference_id });
                    table.ForeignKey(
                        name: "FK_al_content_preferences_player_player_id",
                        column: x => x.player_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "al_vore_spaces",
                columns: table => new
                {
                    space_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    player_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    overlay = table.Column<string>(type: "TEXT", nullable: true),
                    mode = table.Column<int>(type: "INTEGER", nullable: false),
                    burn_damage = table.Column<double>(type: "REAL", nullable: false),
                    brute_damage = table.Column<double>(type: "REAL", nullable: false),
                    muffle_radio = table.Column<bool>(type: "INTEGER", nullable: false),
                    chance_to_escape = table.Column<int>(type: "INTEGER", nullable: false),
                    time_to_escape = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    can_taste = table.Column<bool>(type: "INTEGER", nullable: false),
                    insertion_verb = table.Column<string>(type: "TEXT", nullable: true),
                    release_verb = table.Column<string>(type: "TEXT", nullable: true),
                    fleshy = table.Column<bool>(type: "INTEGER", nullable: false),
                    internal_sound_loop = table.Column<bool>(type: "INTEGER", nullable: false),
                    insertion_sound = table.Column<string>(type: "TEXT", nullable: true),
                    release_sound = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_al_vore_spaces", x => x.space_id);
                    table.ForeignKey(
                        name: "FK_al_vore_spaces_player_player_id",
                        column: x => x.player_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_al_content_preferences_player_id",
                table: "al_content_preferences",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "IX_al_vore_spaces_player_id",
                table: "al_vore_spaces",
                column: "player_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "al_content_preferences");

            migrationBuilder.DropTable(
                name: "al_vore_spaces");
        }
    }
}
