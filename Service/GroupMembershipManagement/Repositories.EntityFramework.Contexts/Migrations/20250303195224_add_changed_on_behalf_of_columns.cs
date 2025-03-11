using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <inheritdoc />
    public partial class add_changed_on_behalf_of_columns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangedOnBehalfOfDisplayName",
                table: "SyncJobChanges",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChangedOnBehalfOfObjectId",
                table: "SyncJobChanges",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
            $@"
                ALTER TABLE SyncJobChanges ALTER COLUMN ChangeSource INT NULL;
                ALTER TABLE SyncJobChanges ALTER COLUMN ChangedByDisplayName NVARCHAR(MAX) NULL;
                ALTER TABLE SyncJobChanges ALTER COLUMN ChangedByObjectId UNIQUEIDENTIFIER NULL;
                ALTER TABLE SyncJobChanges ALTER COLUMN ChangeDetails NVARCHAR(MAX) NULL;

                -- For jobs with at least one record in SyncJobChanges, update

                WITH MostRecentChange AS (
                    SELECT
                        sjc.SyncJobId,
                        MAX(sjc.ChangeTime) AS MostRecentChangeTime
                    FROM SyncJobChanges sjc
                    WHERE sjc.ChangeReason IN ('Onboarding', 'Update')
                    GROUP BY sjc.SyncJobId
                )
                UPDATE sjc
                SET sjc.ChangedOnBehalfOfDisplayName = sj.Requestor
                FROM SyncJobChanges sjc
                INNER JOIN MostRecentChange mrc ON sjc.SyncJobId = mrc.SyncJobId
                    AND sjc.ChangeTime = mrc.MostRecentChangeTime
                INNER JOIN SyncJobs sj ON sj.Id = sjc.SyncJobId
                WHERE sjc.ChangeReason IN ('Onboarding', 'Update')
                    AND sjc.ChangedOnBehalfOfDisplayName IS NULL
                    AND sjc.ChangedByDisplayName <> sj.Requestor;

                -- For jobs in Idle, InProgress statuses with no records in SyncJobChanges, insert 2 records

                INSERT INTO SyncJobChanges (SyncJobId, ChangeReason, ChangedOnBehalfOfDisplayName, ChangeTime)
                SELECT sj.Id, 'Onboarding', sj.Requestor, sj.InitialOnboardingDate
                FROM SyncJobs sj
                WHERE sj.Status IN ('Idle', 'InProgress')
                    AND NOT EXISTS (
                        SELECT 1 FROM SyncJobChanges sjc WHERE sjc.SyncJobId = sj.Id
                    )
                UNION ALL
                SELECT sj.Id, 'SubmissionApproved', NULL, DATEADD(DAY, 1, sj.InitialOnboardingDate)
                FROM SyncJobs sj
                WHERE sj.Status IN ('Idle', 'InProgress')
                    AND NOT EXISTS (
                        SELECT 1 FROM SyncJobChanges sjc WHERE sjc.SyncJobId = sj.Id
                    );

                -- For jobs in PendingReview status with no records in SyncJobChanges, insert one record

                INSERT INTO SyncJobChanges (SyncJobId, ChangeReason, ChangedOnBehalfOfDisplayName, ChangeTime)
                SELECT sj.Id, 'Onboarding', sj.Requestor, sj.InitialOnboardingDate
                FROM SyncJobs sj
                WHERE sj.Status = 'PendingReview'
                    AND NOT EXISTS (
                        SELECT 1 FROM SyncJobChanges sjc WHERE sjc.SyncJobId = sj.Id
                    );

                -- For jobs in SubmissionRejected status with no records in SyncJobChanges, insert 2 records

                INSERT INTO SyncJobChanges (SyncJobId, ChangeReason, ChangedOnBehalfOfDisplayName, ChangeTime)
                -- Insert 'Onboarding'
                SELECT sj.Id, 'Onboarding', sj.Requestor, sj.InitialOnboardingDate
                FROM SyncJobs sj
                WHERE sj.Status = 'SubmissionRejected'
                    AND NOT EXISTS (
                        SELECT 1 FROM SyncJobChanges sjc WHERE sjc.SyncJobId = sj.Id
                    )

                UNION ALL

                -- Insert 'SubmissionRejected'
                SELECT sj.Id, 'SubmissionRejected', NULL, DATEADD(DAY, 1, sj.InitialOnboardingDate)
                FROM SyncJobs sj
                WHERE sj.Status = 'SubmissionRejected'
                    AND NOT EXISTS (
                        SELECT 1 FROM SyncJobChanges sjc WHERE sjc.SyncJobId = sj.Id
                    );

                -- For jobs not in 'Idle', 'InProgress', 'PendingReview', 'SubmissionRejected' statuses with no records, insert 2 records

                INSERT INTO SyncJobChanges (SyncJobId, ChangeReason, ChangedOnBehalfOfDisplayName, ChangeTime)
                -- Insert 'Onboarding'
                SELECT sj.Id, 'Onboarding', sj.Requestor, sj.InitialOnboardingDate
                FROM SyncJobs sj
                WHERE sj.Status NOT IN ('Idle', 'InProgress', 'PendingReview', 'SubmissionRejected')
                    AND NOT EXISTS (
                        SELECT 1 FROM SyncJobChanges sjc WHERE sjc.SyncJobId = sj.Id
                    )

                UNION ALL

                -- Insert 'StatusUpdate'
                SELECT sj.Id, 'StatusUpdate', NULL, DATEADD(DAY, 1, sj.InitialOnboardingDate)
                FROM SyncJobs sj
                WHERE sj.Status NOT IN ('Idle', 'InProgress', 'PendingReview', 'SubmissionRejected')
                    AND NOT EXISTS (
                        SELECT 1 FROM SyncJobChanges sjc WHERE sjc.SyncJobId = sj.Id
                    );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
            $@"
                DELETE FROM [dbo].[SyncJobChanges] WHERE ChangedByDisplayName IS NULL;
            ");

            migrationBuilder.DropColumn(
                name: "ChangedOnBehalfOfDisplayName",
                table: "SyncJobChanges");

            migrationBuilder.DropColumn(
                name: "ChangedOnBehalfOfObjectId",
                table: "SyncJobChanges");
        }
    }
}