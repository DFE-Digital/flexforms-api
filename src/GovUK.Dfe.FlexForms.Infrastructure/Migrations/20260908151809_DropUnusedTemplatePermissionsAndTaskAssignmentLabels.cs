using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Drops leftover <c>ea.TemplatePermissions</c> (and its temporal history table)
    /// plus unused <c>ea.TaskAssignmentLabels</c>. Template grants already live in
    /// <c>ea.Permissions</c> as <c>ResourceType.Template</c> after
    /// <see cref="CopyTemplatePermissionsIntoPermissions"/>.
    /// </summary>
    public partial class DropUnusedTemplatePermissionsAndTaskAssignmentLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskAssignmentLabels",
                schema: "ea");

            migrationBuilder.DropTable(
                name: "TemplatePermissions",
                schema: "ea")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "History_TemplatePermissions")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "ea")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskAssignmentLabels",
                schema: "ea",
                columns: table => new
                {
                    TaskAssignmentLabelsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    TaskId = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAssignmentLabels", x => x.TaskAssignmentLabelsId);
                    table.ForeignKey(
                        name: "FK_TaskAssignmentLabels_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "ea",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskAssignmentLabels_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "ea",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemplatePermissions",
                schema: "ea",
                columns: table => new
                {
                    TemplatePermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessType = table.Column<byte>(type: "tinyint", nullable: false),
                    GrantedOn = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplatePermissions", x => x.TemplatePermissionId);
                    table.ForeignKey(
                        name: "FK_TemplatePermissions_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "ea",
                        principalTable: "Templates",
                        principalColumn: "TemplateId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TemplatePermissions_Users_GrantedBy",
                        column: x => x.GrantedBy,
                        principalSchema: "ea",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemplatePermissions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "ea",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "History_TemplatePermissions")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "ea")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignmentLabels_CreatedBy",
                schema: "ea",
                table: "TaskAssignmentLabels",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignmentLabels_UserId",
                schema: "ea",
                table: "TaskAssignmentLabels",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplatePermissions_GrantedBy",
                schema: "ea",
                table: "TemplatePermissions",
                column: "GrantedBy");

            migrationBuilder.CreateIndex(
                name: "IX_TemplatePermissions_TemplateId",
                schema: "ea",
                table: "TemplatePermissions",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplatePermissions_UserId_TemplateId",
                schema: "ea",
                table: "TemplatePermissions",
                columns: new[] { "UserId", "TemplateId" });
        }
    }
}
