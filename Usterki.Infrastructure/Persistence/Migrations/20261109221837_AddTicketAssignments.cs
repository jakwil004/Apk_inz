using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Usterki.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedTechnicianId",
                table: "Tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TicketAssignmentHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousTechnicianId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewTechnicianId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketAssignmentHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketAssignmentHistories_AspNetUsers_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketAssignmentHistories_AspNetUsers_NewTechnicianId",
                        column: x => x.NewTechnicianId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketAssignmentHistories_AspNetUsers_PreviousTechnicianId",
                        column: x => x.PreviousTechnicianId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketAssignmentHistories_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_AssignedTechnicianId_CreatedAtUtc",
                table: "Tickets",
                columns: new[] { "AssignedTechnicianId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketAssignmentHistories_ChangedByUserId",
                table: "TicketAssignmentHistories",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketAssignmentHistories_NewTechnicianId",
                table: "TicketAssignmentHistories",
                column: "NewTechnicianId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketAssignmentHistories_PreviousTechnicianId",
                table: "TicketAssignmentHistories",
                column: "PreviousTechnicianId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketAssignmentHistories_TicketId_ChangedAtUtc",
                table: "TicketAssignmentHistories",
                columns: new[] { "TicketId", "ChangedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_AspNetUsers_AssignedTechnicianId",
                table: "Tickets",
                column: "AssignedTechnicianId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_AspNetUsers_AssignedTechnicianId",
                table: "Tickets");

            migrationBuilder.DropTable(
                name: "TicketAssignmentHistories");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_AssignedTechnicianId_CreatedAtUtc",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "AssignedTechnicianId",
                table: "Tickets");
        }
    }
}
