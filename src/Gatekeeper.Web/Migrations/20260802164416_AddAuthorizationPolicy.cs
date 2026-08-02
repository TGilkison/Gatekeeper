using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatekeeper.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizationPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DecisionAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Resource = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolicyAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RoleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolicyGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Resource = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Effect = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PolicyRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyRoles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionAudit_Subject_Resource",
                table: "DecisionAudit",
                columns: new[] { "Subject", "Resource" });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionAudit_Timestamp",
                table: "DecisionAudit",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyAssignments_Subject",
                table: "PolicyAssignments",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyGrants_Action_Resource",
                table: "PolicyGrants",
                columns: new[] { "Action", "Resource" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyRoles_Name",
                table: "PolicyRoles",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisionAudit");

            migrationBuilder.DropTable(
                name: "PolicyAssignments");

            migrationBuilder.DropTable(
                name: "PolicyGrants");

            migrationBuilder.DropTable(
                name: "PolicyRoles");
        }
    }
}
