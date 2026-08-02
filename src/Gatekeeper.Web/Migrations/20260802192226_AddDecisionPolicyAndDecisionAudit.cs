using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gatekeeper.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDecisionPolicyAndDecisionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "AuditLog",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Resource",
                table: "AuditLog",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "AuditLog",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

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
                name: "IX_AuditLog_Subject_Resource",
                table: "AuditLog",
                columns: new[] { "Subject", "Resource" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyAssignments_Subject_RoleName",
                table: "PolicyAssignments",
                columns: new[] { "Subject", "RoleName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyGrants_Subject_Action_Resource",
                table: "PolicyGrants",
                columns: new[] { "Subject", "Action", "Resource" });

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
                name: "PolicyAssignments");

            migrationBuilder.DropTable(
                name: "PolicyGrants");

            migrationBuilder.DropTable(
                name: "PolicyRoles");

            migrationBuilder.DropIndex(
                name: "IX_AuditLog_Subject_Resource",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "Resource",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "AuditLog");
        }
    }
}
