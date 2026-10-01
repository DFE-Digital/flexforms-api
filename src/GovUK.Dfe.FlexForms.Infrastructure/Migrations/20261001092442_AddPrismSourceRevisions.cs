using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovUK.Dfe.FlexForms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrismSourceRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SourceRevision",
                schema: "ea",
                table: "Applications",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SubmittedRevision",
                schema: "ea",
                table: "Applications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CreatedAtRevision",
                schema: "ea",
                table: "ApplicationResponses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            if (migrationBuilder.ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
                return;

            // Backfill. Historic ordering between late saves and the submit is unknown, so responses are
            // numbered first, then the submit (if the application was ever submitted), then the delete.
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT ResponseId,
                           ROW_NUMBER() OVER (PARTITION BY ApplicationId ORDER BY CreatedOn, ResponseId) AS Revision
                    FROM ea.ApplicationResponses
                )
                UPDATE r
                SET CreatedAtRevision = n.Revision
                FROM ea.ApplicationResponses r
                JOIN numbered n ON n.ResponseId = r.ResponseId;
                """);

            // Status: 1 = Submitted, 2 = Deleted. The temporal history tells us whether a deleted application was submitted first.
            migrationBuilder.Sql("""
                WITH stats AS (
                    SELECT a.ApplicationId,
                           ISNULL((SELECT MAX(r.CreatedAtRevision) FROM ea.ApplicationResponses r WHERE r.ApplicationId = a.ApplicationId), 0) AS ResponseRevision,
                           CASE WHEN a.Status = 1 OR EXISTS (
                                    SELECT 1 FROM ea.Applications FOR SYSTEM_TIME ALL h
                                    WHERE h.ApplicationId = a.ApplicationId AND h.Status = 1)
                                THEN 1 ELSE 0 END AS WasSubmitted,
                           CASE WHEN a.Status = 2 THEN 1 ELSE 0 END AS IsDeleted
                    FROM ea.Applications a
                )
                UPDATE a
                SET SubmittedRevision = CASE WHEN s.WasSubmitted = 1 THEN s.ResponseRevision + 1 END,
                    SourceRevision = s.ResponseRevision + s.WasSubmitted + s.IsDeleted
                FROM ea.Applications a
                JOIN stats s ON s.ApplicationId = a.ApplicationId;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceRevision",
                schema: "ea",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "SubmittedRevision",
                schema: "ea",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "CreatedAtRevision",
                schema: "ea",
                table: "ApplicationResponses");
        }
    }
}
