using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Usterki.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketHistoryAndComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Tickets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "TicketComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketComments_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketComments_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TicketStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketStatusHistories_AspNetUsers_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketStatusHistories_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketComments_AuthorId",
                table: "TicketComments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketComments_TicketId_CreatedAtUtc",
                table: "TicketComments",
                columns: new[] { "TicketId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketStatusHistories_ChangedByUserId",
                table: "TicketStatusHistories",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketStatusHistories_TicketId_ChangedAtUtc",
                table: "TicketStatusHistories",
                columns: new[] { "TicketId", "ChangedAtUtc" });
            migrationBuilder.Sql("""
                UPDATE "Tickets"
                SET "Version" = 1;

                INSERT INTO "TicketStatusHistories"
                (
                    "Id",
                    "TicketId",
                    "FromStatus",
                    "ToStatus",
                    "ChangedByUserId",
                    "Reason",
                    "ChangedAtUtc"
                )
                SELECT
                    "Id",
                    "Id",
                    NULL,
                    "Status",
                    "ReporterId",
                    'Utworzenie zgłoszenia.',
                    "CreatedAtUtc"
                FROM "Tickets";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketComments");

            migrationBuilder.DropTable(
                name: "TicketStatusHistories");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Tickets");
        }
    }
}
