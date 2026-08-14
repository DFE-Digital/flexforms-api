using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationDeletionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                schema: "ea",
                table: "Applications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedOn",
                schema: "ea",
                table: "Applications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreDeletedStatus",
                schema: "ea",
                table: "Applications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_DeletedBy",
                schema: "ea",
                table: "Applications",
                column: "DeletedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_Applications_Users_DeletedBy",
                schema: "ea",
                table: "Applications",
                column: "DeletedBy",
                principalSchema: "ea",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Applications_Users_DeletedBy",
                schema: "ea",
                table: "Applications");

            migrationBuilder.DropIndex(
                name: "IX_Applications_DeletedBy",
                schema: "ea",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                schema: "ea",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "DeletedOn",
                schema: "ea",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "PreDeletedStatus",
                schema: "ea",
                table: "Applications");
        }
    }
}
