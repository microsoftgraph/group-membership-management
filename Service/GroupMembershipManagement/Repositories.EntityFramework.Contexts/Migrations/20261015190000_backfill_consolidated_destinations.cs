using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <summary>
    /// Backfills legacy identities, group names, and group email, skipping NULL channel IDs and preserving existing rows.
    /// </summary>
    [DbContext(typeof(GMMContext))]
    [Migration("20261015190000_backfill_consolidated_destinations")]
    public partial class backfill_consolidated_destinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO [Destinations] ([SyncJobId], [DestinationType])
SELECT s.[Id], s.[MembershipType]
FROM [SyncJobs] s
WHERE ((s.[MembershipType] = 'GroupMembership'
        AND EXISTS (SELECT 1 FROM [Groups] g WHERE g.[SyncJobId] = s.[Id]))
    OR (s.[MembershipType] = 'TeamsChannelMembership'
        AND EXISTS (SELECT 1 FROM [TeamsChannels] c WHERE c.[SyncJobId] = s.[Id] AND c.[ChannelId] IS NOT NULL)))
  AND NOT EXISTS (SELECT 1 FROM [Destinations] d WHERE d.[SyncJobId] = s.[Id]);");

            migrationBuilder.Sql(@"
INSERT INTO [GroupDestinations] ([SyncJobId], [GroupId], [Name], [Email])
SELECT g.[SyncJobId], g.[GroupId], n.[Name], e.[Email]
FROM [Groups] g
INNER JOIN [Destinations] d ON d.[SyncJobId] = g.[SyncJobId] AND d.[DestinationType] = 'GroupMembership'
LEFT JOIN [DestinationNames] n ON n.[Id] = g.[SyncJobId]
LEFT JOIN [DestinationEmail] e ON e.[Id] = g.[SyncJobId]
WHERE NOT EXISTS (SELECT 1 FROM [GroupDestinations] gd WHERE gd.[SyncJobId] = g.[SyncJobId]);");

            // Legacy channel names are ambiguous; DestinationAttributesUpdater refills both names.
            migrationBuilder.Sql(@"
INSERT INTO [TeamsChannelDestinations] ([SyncJobId], [TeamId], [ChannelId], [TeamName], [ChannelName])
SELECT c.[SyncJobId], c.[GroupId], c.[ChannelId], NULL, NULL
FROM [TeamsChannels] c
INNER JOIN [Destinations] d ON d.[SyncJobId] = c.[SyncJobId] AND d.[DestinationType] = 'TeamsChannelMembership'
WHERE c.[ChannelId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [TeamsChannelDestinations] td WHERE td.[SyncJobId] = c.[SyncJobId]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Backfilled rows cannot be distinguished safely from dual-written rows.
        }
    }
}
